#pragma warning disable CS0436
#nullable enable
using System;
using BazaarGameShared.TempoNet.Enums;
using BazaarPlusPlus.Game.EndOfRun;
using HarmonyLib;
using TMPro;
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
            var ratingBeforeRun = ResolveRatingBeforeRun();
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

    private static int? ResolveRatingBeforeRun()
    {
        return Singleton<GamePlaySceneSingleton>.Instance?.RatingBeforeRun;
    }

    private static int? ResolveRatingAfterRun()
    {
        return Singleton<GamePlaySceneSingleton>.Instance?.RatingAfterRun;
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
        var rankPositioner = Traverse
            .Create(controller)
            .Field("rankPositioner")
            .GetValue<RectTransform>();
        if (rankPositioner == null)
            return null;

        var existing = rankPositioner.Find(RatingLineObjectName)?.GetComponent<TextMeshProUGUI>();
        if (existing != null)
        {
            SyncStyle(sourceLabel, existing);
            return existing;
        }

        var clone = UnityEngine.Object.Instantiate(sourceLabel.gameObject, rankPositioner);
        clone.name = RatingLineObjectName;
        clone.SetActive(true);

        var ratingLine = clone.GetComponent<TextMeshProUGUI>();
        if (ratingLine == null)
            return null;

        SyncStyle(sourceLabel, ratingLine);

        var rect = ratingLine.rectTransform;
        rect.SetSiblingIndex(rankPositioner.childCount - 1);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, -66f);
        rect.localScale = Vector3.one;

        var layout = clone.GetComponent<LayoutElement>() ?? clone.AddComponent<LayoutElement>();
        layout.minHeight = 22f;
        layout.preferredHeight = 22f;

        return ratingLine;
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
