#nullable enable

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

internal record KeyedParameterInfo(
    KeyedParameterKind Kind,
    ServiceKeyLookupMode LookupMode,
    object? Key,
    Type ParameterType);
