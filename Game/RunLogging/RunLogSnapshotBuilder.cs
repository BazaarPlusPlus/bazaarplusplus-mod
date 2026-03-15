#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BazaarPlusPlus.Game.RunLogging.Models;

namespace BazaarPlusPlus.Game.RunLogging;

public static class RunLogSnapshotBuilder
{
    public static string ComputeStateFingerprint(RunLogStateSnapshotInput input)
    {
        return ComputeSha1(
            Join(
                input.Day?.ToString(CultureInfo.InvariantCulture),
                input.Hour?.ToString(CultureInfo.InvariantCulture),
                input.State,
                input.EncounterId,
                input.RerollCost?.ToString(CultureInfo.InvariantCulture),
                input.RerollsRemaining?.ToString(CultureInfo.InvariantCulture)
            )
        );
    }

    public static string ComputeSelectionFingerprint(RunLogSelectionSnapshotInput input)
    {
        var builder = new StringBuilder();
        builder.Append(
            Join(
                input.Day?.ToString(CultureInfo.InvariantCulture),
                input.Hour?.ToString(CultureInfo.InvariantCulture),
                input.State,
                input.EncounterId
            )
        );

        foreach (var option in input.Options)
        {
            builder.Append('|');
            builder.Append(
                Join(
                    option.Index.ToString(CultureInfo.InvariantCulture),
                    option.InstanceId,
                    option.TemplateId,
                    option.Name,
                    option.Tier,
                    option.Enchant
                )
            );
        }

        return ComputeSha1(builder.ToString());
    }

    public static IList<RunLogOptionSnapshot> ProjectOptions(
        IEnumerable<RunLogSelectionOptionInput> options
    )
    {
        var projected = new List<RunLogOptionSnapshot>();
        foreach (var option in options)
        {
            projected.Add(
                new RunLogOptionSnapshot
                {
                    Index = option.Index,
                    InstanceId = option.InstanceId,
                    TemplateId = option.TemplateId,
                    Name = option.Name,
                    Tier = option.Tier,
                    Enchant = option.Enchant,
                    Tags = new List<string>(option.Tags),
                    Attributes = new Dictionary<string, object?>(option.Attributes),
                }
            );
        }

        return projected;
    }

    private static string Join(params string?[] parts)
    {
        return string.Join("|", parts);
    }

    private static string ComputeSha1(string content)
    {
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(content));
        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));

        return builder.ToString();
    }
}
