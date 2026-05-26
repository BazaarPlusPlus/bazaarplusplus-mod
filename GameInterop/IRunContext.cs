#nullable enable
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Core.RunContext;

namespace BazaarPlusPlus.GameInterop;

internal interface IRunContext
{
    bool IsInGameRun { get; set; }

    string? CurrentServerRunId { get; set; }

    RunExitKind LastRunExitKind { get; set; }

    EVictoryCondition LastVictoryCondition { get; set; }

    string LastMessageId { get; set; }
}
