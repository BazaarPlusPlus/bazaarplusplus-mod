#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BazaarPlusPlus.Game.VoiceSubtitles;

internal static class VoiceLineCatalog
{
    private static readonly object SyncRoot = new();
    private static VoiceLine[] _catalogLines = Array.Empty<VoiceLine>();
    private static string _catalogName = "empty";

    private static readonly IReadOnlyDictionary<string, string> CharacterAliases = new Dictionary<
        string,
        string
    >(StringComparer.OrdinalIgnoreCase)
    {
        ["Dooley"] = "Dooley",
        ["Jules"] = "Jules",
        ["Karnok"] = "Karnok",
        ["Mak"] = "Mak",
        ["Pyg"] = "Pygmalien",
        ["Pygmalien"] = "Pygmalien",
        ["Stelle"] = "Stelle",
        ["Vanessa"] = "Vanessa",
    };

    private static readonly VoiceLine[] SampleLines =
    {
        new("001_VanessaPvPDefeat1", "Defeat does not defeat me.", "失败不会打败我。", 2.15f),
        new(
            "002_VanessaUpgrade1",
            "Ooh, the keener the blade, the quicker the fight.",
            "嚯，刀刃越利，胜负越快。",
            3.42f
        ),
        new(
            "003_VanessaPvPIntro2",
            "You should know, I'm a bit of a legend on Polago.",
            "你该知道，我在波拉戈也算个传奇。",
            3.38f
        ),
        new(
            "004_VanessaRunVictory2",
            "Read the tides and listen to the wind, they will guide your strikes.",
            "看潮汐，听海风，它们会指引你的出手。",
            5.59f
        ),
        new(
            "005_VanessaLastLife2",
            "Ships may sink, but I'm a good swimmer.",
            "船会沉，但我水性不错。",
            3.27f
        ),
        new(
            "006_VanessaPvEVictory2",
            "Was that even a battle? Maybe a skirmish?",
            "那也算战斗吗？顶多是一场小冲突。",
            3.4f
        ),
        new(
            "007_VanessaNoBuyGold1",
            "Heh, must be a hole in my coin purse.",
            "呵，看来我的钱袋漏了个洞。",
            3.08f
        ),
        new(
            "009_VanessaIdle3",
            "You remind me of my first voyage around the globe. Unbearably slow.",
            "你让我想起第一次环球航行，慢得让人难以忍受。",
            5.37f
        ),
        new("013_VanessaLevelUp4", "Rally, men! Blades to the wind!", "船员们，迎风举刃！", 2.11f),
        new("014_VanessaNoBuySpace4", "Maybe if we tow the barge.", "也许拖艘驳船来就行。", 2.16f),
        new(
            "024_VanessaMultiClick3",
            "My father was the first captain to resist, and he paid the price for it.",
            "我的父亲是第一个奋起反抗的船长，也为此付出了代价。",
            5.63f
        ),
    };

    private static readonly IReadOnlyDictionary<string, string> HookTokens = new Dictionary<
        string,
        string
    >(StringComparer.Ordinal)
    {
        ["OnIdle"] = "Idle",
        ["OnMultiClick"] = "MultiClick",
        ["OnLastLife"] = "LastLife",
        ["OnNoBuyGold"] = "NoBuyGold",
        ["OnNoBuySpace"] = "NoBuySpace",
        ["OnPvEVictoryDefeat"] = "PvEVictory",
        ["OnPvPIntro"] = "PvPIntro",
        ["OnPvPVictoryDefeat"] = "PvPDefeat",
        ["OnRunVictoryDefeat"] = "RunVictory",
        ["OnLevelUp"] = "LevelUp",
        ["OnUpgrade"] = "Upgrade",
    };

    internal static void ReplaceCatalog(VoiceLine[] lines, string catalogName)
    {
        var nextLines = lines ?? Array.Empty<VoiceLine>();
        var nextName = string.IsNullOrWhiteSpace(catalogName) ? "unknown" : catalogName;
        lock (SyncRoot)
        {
            _catalogLines = nextLines.ToArray();
            _catalogName = nextName;
        }
    }

    internal static void Reset()
    {
        lock (SyncRoot)
        {
            _catalogLines = Array.Empty<VoiceLine>();
            _catalogName = "empty";
        }
    }

    public static VoiceLine Resolve(string? eventReferenceText, string sourceLabel, string hookName)
    {
        return ResolveDetailed(eventReferenceText, sourceLabel, hookName).Line;
    }

