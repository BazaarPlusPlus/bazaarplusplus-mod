#pragma warning disable CS0436
#nullable enable
using System;
using System.Linq;
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Game.EndOfRun;
using HarmonyLib;
using TMPro;
using TheBazaar;
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(EndOfRunRankController), "UpdateRank")]
internal static class LegendaryEndOfMatchRatingUpdateRankPatch
{
    [HarmonyPostfix]
    private static void Postfix(EndOfRunRankController __instance, ERank rank)
    {
        LegendaryEndOfMatchRatingUi.Refresh(__instance, rank == ERank.Legendary);
    }
}

[HarmonyPatch(typeof(EndOfRunRankController), "UpdateRankDisplay")]
internal static class LegendaryEndOfMatchRatingUpdateRankDisplayPatch
{
    [HarmonyPostfix]
    private static void Postfix(EndOfRunRankController __instance)
    {
        var postRunRank = LegendaryEndOfMatchRatingUi.GetPostRunRank(__instance);
        LegendaryEndOfMatchRatingUi.Refresh(__instance, postRunRank == ERank.Legendary);
    }
}

internal static class LegendaryEndOfMatchRatingUi
{
    private const string RatingLineObjectName = "BppLegendaryRatingLine";
    private const float MinimumVerticalOffset = 28f;
    private const float AdditionalVerticalSpacing = 6f;
    private static readonly AccessTools.FieldRef<EndOfRunRankController, ERank> PostRunRankRef =
        AccessTools.FieldRefAccess<EndOfRunRankController, ERank>("postRunRank");

    public static ERank GetPostRunRank(EndOfRunRankController controller)
    {
        return PostRunRankRef(controller);
    }

    public static void Refresh(EndOfRunRankController controller, bool isLegendary)
    {
        try
        {
            var ratingBeforeRun = ResolveRatingBeforeRun(controller);
            var ratingAfterRun = ResolveRatingAfterRun();
            var shouldShow = LegendaryEndOfMatchRatingDisplayPolicy.ShouldShow(
                isLegendary,
                ratingBeforeRun,
                ratingAfterRun
            );

            var sourceLabel = FindActiveRankLabel(controller);
            if (sourceLabel == null)
                return;

            var ratingLine = FindOrCreateRatingLine(controller, sourceLabel);
            if (ratingLine == null)
                return;

            ratingLine.gameObject.SetActive(shouldShow);
            if (!shouldShow)
                return;

            ratingLine.text = LegendaryEndOfMatchRatingFormatter.BuildLine(
                ratingBeforeRun!.Value,
                ratingAfterRun!.Value
            );
        }
        catch (Exception ex)
        {
            BppLog.Warn("LegendaryEndOfMatchRating", $"Failed to refresh rating line: {ex.Message}");
        }
    }

    private static int? ResolveRatingBeforeRun(EndOfRunRankController controller)
    {
        var stateMachine = controller.StateMachine;
        if (stateMachine == null)
            return null;

        var commonData = Traverse.Create(stateMachine).Field("CommonData").GetValue<object>();
        if (commonData == null)
            return null;

        var currentSeasonRank = AccessTools
            .Field(commonData.GetType(), "CurrentSeasonRank")
            ?.GetValue(commonData);
        if (currentSeasonRank == null)
            return null;

        return AccessTools.Field(currentSeasonRank.GetType(), "Rating")?.GetValue(currentSeasonRank) as int?
            ?? AccessTools.Property(currentSeasonRank.GetType(), "Rating")?.GetValue(currentSeasonRank) as int?;
    }

    private static int? ResolveRatingAfterRun()
    {
        return Data.Rank?.CurrentSeasonRank?.Rating;
    }

    private static TextMeshProUGUI? FindActiveRankLabel(EndOfRunRankController controller)
    {
        var bigDisplay = Traverse.Create(controller).Field("bigDisplay").GetValue<object>();
        var bigDisplaySwap = Traverse.Create(controller).Field("bigDisplaySwap").GetValue<object>();

        var swapLabel = AccessTools.Field(bigDisplaySwap?.GetType(), "rankLabel")?.GetValue(bigDisplaySwap)
            as TextMeshProUGUI;
        if (swapLabel != null && swapLabel.gameObject.activeInHierarchy)
            return swapLabel;

        return AccessTools.Field(bigDisplay?.GetType(), "rankLabel")?.GetValue(bigDisplay)
            as TextMeshProUGUI;
    }

    private static TextMeshProUGUI? FindOrCreateRatingLine(
        EndOfRunRankController controller,
        TextMeshProUGUI sourceLabel
    )
    {
        var sourceRect = sourceLabel.rectTransform;
        var existing = FindExistingRatingLine(controller);
        if (existing == null)
        {
            existing = CreateRatingLine(sourceRect, sourceLabel);
            if (existing == null)
                return null;
        }

        var ratingRect = existing.rectTransform;
        if (ratingRect.parent != sourceRect)
            ratingRect.SetParent(sourceRect, worldPositionStays: false);

        SyncStyle(sourceLabel, existing);
        SyncPosition(sourceRect, ratingRect);
        return existing;
    }

    private static TextMeshProUGUI? FindExistingRatingLine(EndOfRunRankController controller)
    {
        return controller.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)
            .FirstOrDefault(text => text.name == RatingLineObjectName);
    }

    private static TextMeshProUGUI? CreateRatingLine(
        RectTransform sourceRect,
        TextMeshProUGUI sourceLabel
    )
    {
        var lineObject = new GameObject(RatingLineObjectName, typeof(RectTransform));
        lineObject.layer = sourceLabel.gameObject.layer;
        lineObject.SetActive(true);

        var rect = lineObject.GetComponent<RectTransform>();
        if (rect == null)
            return null;

        rect.SetParent(sourceRect, worldPositionStays: false);

        var ratingLine = lineObject.AddComponent<TextMeshProUGUI>();
        var layout = lineObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        return ratingLine;
    }

    private static void SyncPosition(RectTransform sourceRect, RectTransform ratingRect)
    {
        ratingRect.anchorMin = new Vector2(0.5f, 0.5f);
        ratingRect.anchorMax = new Vector2(0.5f, 0.5f);
        ratingRect.pivot = new Vector2(0.5f, 0.5f);
        ratingRect.sizeDelta = sourceRect.sizeDelta;
        ratingRect.localScale = Vector3.one;
        ratingRect.SetSiblingIndex(ratingRect.parent.childCount - 1);

        var verticalOffset = Mathf.Max(
            MinimumVerticalOffset,
            (sourceRect.rect.height * 0.5f) + AdditionalVerticalSpacing
        );
        ratingRect.anchoredPosition = new Vector2(0f, -verticalOffset);
    }

    private static void SyncStyle(TextMeshProUGUI sourceLabel, TextMeshProUGUI ratingLine)
    {
        ratingLine.font = sourceLabel.font;
        ratingLine.fontSharedMaterial = sourceLabel.fontSharedMaterial;
        ratingLine.alignment = TextAlignmentOptions.Center;
        ratingLine.color = sourceLabel.color;
        ratingLine.richText = true;
        ratingLine.enableAutoSizing = false;
        ratingLine.fontSize = Math.Max(18f, sourceLabel.fontSize * 0.32f);
        ratingLine.textWrappingMode = TextWrappingModes.NoWrap;
        ratingLine.overflowMode = TextOverflowModes.Overflow;
        ratingLine.raycastTarget = false;
    }
}
