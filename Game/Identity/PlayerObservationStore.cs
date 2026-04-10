#nullable enable
using System;
using System.IO;

namespace BazaarPlusPlus.Game.Identity;

internal sealed class PlayerObservationStore
{
    private readonly string _observationPath;

    public PlayerObservationStore(string observationPath)
    {
        if (string.IsNullOrWhiteSpace(observationPath))
            throw new ArgumentException("Observation path is required.", nameof(observationPath));

        _observationPath = observationPath;
    }

    public bool TryLoad(out PlayerObservationRecord? record)
    {
        return BppIdentityEnvelope.TryDecodePayload(_observationPath, out record);
    }

    public void Save(PlayerObservationRecord record)
    {
        if (record == null)
            throw new ArgumentNullException(nameof(record));

        var directory = Path.GetDirectoryName(_observationPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllBytes(_observationPath, BppIdentityEnvelope.EncodePayload(record));
    }
}
