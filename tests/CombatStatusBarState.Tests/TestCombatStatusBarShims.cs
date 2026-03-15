namespace BazaarPlusPlus
{
    internal static class ModState
    {
        internal static bool IsInGameRun { get; set; }
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
