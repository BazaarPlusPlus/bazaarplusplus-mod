#nullable enable
using System;

namespace BazaarPlusPlus.Game.Identity
{
    public sealed class AuthStore
    {
        private const int CurrentSchemaVersion = 1;
        private readonly string _identityDirectoryPath;

        public AuthStore(string identityDirectoryPath)
        {
            _identityDirectoryPath = identityDirectoryPath
                ?? throw new ArgumentNullException(nameof(identityDirectoryPath));
        }

        public bool TryLoad(out AuthRecord? record)
        {
            record = null;
            if (
                !IdentityJsonFileStore.TryRead<AuthRecordFile>(
                    IdentityJsonFileStore.AuthPath(_identityDirectoryPath),
                    out var payload
                )
                || payload == null
                || !payload.IsValid()
            )
            {
                return false;
            }

            record = new AuthRecord(
                payload.Token!,
                payload.PlayerAccountId!,
                payload.PlayerUsername!,
                payload.IssuedAtUtc!
            );
            return true;
        }

        public void Upsert(AuthRecord record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            IdentityJsonFileStore.Write(
                IdentityJsonFileStore.AuthPath(_identityDirectoryPath),
                new AuthRecordFile
                {
                    SchemaVersion = CurrentSchemaVersion,
                    Token = record.Token,
                    PlayerAccountId = record.PlayerAccountId,
                    PlayerUsername = record.PlayerUsername,
                    IssuedAtUtc = record.IssuedAtUtc,
                }
            );
            IdentityJsonFileStore.DeleteLegacyDatabaseFiles(_identityDirectoryPath);
        }

        public void Delete()
        {
            IdentityJsonFileStore.DeleteIfExists(
                IdentityJsonFileStore.AuthPath(_identityDirectoryPath)
            );
            IdentityJsonFileStore.DeleteLegacyDatabaseFiles(_identityDirectoryPath);
        }

        private sealed class AuthRecordFile
        {
            public int SchemaVersion { get; set; }

            public string? Token { get; set; }

            public string? PlayerAccountId { get; set; }

            public string? PlayerUsername { get; set; }

            public string? IssuedAtUtc { get; set; }

            public bool IsValid() =>
                SchemaVersion == CurrentSchemaVersion
                && !string.IsNullOrWhiteSpace(Token)
                && !string.IsNullOrWhiteSpace(PlayerAccountId)
                && !string.IsNullOrWhiteSpace(PlayerUsername)
                && !string.IsNullOrWhiteSpace(IssuedAtUtc);
        }
    }
}
