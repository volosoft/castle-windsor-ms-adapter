using System;
using System.Linq;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Handlers;
using Castle.MicroKernel.LifecycleConcerns;
using Castle.MicroKernel.ModelBuilder;
using Castle.MicroKernel.ComponentActivator;
using System.Runtime.CompilerServices;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class MsOwnedLifecycleContributor(bool factory) : IComponentModelDescriptor, IMetaComponentModelDescriptor
{
    internal const string Marker = "MsOwnedLifecycle";
    public void BuildComponentModel(IKernel kernel, ComponentModel model)
    {
        model.ExtendedProperties[Marker] = model.Lifecycle.DecommissionConcerns.ToArray();
        if (factory) model.Implementation = typeof(Castle.Core.Internal.LateBoundComponent);
    }

    public void ConfigureComponentModel(IKernel kernel, ComponentModel model)
    {
        var scopeResolution = new MsOwnedScopeResolution();
        model.ResolveExtensions(true).Add(scopeResolution);
        model.ReleaseExtensions(true).Add(scopeResolution);
        if (!ConfigureDecommission(model)) return;
        if (model.CustomComponentActivator == typeof(FactoryMethodActivator<object>))
        {
            model.ExtendedProperties[NullFactoryResult.FactoryMarker] = true;
            model.ResolveExtensions(true).Add(new NullFactoryResolution());
            model.CustomComponentActivator = typeof(MsOwnedFactoryActivator);
        }
    }

    // Windsor rebuilds closed generic models and reapplies meta descriptors, not registration descriptors.
    void IMetaComponentModelDescriptor.ConfigureComponentModel(IKernel kernel, ComponentModel model)
        => ConfigureDecommission(model);

    private static bool ConfigureDecommission(ComponentModel model)
    {
        var concerns = model.Lifecycle.DecommissionConcerns.ToArray();
        if (concerns.Length == 0 && !typeof(IAsyncDisposable).IsAssignableFrom(model.Implementation)) return false;
        foreach (var concern in concerns) model.Lifecycle.Remove(concern);
        model.Lifecycle.Add(new MsDecommissionConcern(concerns));
        return true;
    }
}

internal sealed class MsOwnedScopeResolution : IResolveExtension, IReleaseExtension
{
    [ThreadStatic]
    internal static CreationContext CurrentContext;
    private IKernel _kernel;
    private bool _singleton;
    private ComponentModel _model;
    private bool _mayDisposeAsync;
    private readonly ConditionalWeakTable<Burden, ScopeActivation> _activations = new();
    public void Init(IKernel kernel, IHandler handler)
    {
        _kernel = kernel;
        _model = handler.ComponentModel;
        _mayDisposeAsync = typeof(IAsyncDisposable).IsAssignableFrom(_model.Implementation) ||
            _model.CustomComponentActivator != null && _model.CustomComponentActivator != typeof(MsValueTypeActivator);
        _singleton = handler.ComponentModel.CustomLifestyle != null &&
            typeof(Castle.MicroKernel.Lifestyle.SingletonLifestyleManager).IsAssignableFrom(handler.ComponentModel.CustomLifestyle);
    }

