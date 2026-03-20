#nullable enable
namespace BazaarPlusPlus.Core.RunContext;

internal sealed class RunContextStore : IRunContext
{
    public bool IsInGameRun { get; set; }

    public string? CurrentServerRunId { get; set; }

    public string LastMessageId { get; set; } = string.Empty;

    public void Reset()
    {
        IsInGameRun = false;
        CurrentServerRunId = null;
        LastMessageId = string.Empty;
    }
}
