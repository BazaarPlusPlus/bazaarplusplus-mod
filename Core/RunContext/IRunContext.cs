#nullable enable
namespace BazaarPlusPlus.Core.RunContext;

internal interface IRunContext
{
    bool IsInGameRun { get; set; }

    string? CurrentServerRunId { get; set; }

    string LastMessageId { get; set; }
}