    public void Intercept(ResolveInvocation invocation)
    {
        var scope = _singleton ? _kernel.Resolve<GlobalMsLifetimeScope>() : MsLifetimeScopeProvider.SelectScope(_kernel, invocation.Context);
        using var lifetime = MsLifetimeScope.Using(scope);
        var scopeOwner = MsScopeState.Resolve(_kernel, scope);
        // Mark before checking closure, so async publication cannot race a synchronous shutdown.
        if (_mayDisposeAsync) scopeOwner?.RequireAsyncDisposal();
        ObjectDisposedException.ThrowIf(scopeOwner?.IsDisposed == true, scopeOwner);
        var parent = invocation.Context.SelectScopeRoot(handlers => handlers.LastOrDefault())?.Burden;
        if (parent?.Model.Implementation == typeof(ScopedResolution)) parent = null;
        var previous = invocation.Context.ReleasePolicy;
        var previousContext = CurrentContext;
        CurrentContext = invocation.Context;
        if (scopeOwner != null) invocation.Context.ReleasePolicy = scopeOwner.ReleasePolicy;
        try
        {
            invocation.Proceed();
            if (invocation.Burden is { } burden && scopeOwner != null)
            {
                var activation = _activations.GetValue(burden, value =>
                {
                    var record = new ScopeActivation(scopeOwner, value, parent);
                    record.Observe();
                    return record;
                });
                if (scopeOwner.IsDisposed)
                {
                    MsAsyncDisposal.ReleaseLate(activation.Instance, activation.Release);
                    throw new ObjectDisposedException(nameof(MsLifetimeScope));
                }
                if (burden.RequiresPolicyRelease) scopeOwner.AddActivation(activation);
            }
            if (parent != null)
            {
                var ownerModel = _kernel.GetHandler(parent.Model.Name)?.ComponentModel ?? parent.Model;
                var dependency = _kernel.GetHandler(_model.Name)?.ComponentModel ?? _model;
                if (!ReferenceEquals(ownerModel, dependency) && !ownerModel.Dependents.Contains(dependency)) ownerModel.AddDependent(dependency);
            }
        }
        finally
        {
            invocation.Context.ReleasePolicy = previous;
            CurrentContext = previousContext;
        }
    }

    public void Intercept(ReleaseInvocation invocation)
    {
        if (_activations.TryGetValue(invocation.Burden, out var activation) &&
            (activation.IsReleased || !activation.Scope.IsDisposed && activation.Owner is { Instance: null }))
        {
            invocation.ReturnValue = false;
            return;
        }
        invocation.Proceed();
    }
}

internal sealed class MsOwnedDefaults : IContributeComponentModelConstruction
{
    public void ProcessModel(IKernel kernel, ComponentModel model)
    {
        if (model.ExtendedProperties[MsOwnedLifecycleContributor.Marker] is not IDecommissionConcern[] previous) return;
        foreach (var concern in model.Lifecycle.DecommissionConcerns.Except(previous).ToArray())
        {
            IDecommissionConcern replacement = concern is DisposalConcern ? MsDisposalConcern.Instance
                : concern.GetType() == typeof(LateBoundDecommissionConcerns) ? new MsLateBoundDisposal() : concern;
            if (ReferenceEquals(concern, replacement)) continue;
            model.Lifecycle.Remove(concern);
            model.Lifecycle.Add(replacement);
        }
        if (!model.Lifecycle.HasDecommissionConcerns && typeof(IAsyncDisposable).IsAssignableFrom(model.Implementation))
            model.Lifecycle.Add(MsDisposalConcern.Instance);
    }
}

internal sealed class MsOwnedFactoryActivator : FactoryMethodActivator<object>
{
    public MsOwnedFactoryActivator(ComponentModel model, IKernelInternal kernel,
        ComponentInstanceDelegate created, ComponentInstanceDelegate destroyed) : base(model, kernel, created, destroyed) { }

    protected override object Instantiate(CreationContext context)
    {
        var instance = creator(Kernel, Model, context) ?? new NullFactoryResult();
        if (instance is not NullFactoryResult && Kernel.ProxyFactory.ShouldCreateProxy(Model) &&
            !Castle.DynamicProxy.ProxyUtil.IsProxy(instance))
            instance = Kernel.ProxyFactory.Create(Kernel, instance, Model, context);
        if (Castle.DynamicProxy.ProxyUtil.GetUnproxiedInstance(instance) is IAsyncDisposable &&
            context.SelectScopeRoot(handlers => handlers.LastOrDefault())?.Burden is { } burden)
            burden.RequiresDecommission = true;
        return instance;
    }

    protected override void ApplyCommissionConcerns(object instance)
    {
        if (instance is not NullFactoryResult) base.ApplyCommissionConcerns(instance);
    }

    protected override void ApplyDecommissionConcerns(object instance)
    {
        if (instance is not NullFactoryResult) base.ApplyDecommissionConcerns(instance);
    }
}
