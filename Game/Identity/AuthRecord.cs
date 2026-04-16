namespace BazaarPlusPlus.Game.Identity
{
    public sealed class AuthRecord
    {
        public AuthRecord(string token, string playerAccountId, string playerUsername, string issuedAtUtc)
        {
            Token = token;
            PlayerAccountId = playerAccountId;
            PlayerUsername = playerUsername;
            IssuedAtUtc = issuedAtUtc;
        }

        public string Token { get; }
        public string PlayerAccountId { get; }
        public string PlayerUsername { get; }
        public string IssuedAtUtc { get; }
    }
}
