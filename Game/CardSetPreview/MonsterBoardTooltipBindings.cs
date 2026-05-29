#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using TheBazaar.UI.Tooltips;

namespace BazaarPlusPlus.Game.CardSetPreview;

// Cached reflection MemberInfo handles into the game's MonsterBoardTooltip /
// CardPreviewBase types, extracted from ItemBoardOverlay. Resolved once at static
// init using the exact same AccessTools targets the overlay relied on before.
internal static class MonsterBoardTooltipBindings
{
    public static readonly Type? MonsterBoardTooltipType = AccessTools.TypeByName(
        "TheBazaar.UI.Tooltips.MonsterBoardTooltip"
    );

    public static readonly FieldInfo? MonsterBoardTooltipField = AccessTools.Field(
        typeof(CardTooltipController),
        "_monsterBoardTooltip"
    );

    public static readonly MethodInfo? HandlePoolingMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "HandlePooling")
            : null;

    public static readonly MethodInfo? AddCardMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "AddCard")
            : null;

    public static readonly MethodInfo? SetCarpetMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "SetCarpet")
            : null;

    public static readonly MethodInfo? ShowMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "Show")
            : null;

    public static readonly MethodInfo? HideMethod =
        MonsterBoardTooltipType != null
            ? AccessTools.Method(MonsterBoardTooltipType, "Hide")
            : null;

    public static readonly FieldInfo? SkillParentField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillParent")
            : null;

    public static readonly FieldInfo? SocketsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_sockets")
            : null;

    public static readonly FieldInfo? HealthTextField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_healthText")
            : null;

    public static readonly FieldInfo? CarpetImageField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_carpetImage")
            : null;

    public static readonly Type? CardPreviewBaseType = AccessTools.TypeByName(
        "TheBazaar.UI.CardPreviewBase"
    );

    public static readonly MethodInfo? CardPreviewShowMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Show") : null;

    public static readonly FieldInfo? SmallItemPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_smallItemPool")
            : null;

    public static readonly FieldInfo? MediumItemPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_mediumItemPool")
            : null;

    public static readonly FieldInfo? LargeItemPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_largeItemPool")
            : null;

    public static readonly FieldInfo? SkillPoolField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_skillPool")
            : null;

    public static readonly FieldInfo? ActiveCardsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_activeCards")
            : null;

    public static readonly FieldInfo? ActiveSkillsField =
        MonsterBoardTooltipType != null
            ? AccessTools.Field(MonsterBoardTooltipType, "_activeSkills")
            : null;
}
