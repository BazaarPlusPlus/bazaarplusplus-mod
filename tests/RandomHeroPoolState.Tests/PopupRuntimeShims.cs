using BazaarGameShared.Domain.Core.Types;

// Runtime shims mirror the current popup's non-notifying selection and cache contracts.
namespace UnityEngine
{
    public class MonoBehaviour
    {
        public GameObject gameObject { get; } = new();
        public bool isActiveAndEnabled => true;

        public T? GetComponent<T>()
            where T : class => gameObject.GetComponent<T>();
    }

    public class GameObject
    {
        private readonly Dictionary<Type, object> _components = new();

        public T? GetComponent<T>()
            where T : class => _components.GetValueOrDefault(typeof(T)) as T;

        public T AddComponent<T>()
            where T : new()
        {
            var instance = new T();
            _components[typeof(T)] = instance;
            return instance;
        }
    }

    public static class Random
    {
        public static int NextIndex { get; set; }

        public static int Range(int min, int max) => min + NextIndex % (max - min);
    }
}

namespace BazaarGameShared.Domain.Core.Types
{
    public enum EHero
    {
        Common,
        Vanessa,
        Pygmalien,
        Mak,
        Stelle,
    }
}

namespace TheBazaar
{
    public readonly record struct OwnedHero(string heroId);

    public sealed class Cache<T>
    {
        public bool HasData { get; set; } = true;
        public T Value { get; set; } = default!;
    }

    public sealed class RunConfigCache
    {
        public bool HasData { get; set; } = true;

        public void SetSelectedHero(EHero hero) => Data.SelectedHero = hero;
    }

    public static class Data
    {
        public static EHero SelectedHero { get; set; } = EHero.Vanessa;
    }

    public static class ClientCache
    {
        public static Cache<OwnedHero[]> OwnedHeroes { get; } = new();
        public static RunConfigCache RunConfig { get; } = new();
    }

    public sealed class Preferences
    {
        public bool RandomHeroEnabled { get; set; }
    }

    public static class PlayerPreferences
    {
        public static Preferences Data { get; } = new();
    }

    public sealed class HeroManager
    {
        public void SelectHero(EHero hero)
        {
            PlayerPreferences.Data.RandomHeroEnabled = false;
            ClientCache.RunConfig.SetSelectedHero(hero);
        }

        public static bool IsSelectedHero(EHero hero) =>
            !PlayerPreferences.Data.RandomHeroEnabled && Data.SelectedHero == hero;
    }
}

namespace TheBazaar.AppFramework
{
    public static class Services
    {
        public static T Get<T>()
            where T : new() => new();
    }
}

namespace TheBazaar.UI.Menu
{
    public sealed class HeroData
    {
        public EHero Hero { get; set; }
        public bool Owned { get; set; }
    }

    public sealed class NativeToggle
    {
        public object? group { get; set; } = new();
        public bool Selected { get; private set; }

        public void SetSelected(bool selected) => Selected = selected;
    }

    public sealed class HeroOptionController : UnityEngine.MonoBehaviour
    {
        public object? Data { get; set; }
        public NativeToggle _button { get; } = new();
        public bool _isSelected;

        public void UpdateSelected()
        {
            _isSelected = Data is HeroData data && HeroManager.IsSelectedHero(data.Hero);
            _button.SetSelected(_isSelected);
            _button.group = new();
        }
    }
}

namespace BazaarPlusPlus.Game.Lobby.RandomHeroPool
{
    internal static class RandomHeroPoolPlayerPrefs
    {
        internal static string[]? Selected { get; set; }

        internal static string NormalizeHeroId(string heroId) => heroId;

        internal static void SaveSelectedHeroIds(IEnumerable<string> ids) =>
            Selected = ids.ToArray();

        internal static bool TryResolveState(
            IEnumerable<string> owned,
            out RandomHeroPoolState? state
        )
        {
            var heroes = owned.ToArray();
            state = heroes.Length == 0 ? null : RandomHeroPoolStateFactory.Create(heroes, Selected);
            return state != null;
        }
    }

    internal static class RandomHeroPoolHeroIdentity
    {
        internal static bool Matches(string? runtimeId, string? savedId) => runtimeId == savedId;
    }
}
