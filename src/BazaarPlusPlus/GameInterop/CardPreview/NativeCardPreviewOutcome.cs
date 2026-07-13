#nullable enable
using System;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal enum NativeCardPreviewOperation
{
    ResolveTemplate,
    ResolveKind,
    Instantiate,
    ResolvePreviewType,
    ResolvePreviewComponent,
    SetUp,
    ResolveRect,
    Resize,
    Show,
    GetTooltipData,
    GetClientCard,
    SetTooltipData,
    InvokeHover,
    InvokeHoverOut,
}

internal enum NativeCardPreviewFailureReason
{
    StaticDataUnavailable,
    TemplateUnavailable,
    UnsupportedCardType,
    AssetLoaderUnavailable,
    PreviewTypeUnavailable,
    PreviewComponentUnavailable,
    InstantiateException,
    SetUpException,
    RectUnavailable,
    ResizeException,
    ShowException,
    ReflectionUnavailable,
    ReflectionException,
}

internal sealed class NativeCardPreviewFailure
{
    internal NativeCardPreviewFailure(
        NativeCardPreviewOperation operation,
        NativeCardPreviewFailureReason reason,
        Guid? templateId,
        Exception? exception = null
    )
    {
        Operation = operation;
        Reason = reason;
        TemplateId = templateId;
        Exception = exception;
    }

    internal NativeCardPreviewOperation Operation { get; }
    internal NativeCardPreviewFailureReason Reason { get; }
    internal Guid? TemplateId { get; }
    internal Exception? Exception { get; }
}

internal readonly struct NativeCardPreviewCreateOutcome
{
    internal NativeCardPreviewCreateOutcome(
        NativeCardPreviewHandle? handle,
        NativeCardPreviewFailure? failure
    )
    {
        Handle = handle;
        Failure = failure;
    }

    internal NativeCardPreviewHandle? Handle { get; }
    internal NativeCardPreviewFailure? Failure { get; }

    internal static NativeCardPreviewCreateOutcome Ready(NativeCardPreviewHandle handle) =>
        new(handle, null);

    internal static NativeCardPreviewCreateOutcome Degraded(NativeCardPreviewFailure failure) =>
        new(null, failure);

    internal static NativeCardPreviewCreateOutcome Unavailable() => new(null, null);
}

internal readonly struct NativeCardPreviewInstantiateOutcome
{
    internal NativeCardPreviewInstantiateOutcome(Component? card, NativeCardPreviewFailure? failure)
    {
        Card = card;
        Failure = failure;
    }

    internal Component? Card { get; }
    internal NativeCardPreviewFailure? Failure { get; }
}
