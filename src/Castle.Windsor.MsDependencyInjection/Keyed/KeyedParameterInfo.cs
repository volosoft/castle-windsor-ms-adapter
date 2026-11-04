#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

internal record KeyedParameterInfo(
    KeyedParameterKind Kind,
    ServiceKeyLookupMode LookupMode,
    object? Key,
    Type ParameterType,
    KeyedParameterInfo? NonKeyedFallback = null)
{
    public KeyedParameterInfo? ForComponent(bool keyed) =>
        Kind == KeyedParameterKind.ServiceKey && !keyed ? NonKeyedFallback : this;
}
