#nullable enable
using BazaarGameShared.Domain.Core.Types;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.UI.Menu;
using UnityEngine;

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool;

internal sealed class RandomHeroPoolNativeController : MonoBehaviour
{
    private static readonly HashSet<RandomHeroPoolNativeController> Controllers = new();
    private static readonly RandomHeroPoolSelector Selector = new();
    private HeroOptionController? _view;
    private bool _refreshPending;

    internal static void Project(HeroOptionController view)
    {
        var controller = view.GetComponent<RandomHeroPoolNativeController>();
        if (controller == null)
            controller = view.gameObject.AddComponent<RandomHeroPoolNativeController>();
        controller._view = view;
        Controllers.Add(controller);
        controller.ApplyVisuals();
    }

    internal static bool TryHandleClick(HeroOptionController view)
    {
        if (view.Data is not HeroData data || !PlayerPreferences.Data.RandomHeroEnabled)
            return false;

        if (data.Hero == EHero.Common)
        {
            // Clicking the random card again returns to ordinary single-hero selection.
            var manager = Services.Get<HeroManager>();
            if (manager == null)
                return false;
            manager.SelectHero(Data.SelectedHero);
            RefreshAll();
            return true;
        }

        if (!data.Owned || !TryResolveState(out var state) || state == null)
            return false;

        var id = RandomHeroPoolPlayerPrefs.NormalizeHeroId(data.Hero.ToString());
        var next = state.SetSelected(id, !state.IsSelected(id));
        RandomHeroPoolPlayerPrefs.SaveSelectedHeroIds(next.SelectedHeroIds);
        RefreshAll();
        return true;
    }

    internal static bool TrySelectRandomHero()
    {
        if (!TryResolveState(out var state) || state == null || !ClientCache.RunConfig.HasData)
            return false;

        var candidates = state.SelectedHeroIds.ToArray();
        var selectedId = Selector.SelectHero(
            candidates,
            UnityEngine.Random.Range(0, candidates.Length)
        );
        // Match canonical preference ids back to the actual owned runtime enum name.
        var owned = ClientCache.OwnedHeroes.Value.FirstOrDefault(hero =>
            RandomHeroPoolHeroIdentity.Matches(hero.heroId, selectedId)
        );
        if (
            string.IsNullOrWhiteSpace(owned.heroId)
            || !Enum.TryParse<EHero>(owned.heroId, out var hero)
        )
            return false;

        RandomHeroPoolPlayerPrefs.SaveSelectedHeroIds(candidates);
        PlayerPreferences.Data.RandomHeroEnabled = true;
        ClientCache.RunConfig.SetSelectedHero(hero);
        RefreshAll();
        return true;
    }

    private static bool TryResolveState(out RandomHeroPoolState? state)
    {
        state = null;
        return ClientCache.OwnedHeroes.HasData
            && RandomHeroPoolPlayerPrefs.TryResolveState(
                ClientCache.OwnedHeroes.Value.Select(hero => hero.heroId),
                out state
            );
    }

    internal static void RefreshAll()
    {
        foreach (var controller in Controllers)
        {
            if (controller != null && controller.isActiveAndEnabled)
            {
                controller._refreshPending = true;
                controller.ApplyVisuals();
            }
        }
    }

    private void LateUpdate()
    {
        if (!_refreshPending)
            return;
        _refreshPending = false;
        // Native toggle listeners can run after the card click and reapply its original value.
        // Reassert the pool projection once the complete input event has finished.
        ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        if (_view == null || _view.Data is not HeroData data || _view._button == null)
            return;

        if (!PlayerPreferences.Data.RandomHeroEnabled)
        {
            // The patch yields to native projection outside pool mode, including ToggleGroup.
            _view.UpdateSelected();
            return;
        }

        // Detach before projection: a native ToggleGroup would clear the other pool members.
        _view._button.group = null;
        var selected =
            data.Hero == EHero.Common
            || (
                data.Owned
                && TryResolveState(out var state)
                && state != null
                && state.IsSelected(RandomHeroPoolPlayerPrefs.NormalizeHeroId(data.Hero.ToString()))
            );
        _view._isSelected = selected;
        _view._button.SetSelected(selected);
    }

    private void OnDestroy() => Controllers.Remove(this);
}
