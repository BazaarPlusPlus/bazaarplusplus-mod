#nullable enable
using System;
using System.Collections.Generic;
using BazaarPlusPlus.Core.Config;

namespace BazaarPlusPlus.Game.Settings;

/// <summary>
/// A settings dock row that cycles through an ordered value ladder.
/// </summary>
/// <remarks>
/// The <c>write</c> callback owns persistence and any coupled persistence (for example,
/// forcing another setting on). The optional <c>onChanged</c> callback reacts after the
/// write completes (for example, publishing an event, refreshing UI, or arming a pump) and
/// is invoked unconditionally with the new value after every activation.
/// </remarks>
internal sealed class CyclingSettingsDockEntry<T> : ISettingsDockEntry
{
    private readonly string _key;
    private readonly Func<string, string> _resolveLabel;
    private readonly IReadOnlyList<T> _ladder;
    private readonly Func<IBppConfig, T> _read;
    private readonly Action<IBppConfig, T> _write;
    private readonly Func<T, bool> _highlightWhen;
    private readonly Func<T, string, string> _resolveStatus;
    private readonly Func<T, T>? _nextOverride;
    private readonly Action<T>? _onChanged;

    internal CyclingSettingsDockEntry(
        int order,
        string key,
        Func<string, string> resolveLabel,
        IReadOnlyList<T> ladder,
        Func<IBppConfig, T> read,
        Action<IBppConfig, T> write,
        Func<T, bool> highlightWhen,
        Func<T, string, string> resolveStatus,
        Func<T, T>? nextOverride = null,
        Action<T>? onChanged = null
    )
    {
        Order = order;
        _key = !string.IsNullOrWhiteSpace(key)
            ? key
            : throw new ArgumentException("Key is required.", nameof(key));
        _resolveLabel = resolveLabel ?? throw new ArgumentNullException(nameof(resolveLabel));
        _ladder = ladder ?? throw new ArgumentNullException(nameof(ladder));
        if (_ladder.Count == 0)
            throw new ArgumentException("At least one ladder value is required.", nameof(ladder));

        _read = read ?? throw new ArgumentNullException(nameof(read));
        _write = write ?? throw new ArgumentNullException(nameof(write));
        _highlightWhen = highlightWhen ?? throw new ArgumentNullException(nameof(highlightWhen));
        _resolveStatus = resolveStatus ?? throw new ArgumentNullException(nameof(resolveStatus));
        _nextOverride = nextOverride;
        _onChanged = onChanged;
    }

    public int Order { get; }

    public BppSettingsDockDefinition Build(IBppConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        return new BppSettingsDockDefinition(
            _key,
            _resolveLabel,
            languageCode => _resolveStatus(_read(config), languageCode),
            () => _highlightWhen(_read(config)),
            () =>
            {
                var next = Next(_read(config));
                _write(config, next);
                _onChanged?.Invoke(next);
            },
            collapseAfterActivate: false
        );
    }

    internal static CyclingSettingsDockEntry<bool> Toggle(
        int order,
        string key,
        Func<string, string> resolveLabel,
        Func<IBppConfig, bool> read,
        Action<IBppConfig, bool> write,
        Action<bool>? onChanged = null
    ) =>
        new(
            order,
            key,
            resolveLabel,
            new[] { false, true },
            read,
            write,
            value => value,
            (value, _) => value ? "ON" : "OFF",
            onChanged: onChanged
        );

    private T Next(T current)
    {
        if (_nextOverride != null)
            return _nextOverride(current);

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _ladder.Count; i++)
        {
            if (comparer.Equals(_ladder[i], current))
                return _ladder[(i + 1) % _ladder.Count];
        }

        return _ladder[0];
    }
}
