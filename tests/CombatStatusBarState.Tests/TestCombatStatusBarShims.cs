using BazaarPlusPlus.Core.Runtime;

namespace BazaarPlusPlus.Core.Runtime
{
    internal interface IRunContext
    {
        bool IsInGameRun { get; set; }
    }

    internal interface IBppServices
    {
        IRunContext RunContext { get; }
    }

    internal sealed class TestRunContext : IRunContext
    {
        public bool IsInGameRun { get; set; }
    }

    internal sealed class TestServices : IBppServices
    {
        public static TestServices Instance { get; } = new TestServices();

        public IRunContext RunContext { get; } = new TestRunContext();
    }
}

namespace BazaarPlusPlus.Game.CombatStatusBar
{
    internal sealed partial class CombatStatusBar
    {
        private static IBppServices? _services;

        static CombatStatusBar()
        {
            _services = TestServices.Instance;
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
