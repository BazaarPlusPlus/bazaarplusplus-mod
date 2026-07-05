#nullable enable
using System;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarPlusPlus.Infrastructure;
using HarmonyLib;
using TheBazaar.Tooltips;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal static class NativeCardPreviewReflection
{
    private const string CardPreviewBaseTypeName = "TheBazaar.UI.CardPreviewBase";

    public static readonly Type? CardPreviewBaseType = AccessTools.TypeByName(
        CardPreviewBaseTypeName
    );

    public static readonly MethodInfo? SetUpMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "SetUp") : null;

    public static readonly MethodInfo? ShowMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Show") : null;

    public static readonly MethodInfo? OnHoverMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "OnHover") : null;

    public static readonly MethodInfo? ResizeMethod =
        CardPreviewBaseType != null ? AccessTools.Method(CardPreviewBaseType, "Resize") : null;

    public static readonly PropertyInfo? SizeProperty =
        CardPreviewBaseType != null ? AccessTools.Property(CardPreviewBaseType, "Size") : null;

    private static readonly FieldInfo? TooltipDataField =
        CardPreviewBaseType != null ? AccessTools.Field(CardPreviewBaseType, "_tooltipData") : null;

    private static readonly FieldInfo? ClientCardField =
        CardPreviewBaseType != null ? AccessTools.Field(CardPreviewBaseType, "_clientCard") : null;

    public static MethodInfo? ResolvePublicInstanceMethod(string name)
    {
        if (CardPreviewBaseType == null || string.IsNullOrWhiteSpace(name))
            return null;

        return CardPreviewBaseType.GetMethod(
            name,
            BindingFlags.Public | BindingFlags.Instance,
            null,
            Type.EmptyTypes,
            null
        );
    }

    public static bool TryGetTooltipData(Component cardPreview, out CardTooltipData tooltipData)
    {
        tooltipData = null!;
        if (!IsCardPreview(cardPreview) || TooltipDataField == null)
            return false;

        try
        {
            if (TooltipDataField.GetValue(cardPreview) is not CardTooltipData value)
                return false;

            tooltipData = value;
            return true;
        }
        catch (Exception ex)
        {
            LogReflectionFailure("get _tooltipData", ex);
            return false;
        }
    }

    public static bool TryGetClientCard(Component cardPreview, out Card card)
    {
        card = null!;
        if (!IsCardPreview(cardPreview) || ClientCardField == null)
            return false;

        try
        {
            if (ClientCardField.GetValue(cardPreview) is not Card value)
                return false;

            card = value;
            return true;
        }
        catch (Exception ex)
        {
            LogReflectionFailure("get _clientCard", ex);
            return false;
        }
    }

    public static bool TrySetTooltipData(Component cardPreview, CardTooltipData tooltipData)
    {
        if (!IsCardPreview(cardPreview) || TooltipDataField == null || tooltipData == null)
            return false;

        try
        {
            TooltipDataField.SetValue(cardPreview, tooltipData);
            return true;
        }
        catch (Exception ex)
        {
            LogReflectionFailure("set _tooltipData", ex);
            return false;
        }
    }

    public static bool CanInvokeOnHover(Component cardPreview)
    {
        return IsCardPreview(cardPreview) && OnHoverMethod != null;
    }

    public static bool TryInvokeOnHover(Component cardPreview)
    {
        if (!IsCardPreview(cardPreview) || OnHoverMethod is not { } method)
            return false;

        try
        {
            method.Invoke(cardPreview, Array.Empty<object>());
            return true;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Debug(
                "TooltipPreview",
                $"Preview OnHover threw: {ex.InnerException?.Message ?? ex.Message}"
            );
            return false;
        }
        catch (Exception ex)
        {
            BppLog.Debug("TooltipPreview", $"Preview OnHover invocation failed: {ex.Message}");
            return false;
        }
    }

    private static void LogReflectionFailure(string operation, Exception ex)
    {
        BppLog.Debug(
            "TooltipPreview",
            $"CardPreview reflection failed operation={operation}: {ex.Message}"
        );
    }

    public static void ApplyLayerRecursive(GameObject root, int layer)
    {
        if (root == null)
            return;

        root.layer = layer;
        var transform = root.transform;
        for (var i = 0; i < transform.childCount; i++)
        {
            var child = transform.GetChild(i);
            if (child != null)
                ApplyLayerRecursive(child.gameObject, layer);
        }
    }

    private static bool IsCardPreview(Component cardPreview)
    {
        return cardPreview != null
            && CardPreviewBaseType != null
            && CardPreviewBaseType.IsInstanceOfType(cardPreview);
    }
}
