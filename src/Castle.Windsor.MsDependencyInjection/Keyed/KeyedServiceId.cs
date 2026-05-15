#nullable enable

using System;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

/// <summary>
/// Identifies a keyed service registration or lookup by its service <see cref="Type"/>
/// and the <see cref="Key"/> object supplied at registration / resolution time.
/// Equality is value-based: two lookups are equal when both the type and the key are
/// equal (key comparison uses <see cref="object.Equals(object)"/>).
/// </summary>
internal readonly record struct KeyedServiceId(Type ServiceType, object? Key);