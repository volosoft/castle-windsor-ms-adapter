using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;

public sealed class ParityContext
{
    private bool _disposed;

    public ParityContext(IServiceProvider provider, DisposeTracker disposes)
    {
        Provider = provider;
        Disposes = disposes;
    }

    public IServiceProvider Provider { get; }
    public DisposeTracker Disposes { get; }

    // IServiceProviderIsService / IServiceProviderIsKeyedService are resolved as services on both
    // backends — the real MS ServiceProvider does NOT expose them through a direct cast.
    public IServiceProviderIsKeyedService IsKeyedService => Provider.GetRequiredService<IServiceProviderIsKeyedService>();
    
    public IServiceProviderIsService IsService => Provider.GetRequiredService<IServiceProviderIsService>();

    /// <summary>Creates a DI scope, runs <paramref name="body"/> against it, then disposes it.</summary>
    public void InScope(Action<IServiceProvider> body)
    {
        using (var scope = Provider.CreateScope())
        {
            body(scope.ServiceProvider);
        }
    }

    /// <summary>Disposes the root provider. Idempotent — safe to call from a test and again from the runner.</summary>
    public void DisposeProvider()
    {
        if (_disposed) return;
        _disposed = true;
        (Provider as IDisposable)?.Dispose();
    }
}

/// <summary>
/// Differential test harness for the keyed-services adapter.
/// <para>
/// Every scenario is executed twice from one identical <see cref="IServiceCollection"/> setup: once
/// against the real Microsoft.Extensions.DependencyInjection 8.0.0 container (the <i>reference</i>
/// implementation) and once against the Castle Windsor adapter. The reference always runs first: if
/// the check fails there, the expectation itself is wrong (not the adapter), and the failure says so.
/// A failure on the Windsor run is reported as <c>[Windsor divergence] &lt;scenario&gt;:</c> so the
/// result is immediately interpretable.
/// </para>
/// </summary>
public static class ParityRunner
{
    /// <summary>
    /// Runs the exact same assertion delegate against both backends. Use this when the expected
    /// behavior can be asserted directly (a returned type, an instance count, a thrown-or-not check).
    /// The reference run validates that the assertion is a correct statement of MS DI 8.0.0 behavior;
    /// the Windsor run then verifies the adapter matches it.
    /// </summary>
    public static void RunAssertParity(
        Action<IServiceCollection> configure,
        Action<ParityContext> assert,
        bool validateScopes = false,
        [CallerMemberName] string scenario = null)
    {
        RunOnReference(configure, validateScopes, scenario, assert);
        RunOnWindsor(configure, validateScopes, scenario, assert);
    }

    /// <summary>
    /// Projects a deterministic <see cref="IReadOnlyList{String}"/> from each backend and asserts the
    /// two projections are sequence-equal. Use this when comparing a literal expectation is awkward
    /// but the two backends' observable output can be reduced to a comparable form — e.g.
    /// <see cref="Outcome.TypeNames"/> for ordered collections,
    /// <see cref="Outcome.Order"/> for instance-identity relationships, or
    /// <see cref="Outcome.Result"/>/<see cref="Outcome.ResultMany"/> for value-or-exception
    /// results. The reference projection runs first and must not throw.
    /// </summary>
    public static void RunOutcomeParity(
        Action<IServiceCollection> configure,
        Func<ParityContext, IReadOnlyList<string>> project,
        bool validateScopes = false,
        [CallerMemberName] string scenario = null)
    {
        IReadOnlyList<string> reference = null;
        RunOnReference(configure, validateScopes, scenario, ctx => reference = project(ctx));

        IReadOnlyList<string> windsor = null;
        RunOnWindsor(configure, validateScopes, scenario, ctx => windsor = project(ctx));

        if (!reference.SequenceEqual(windsor))
        {
            throw new XunitException(
                $"[Windsor divergence] {scenario}: projection mismatch.\n" +
                $"  MS      = [{string.Join(", ", reference)}]\n" +
                $"  Windsor = [{string.Join(", ", windsor)}]");
        }
    }

    private static (ParityContext ctx, Exception buildError) Build(Backend backend, Action<IServiceCollection> configure, bool validateScopes)
    {
        var services = new ServiceCollection();
        var tracker = new DisposeTracker();
        services.AddSingleton(tracker);
        configure(services);

        try
        {
            IServiceProvider provider;
            if (backend == Backend.Ms)
            {
                provider = services.BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateScopes = validateScopes,
                    ValidateOnBuild = false
                });
            }
            else
            {
                // WindsorContainer / WindsorRegistrationHelper resolve via ancestor namespaces,
                // exactly as in the existing WindsorSpecificationTests.
                provider = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);
            }

            return (new ParityContext(provider, tracker), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }

    private static void RunOnReference(
        Action<IServiceCollection> configure, bool validateScopes, string scenario, Action<ParityContext> assert)
    {
        var (ctx, buildError) = Build(Backend.Ms, configure, validateScopes);
        if (buildError != null)
        {
            throw new XunitException(
                $"MS reference failed for '{scenario}' — building the MS container threw. " +
                $"The expectation is wrong, not Windsor.\n{buildError}");
        }

        try
        {
            assert(ctx);
        }
        catch (Exception ex)
        {
            throw new XunitException(
                $"MS reference failed for '{scenario}' — the expectation is wrong, not Windsor.\n{ex}");
        }
        finally
        {
            ctx.DisposeProvider();
        }
    }

    private static void RunOnWindsor(
        Action<IServiceCollection> configure, bool validateScopes, string scenario, Action<ParityContext> assert)
    {
        var (ctx, buildError) = Build(Backend.Windsor, configure, validateScopes);
        if (buildError != null)
        {
            throw new XunitException($"[Windsor divergence] {scenario}: building the Windsor provider threw.\n{buildError}");
        }

        try
        {
            assert(ctx);
        }
        catch (XunitException) { throw; }
        catch (Exception ex)
        {
            throw new XunitException($"[Windsor divergence] {scenario}: {ex.GetType().Name}: {ex.Message}\n{ex}");
        }
        finally
        {
            ctx.DisposeProvider();
        }
    }

    private enum Backend
    {
        /// <summary>The real Microsoft.Extensions.DependencyInjection container (the 8.0.0 oracle).</summary>
        Ms,

        /// <summary>The Castle Windsor adapter under test.</summary>
        Windsor
    }
}