    public static VoiceLineResolution ResolveDetailed(
        string? eventReferenceText,
        string sourceLabel,
        string hookName
    )
    {
        if (!string.IsNullOrWhiteSpace(eventReferenceText))
        {
            var exact = ResolveExactDetailed(eventReferenceText!);
            if (!string.IsNullOrEmpty(exact.Line.Stem))
                return exact;
        }

        if (HookTokens.TryGetValue(hookName, out var tokenFromHook))
        {
            var characterName = ResolveCharacterName(eventReferenceText);
            if (!string.IsNullOrEmpty(characterName))
            {
                var characterResolution = ResolveCharacterHookFallback(
                    characterName!,
                    tokenFromHook,
                    hookName
                );
                if (!string.IsNullOrEmpty(characterResolution.Line.Stem))
                    return characterResolution;
            }
        }

        return new VoiceLineResolution(default, "unresolved", hookName, "none");
    }

    private static VoiceLineResolution ResolveExactDetailed(string eventReferenceText)
    {
        var normalizedTokens = ExtractLookupTokens(eventReferenceText);
        var catalog = SnapshotCatalog();

        foreach (var line in catalog.Lines)
        {
            if (MatchesExactStem(eventReferenceText, normalizedTokens, line.Stem))
                return new VoiceLineResolution(line, "event-stem", line.Stem, catalog.Name);
        }

        foreach (var line in SampleLines)
        {
            if (MatchesExactStem(eventReferenceText, normalizedTokens, line.Stem))
                return new VoiceLineResolution(line, "event-stem", line.Stem, "sample");
        }

        return default;
    }

    private static bool MatchesExactStem(
        string lookupText,
        IReadOnlyList<string> normalizedLookupTokens,
        string stem
    )
    {
        if (lookupText.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        var normalizedStem = NormalizeVoiceStem(stem);
        if (normalizedStem.Length < 8)
            return false;

        foreach (var token in normalizedLookupTokens)
        {
            if (token.Length < 8)
                continue;

            if (
                token.Equals(normalizedStem, StringComparison.OrdinalIgnoreCase)
                || token.IndexOf(normalizedStem, StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> ExtractLookupTokens(string lookupText)
    {
        return lookupText
            .Split(
                new[] { ' ', '/', '\\', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            )
            .Select(NormalizeVoiceStem)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static string NormalizeVoiceStem(string value)
    {
        var start = 0;
        while (start < value.Length && char.IsDigit(value[start]))
            start++;
        if (start < value.Length && value[start] == '_')
            start++;

        var builder = new StringBuilder(value.Length - start);
        for (var i = start; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString().Replace("pygmalien", "pyg", StringComparison.Ordinal);
    }

    private static VoiceLineResolution ResolveCharacterHookFallback(
        string characterName,
        string token,
        string hookName
    )
    {
        var catalog = SnapshotCatalog();
        var candidates = catalog
            .Lines.Where(line =>
                IsCharacterLine(line.Stem, characterName)
                && line.Stem.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
            )
            .ToArray();

        if (candidates.Length == 1)
        {
            return new VoiceLineResolution(
                candidates[0],
                "character-hook-unique",
                $"{characterName}:{token}",
                catalog.Name,
                candidates.Length
            );
        }

        if (candidates.Length > 1)
        {
            return new VoiceLineResolution(
                default,
                "character-hook-ambiguous",
                $"{characterName}:{token}",
                catalog.Name,
                candidates.Length
            );
        }

        return default;
    }

    private static bool IsCharacterLine(string stem, string characterName)
    {
        return stem.IndexOf($"_{characterName}", StringComparison.OrdinalIgnoreCase) >= 0
            || stem.IndexOf($"{characterName}", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? ResolveCharacterName(string? eventReferenceText)
    {
        if (string.IsNullOrWhiteSpace(eventReferenceText))
            return null;

        foreach (var pair in CharacterAliases)
        {
            var alias = pair.Key;
            if (
                eventReferenceText!.IndexOf($"/{alias}/", StringComparison.OrdinalIgnoreCase) >= 0
                || eventReferenceText.IndexOf($"VO_{alias}_", StringComparison.OrdinalIgnoreCase)
                    >= 0
                || eventReferenceText.IndexOf($"_{alias}_", StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static (VoiceLine[] Lines, string Name) SnapshotCatalog()
    {
        lock (SyncRoot)
        {
            return (_catalogLines, _catalogName);
        }
    }
}
