#nullable enable
namespace BazaarPlusPlus.Game.Online;

internal sealed class BearerState
{
    public BearerState(
        string? token = null,
        string? playerAccountId = null,
        string? playerUsername = null
    )
    {
        Token = token;
        PlayerAccountId = playerAccountId;
        PlayerUsername = playerUsername;
    }

    public string? Token { get; }

    public string? PlayerAccountId { get; }

    public string? PlayerUsername { get; }

    public bool IsAvailable => !string.IsNullOrEmpty(Token);
}
