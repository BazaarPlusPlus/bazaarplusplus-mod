#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BazaarPlusPlus.GameInterop.StaticCards;

namespace BazaarPlusPlus.GameInterop.CustomCards;

internal sealed class BppCustomCardRegistry
{
    private readonly Dictionary<Guid, BppCustomCardDescriptor> _byId = new();
    private readonly Func<Guid, bool> _collidesWithNativeCard;

    public BppCustomCardRegistry()
        : this(DefaultCollidesWithNativeCard) { }

    internal BppCustomCardRegistry(Func<Guid, bool> collidesWithNativeCard)
    {
        _collidesWithNativeCard =
            collidesWithNativeCard
            ?? throw new ArgumentNullException(nameof(collidesWithNativeCard));
    }

    public static BppCustomCardRegistry? Current { get; set; }

    public void Register(BppCustomCardDescriptor descriptor)
    {
        if (descriptor == null)
            throw new ArgumentNullException(nameof(descriptor));
        if (descriptor.Id == Guid.Empty)
            throw new InvalidOperationException("BPP custom card id must not be empty.");
        if (string.IsNullOrWhiteSpace(descriptor.InternalName))
            throw new InvalidOperationException(
                $"BPP custom card {descriptor.Id} must declare an internal name."
            );
        if (!BppCustomCardText.HasRequiredText(descriptor.Title))
            throw new InvalidOperationException(
                $"BPP custom card {descriptor.Id} must declare title text for en/zhHans/zhHant."
            );
        if (!BppCustomCardText.HasRequiredText(descriptor.Description))
            throw new InvalidOperationException(
                $"BPP custom card {descriptor.Id} must declare description text for en/zhHans/zhHant."
            );
        if (_byId.ContainsKey(descriptor.Id))
            throw new InvalidOperationException($"Duplicate BPP custom card id {descriptor.Id}.");
        if (_collidesWithNativeCard(descriptor.Id))
            throw new InvalidOperationException(
                $"BPP custom card id {descriptor.Id} collides with a native card template."
            );

        _byId.Add(descriptor.Id, descriptor);
    }

    public bool IsBppCard(Guid id) => _byId.ContainsKey(id);

    public bool TryGet(Guid id, out BppCustomCardDescriptor? descriptor) =>
        _byId.TryGetValue(id, out descriptor);

    public bool HasBundledArt(Guid id) =>
        _byId.TryGetValue(id, out var descriptor) && descriptor.HasBundledArt;

    public IReadOnlyList<BppCustomCardDescriptor> GetAll() =>
        _byId.Values.OrderBy(card => card.SortKey).ThenBy(card => card.InternalName).ToArray();

    public string FontAtlasSample()
    {
        var builder = new System.Text.StringBuilder();
        foreach (var descriptor in GetAll())
        {
            builder.Append(BppCustomCardText.AllLocales(descriptor.Title));
            builder.Append(BppCustomCardText.AllLocales(descriptor.Description));
        }
        return builder.ToString();
    }

    private static bool DefaultCollidesWithNativeCard(Guid id)
    {
        try
        {
            var staticData = BppStaticDataAccess.TryGetReadyManagerObject();
            return BppStaticDataAccess.GetCardTemplate(staticData, id) != null;
        }
        catch
        {
            return false;
        }
    }
}
