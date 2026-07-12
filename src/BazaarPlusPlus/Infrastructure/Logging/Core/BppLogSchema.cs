#nullable enable
using System;
using System.Collections.Generic;

namespace BazaarPlusPlus.Infrastructure.Logging;

/// <summary>
/// Closed, low-cardinality owner of an operational event. Feature code selects a declared scope;
/// it never manufactures a component name from runtime data.
/// </summary>
internal sealed class BppLogFeatureScope
{
    internal static BppLogFeatureScope Logger { get; } = new("Logger", "logging");

    private BppLogFeatureScope(string prefixName, string eventIdPrefix)
    {
        PrefixName = prefixName;
        EventIdPrefix = eventIdPrefix;
    }

    internal string PrefixName { get; }

    internal string EventIdPrefix { get; }
}

internal enum BppLogFieldPrivacy
{
    Public,
    UntrustedText,
    Sensitive,
    LocalPath,
    RemoteUri,
}

internal enum BppLogCorrelationPolicy
{
    None,
    Full,
    Short,
    Hash,
}

internal enum BppLogCardinality
{
    Low,
    High,
}

/// <summary>
/// Defines one ordered field. The definition token owns its name and governance metadata; runtime
/// values bind to this exact token so callers cannot override privacy or correlation policy.
/// </summary>
internal sealed class BppLogFieldDefinition
{
    internal BppLogFieldDefinition(
        int order,
        string name,
        BppLogFieldPrivacy privacy,
        BppLogCorrelationPolicy correlation,
        BppLogCardinality cardinality
    )
    {
        Order = order;
        Name = name;
        Privacy = privacy;
        Correlation = correlation;
        Cardinality = cardinality;
    }

    internal int Order { get; }

    internal string Name { get; }

    internal BppLogFieldPrivacy Privacy { get; }

    internal BppLogCorrelationPolicy Correlation { get; }

    internal BppLogCardinality Cardinality { get; }

    internal BppLogFieldValue Bind(object? value) => new(this, value);
}

internal sealed class BppLogStormPolicy
{
    private readonly BppLogFieldDefinition[] _keyFields;

    internal BppLogStormPolicy(IReadOnlyList<BppLogFieldDefinition>? keyFields)
    {
        _keyFields = Snapshot(keyFields);
    }

    internal IReadOnlyList<BppLogFieldDefinition> KeyFields => _keyFields;

    private static BppLogFieldDefinition[] Snapshot(IReadOnlyList<BppLogFieldDefinition>? fields)
    {
        if (fields == null || fields.Count == 0)
            return Array.Empty<BppLogFieldDefinition>();

        var snapshot = new BppLogFieldDefinition[fields.Count];
        for (var index = 0; index < snapshot.Length; index++)
            snapshot[index] = fields[index];
        return snapshot;
    }
}

/// <summary>
/// Stable event vocabulary entry. Definitions live beside their owning feature and declare scope,
/// event ID, ordered field schema, privacy, correlation, cardinality, and optional storm keys.
/// </summary>
internal sealed class BppLogEventDefinition
{
    private readonly BppLogFieldDefinition[] _fields;

    internal BppLogEventDefinition(
        BppLogFeatureScope scope,
        string eventId,
        IReadOnlyList<BppLogFieldDefinition>? fields,
        BppLogStormPolicy? stormPolicy = null
    )
    {
        Scope = scope;
        EventId = eventId;
        _fields = Snapshot(fields);
        StormPolicy = stormPolicy;
    }

    internal BppLogFeatureScope Scope { get; }

    internal string EventId { get; }

    internal IReadOnlyList<BppLogFieldDefinition> Fields => _fields;

    internal BppLogStormPolicy? StormPolicy { get; }

    private static BppLogFieldDefinition[] Snapshot(IReadOnlyList<BppLogFieldDefinition>? fields)
    {
        if (fields == null || fields.Count == 0)
            return Array.Empty<BppLogFieldDefinition>();

        var snapshot = new BppLogFieldDefinition[fields.Count];
        for (var index = 0; index < snapshot.Length; index++)
            snapshot[index] = fields[index];
        return snapshot;
    }
}

internal readonly struct BppLogFieldValue
{
    internal BppLogFieldValue(BppLogFieldDefinition field, object? value)
    {
        Field = field;
        Value = value;
    }

    internal BppLogFieldDefinition Field { get; }

    internal object? Value { get; }
}
