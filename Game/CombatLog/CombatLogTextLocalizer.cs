#nullable enable
using System.Globalization;
using TheBazaar.Utilities;

namespace BazaarPlusPlus.Game.CombatLog;

internal static class CombatLogTextLocalizer
{
    internal static string SystemEventStarted()
    {
        return IsChinese() ? "沙暴开始" : "Sandstorm started";
    }

    internal static string SystemEventCountdownStarted()
    {
        return IsChinese() ? "沙暴倒计时开始" : "Sandstorm countdown started";
    }

    internal static string CombatantDied(string target)
    {
        return IsChinese() ? $"{LocalizeToken(target)} 阵亡" : $"{target} died";
    }

    internal static string Reward(string rewardKind, double amount)
    {
        var label = LocalizeRewardKind(rewardKind);
        return IsChinese() ? $"{label} +{amount:0.##}" : $"{label} +{amount:0.##}";
    }

    internal static string SummaryEmptyFrame()
    {
        return IsChinese() ? "空帧" : "Empty frame";
    }

    internal static string SummaryLabel(string singular, int count)
    {
        if (IsChinese())
        {
            return singular switch
            {
                "event" => $"{count} 个事件",
                "combatant update" => $"{count} 个战斗单位更新",
                "card update" => $"{count} 个卡牌更新",
                "reward" => $"{count} 个奖励",
                "system event" => $"{count} 个系统事件",
                "unknown event" => $"{count} 个未知事件",
                _ => $"{count} 项",
            };
        }

        return count == 1 ? $"1 {singular}" : $"{count} {singular}s";
    }

    internal static string EffectExecuted(
        string? actionType,
        string effectId,
        string source,
        string target
    )
    {
        var localizedAction = LocalizeToken(actionType) ?? actionType ?? "Execute";
        return IsChinese()
            ? $"{localizedAction} {LocalizeToken(effectId) ?? effectId} {source} -> {LocalizeToken(target) ?? target}"
            : $"{localizedAction} {effectId} {source} -> {target}";
    }

    internal static string EffectTriggered(string effectId, string source, string target)
    {
        return IsChinese()
            ? $"触发 {LocalizeToken(effectId) ?? effectId} {source} -> {LocalizeToken(target) ?? target}"
            : $"Triggered {effectId} {source} -> {target}";
    }

    internal static string Aura(
        string effectId,
        string source,
        string? applied,
        string? removed
    )
    {
        if (IsChinese())
        {
            if (!string.IsNullOrEmpty(applied) && !string.IsNullOrEmpty(removed))
                return $"光环 {LocalizeToken(effectId) ?? effectId} {source} 施加 [{applied}] 移除 [{removed}]";
            if (!string.IsNullOrEmpty(applied))
                return $"光环 {LocalizeToken(effectId) ?? effectId} {source} 施加 [{applied}]";
            if (!string.IsNullOrEmpty(removed))
                return $"光环 {LocalizeToken(effectId) ?? effectId} {source} 移除 [{removed}]";
            return $"光环 {LocalizeToken(effectId) ?? effectId} {source}";
        }

        if (!string.IsNullOrEmpty(applied) && !string.IsNullOrEmpty(removed))
            return $"Aura {effectId} {source} applied [{applied}] removed [{removed}]";
        if (!string.IsNullOrEmpty(applied))
            return $"Aura {effectId} {source} applied [{applied}]";
        if (!string.IsNullOrEmpty(removed))
            return $"Aura {effectId} {source} removed [{removed}]";
        return $"Aura {effectId} {source}";
    }

    internal static string Enchantment(string source, string enchantmentLabel, bool isReverted)
    {
        return IsChinese()
            ? $"附魔{(isReverted ? "还原" : "施加")} {source} -> {enchantmentLabel}"
            : $"Enchant {(isReverted ? "reverted" : "applied")} {source} -> {enchantmentLabel}";
    }

    internal static string Transform(string source, string transformedCards)
    {
        return IsChinese()
            ? $"变形 {source} -> [{transformedCards}]"
            : $"Transform {source} -> [{transformedCards}]";
    }

    internal static string TransformReverted(string original, string revertedIds)
    {
        return IsChinese()
            ? $"变形还原 {original} <- [{revertedIds}]"
            : $"Transform reverted {original} from [{revertedIds}]";
    }

    internal static string QuestCompleted(string source, int groupIndex, int entryIndex)
    {
        return IsChinese()
            ? $"任务完成 {source} 组={groupIndex} 条目={entryIndex}"
            : $"Quest completed {source} group={groupIndex} entry={entryIndex}";
    }

    internal static string QuestUpdated(
        string source,
        int groupIndex,
        int entryIndex,
        int previousProgress,
        int currentProgress
    )
    {
        return IsChinese()
            ? $"任务更新 {source} 组={groupIndex} 条目={entryIndex} {previousProgress} -> {currentProgress}"
            : $"Quest updated {source} group={groupIndex} entry={entryIndex} {previousProgress} -> {currentProgress}";
    }

    internal static string LocalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        var localized = TryLocalizeToken(token);
        if (!string.IsNullOrWhiteSpace(localized))
            return localized;

        return token switch
        {
            "Opponent" => IsChinese() ? "对手" : "Opponent",
            "Player" => IsChinese() ? "玩家" : "Player",
            "burn" => IsChinese() ? "燃烧" : "burn",
            "poison" => IsChinese() ? "中毒" : "poison",
            _ => token,
        };
    }

    private static string? TryLocalizeToken(string token)
    {
        try
        {
            var localized = new LocalizableText(token).GetLocalizedText();
            if (!string.IsNullOrWhiteSpace(localized))
                return localized;
        }
        catch
        {
        }

        return null;
    }

    private static string LocalizeRewardKind(string rewardKind)
    {
        return rewardKind switch
        {
            "Monster gold" => IsChinese() ? "怪物金币" : rewardKind,
            "Monster xp" => IsChinese() ? "怪物经验" : rewardKind,
            _ => LocalizeToken(rewardKind),
        };
    }

    private static bool IsChinese()
    {
        var localizedPlayer = TryLocalizeToken("Player");
        if (!string.IsNullOrWhiteSpace(localizedPlayer) && ContainsCjk(localizedPlayer))
            return true;

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh";
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch >= 0x4E00 && ch <= 0x9FFF)
                return true;
        }

        return false;
    }
}
