namespace BazaarPlusPlus
{
    internal static class ModState
    {
        internal static bool IsInGameRun { get; set; }
    }
}

namespace BazaarPlusPlus.Game.CombatStatusBar
{
    internal sealed partial class CombatStatusBar
    {
        private static bool? _persistedOverlayVisibilityForTests;

        internal static bool GetPersistedOverlayVisibilityForTests()
        {
            return _persistedOverlayVisibilityForTests ?? true;
        }

        internal static void ClearPersistedOverlayVisibilityForTests()
        {
            _persistedOverlayVisibilityForTests = null;
        }

        static partial void PersistOverlayVisibility(bool visible)
        {
            _persistedOverlayVisibilityForTests = visible;
        }
    }
}

namespace TheBazaar
{
    public sealed class GameServiceManager
    {
        public bool GamePaused { get; private set; }

        public void PauseOrUnpauseGame(bool paused)
        {
            GamePaused = paused;
        }
    }

    public sealed class Singleton<T>
        where T : class
    {
        public static T? Instance { get; set; }
    }
}
