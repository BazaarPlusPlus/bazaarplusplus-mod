#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.GameInterop.StaticCards;
using BazaarPlusPlus.Infrastructure;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Resolves a deterministic donor ArtKey from native static data, by card size, so the
// collection LoadArt prefix can materialize an *authored* base material for a synthetic
// achievement card. The full-table card-map read runs on a worker thread (mirrors
// CollectionCatalog.cs:70); Resolve returns Pending until the scan completes, which the
// factory maps to NotReady (retried next frame).
internal class BppCustomCardMaterialDonorResolver
{
    private readonly object _gate = new();
    private object? _source;
    private readonly Dictionary<ECardSize, BppCustomCardMaterialDonorResult> _resolved = new();
    private readonly HashSet<ECardSize> _inFlight = new();

    public virtual BppCustomCardMaterialDonorResult Resolve(object? staticData, ECardSize size)
    {
        if (staticData == null)
            return BppCustomCardMaterialDonorResult.Pending();

        lock (_gate)
        {
            if (!ReferenceEquals(_source, staticData))
            {
                _source = staticData;
                _resolved.Clear();
                _inFlight.Clear();
            }
            if (_resolved.TryGetValue(size, out var done))
                return done;
            if (!_inFlight.Add(size))
                return BppCustomCardMaterialDonorResult.Pending(); // scan already running
        }

        var captured = staticData;
        _ = Task.Run(() =>
        {
            string? key = null;
            try
            {
                key = ScanForDonor(captured, size);
            }
            catch (Exception ex)
            {
                BppLog.Warn(
                    nameof(BppCustomCardMaterialDonorResolver),
                    $"Donor scan failed for size={size}: {ex.Message}"
                );
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_source, captured))
                    {
                        _resolved[size] = string.IsNullOrEmpty(key)
                            ? BppCustomCardMaterialDonorResult.Unavailable()
                            : BppCustomCardMaterialDonorResult.Ready(key);
                        _inFlight.Remove(size);
                    }
                }
            }
        });
        return BppCustomCardMaterialDonorResult.Pending();
    }

    private static string? ScanForDonor(object source, ECardSize size)
    {
        var cardMap = BppStaticDataAccess.LoadCardMap(source);
        if (cardMap == null)
            return null;

        var items = cardMap
            .Values.OfType<TCardItem>()
            .Where(t =>
            {
                var c = CollectionCardClassifier.Classify(t);
                return c.IsCatalogCard && !c.IsPackage;
            })
            .ToList();

        string? Pick(IEnumerable<TCardItem> source2) =>
            source2
                .OrderBy(t => t.Id)
                .Select(t => t.ArtKey)
                .FirstOrDefault(k => !string.IsNullOrEmpty(k));

        // Prefer same size; fall back to any catalog item so a new size can never become
        // permanently unrenderable. The card-art material is keyed on ArtKey, not size.
        return Pick(items.Where(t => t.Size == size)) ?? Pick(items);
    }
}

internal enum BppCustomCardMaterialDonorStatus
{
    Pending,
    Ready,
    Unavailable,
}

internal readonly struct BppCustomCardMaterialDonorResult
{
    private BppCustomCardMaterialDonorResult(
        BppCustomCardMaterialDonorStatus status,
        string? artKey
    )
    {
        Status = status;
        ArtKey = artKey;
    }

    public BppCustomCardMaterialDonorStatus Status { get; }
    public string? ArtKey { get; }

    public static BppCustomCardMaterialDonorResult Pending() =>
        new(BppCustomCardMaterialDonorStatus.Pending, null);

    public static BppCustomCardMaterialDonorResult Ready(string artKey) =>
        string.IsNullOrEmpty(artKey)
            ? Unavailable()
            : new BppCustomCardMaterialDonorResult(BppCustomCardMaterialDonorStatus.Ready, artKey);

    public static BppCustomCardMaterialDonorResult Unavailable() =>
        new(BppCustomCardMaterialDonorStatus.Unavailable, null);
}
