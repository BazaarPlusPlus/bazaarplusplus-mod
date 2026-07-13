#nullable enable
using System;
using System.Reflection;
using BazaarPlusPlus.GameInterop.CardPreview;

namespace BazaarPlusPlus.GameInterop.ItemBoardPreview;

internal static class ItemBoardPreviewInitialization
{
    internal static bool CanInitialize(
        MethodInfo? setUpMethod,
        Action<NativeCardPreviewFailure>? reportFailure
    )
    {
        if (setUpMethod != null)
            return true;

        reportFailure?.Invoke(
            new NativeCardPreviewFailure(
                NativeCardPreviewOperation.SetUp,
                NativeCardPreviewFailureReason.ReflectionUnavailable,
                templateId: null
            )
        );
        return false;
    }
}
