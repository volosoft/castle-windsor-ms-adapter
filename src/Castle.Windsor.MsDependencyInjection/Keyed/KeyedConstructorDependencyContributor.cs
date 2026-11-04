using System;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.ModelBuilder;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

internal sealed class KeyedConstructorDependencyContributor(TypeKeyedMetadataRegistry metadata, KeyedServiceRegistry registry)
    : IContributeComponentModelConstruction
{
    internal const string Reference = "${Castle.Windsor.MsDependencyInjection.KeyedParameter}";

    public void ProcessModel(IKernel kernel, ComponentModel model)
    {
        foreach (var constructor in model.Constructors)
        {
            var type = constructor.Constructor.DeclaringType;
            if (type == null || !metadata.HasAnyKeyedParameter(type)) continue;
            var parameters = constructor.Constructor.GetParameters();
            foreach (var dependency in constructor.Dependencies)
            {
                if (dependency.Parameter != null) continue;
                var parameter = Array.Find(parameters, candidate => candidate.Name == dependency.DependencyKey);
                if (parameter == null || !metadata.TryGet(type, parameter, out var info)) continue;
                var selected = info.ForComponent(registry.IsKeyedService(model.Name));
                if (selected == null || selected.Kind == KeyedParameterKind.ServiceKey) continue;

                // Declining a keyed parameter must not enable ordinary type resolution.
                dependency.IsOptional = true;
                dependency.Parameter = new ParameterModel(dependency.DependencyKey, Reference);
            }
        }
    }
}
