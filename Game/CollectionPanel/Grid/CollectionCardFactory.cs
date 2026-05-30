#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Cards.Skill;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.CollectionPanel.Data;
using BazaarPlusPlus.Game.HistoryPanel.Preview;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;

namespace BazaarPlusPlus.Game.CollectionPanel.Grid;

// Resolves a CollectionCardVm into a live, SetUp-ready CardPreviewBase instance vended from
// the pool. Both the Item and Skill branches eventually call CardPreviewBase.SetUp via
// reflection so the mod compiles against any DLL surface where the method exists; the
// instance argument is constructed locally (filling InstanceId / TemplateVersion / Attributes)
// to avoid the NPE paths inside the game's CardPreviewBase.SetUp.
internal sealed class CollectionCardFactory
{
    private readonly CollectionCardPool _pool;
    private readonly Transform _parent;
    private int _instanceCounter;

    public CollectionCardFactory(CollectionCardPool pool, Transform parent)
    {
        _pool = pool;
        _parent = parent;
    }

    public bool ReflectionReady => HistoryPanelCardPreviewReflection.SetUpMethod != null;

    public CollectionCardBinding? TryBind(CollectionCardVm vm)
    {
        if (vm == null)
            return null;

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
            return null;

        var template = HistoryPanelPreviewTemplateLookup.GetCardTemplate(staticData, vm.Id);
        if (template == null)
        {
            BppLog.Warn(
                "CollectionCardFactory",
                $"Template lookup failed for id={vm.Id} ({vm.InternalName})."
            );
            return null;
        }

        var kind =
            vm.Type == ECardType.Skill
                ? CollectionCardKind.ForSkill()
                : CollectionCardKind.ForItem(vm.Size);
        var card = _pool.Take(kind, _parent);
        if (card == null)
            return null;

        var instance = BuildSyntheticInstance(vm);
        var setUpTask = InvokeSetUpSafe(card, template, instance);
        return new CollectionCardBinding(card, kind, setUpTask);
    }

    public void Return(Component? card, CollectionCardKind kind) => _pool.Return(card, kind);

    private TCardInstance BuildSyntheticInstance(CollectionCardVm vm)
    {
        var attributes = new Dictionary<ECardAttributeType, int>();
        var id = $"bpp-collection-{++_instanceCounter}";

        if (vm.Type == ECardType.Skill)
        {
            return new TCardInstanceSkill
            {
                TemplateId = vm.Id,
                TemplateVersion = string.Empty,
                InstanceId = id,
                Tier = vm.StartingTier,
                Attributes = attributes,
            };
        }

        return new TCardInstanceItem
        {
            TemplateId = vm.Id,
            TemplateVersion = string.Empty,
            InstanceId = id,
            Tier = vm.StartingTier,
            Attributes = attributes,
        };
    }

    // Accepts TCardInstance (base) so the same helper can launch both Item and Skill SetUp.
    // The reflected SetUp on CardPreviewBase declares its instance parameter as the base
    // class, so the JIT signature matches in both branches.
    private static async Task InvokeSetUpSafe(
        Component card,
        TCardBase template,
        TCardInstance instance
    )
    {
        var method = HistoryPanelCardPreviewReflection.SetUpMethod;
        if (method == null)
            return;

        try
        {
            var raw = method.Invoke(card, new object[] { template, false, instance });
            if (raw is Task task)
                await task;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "CollectionCardFactory",
                $"CardPreviewBase.SetUp threw for template={template.Id}: {ex.InnerException?.Message ?? ex.Message}"
            );
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "CollectionCardFactory",
                $"CardPreviewBase.SetUp invocation failed for template={template.Id}: {ex.Message}"
            );
            throw;
        }
    }
}

// One realized card + the bind kind we hand back to the pool on Return + the SetUp task we
// must await before flipping the card visible (to avoid showing a frame mid-LoadArt).
internal readonly struct CollectionCardBinding
{
    public CollectionCardBinding(Component card, CollectionCardKind kind, Task setUpTask)
    {
        Card = card;
        Kind = kind;
        SetUpTask = setUpTask;
    }

    public Component Card { get; }
    public CollectionCardKind Kind { get; }
    public Task SetUpTask { get; }
}
