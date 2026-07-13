#nullable enable
using System;
using System.Reflection;
using UnityEngine;

namespace BazaarPlusPlus.GameInterop.CardPreview;

internal sealed class NativeCardPreviewHoverRelay
{
    private static readonly MethodInfo? OnHoverMethod =
        NativeCardPreviewReflection.ResolvePublicInstanceMethod("OnHover");
    private static readonly MethodInfo? OnHoverOutMethod =
        NativeCardPreviewReflection.ResolvePublicInstanceMethod("OnHoverOut");

    private readonly Action<NativeCardPreviewFailure>? _reportFailure;
    private Component? _card;
    private bool _hovered;

    public NativeCardPreviewHoverRelay(Action<NativeCardPreviewFailure>? reportFailure = null) =>
        _reportFailure = reportFailure;

    public void Bind(Component? card)
    {
        if (ReferenceEquals(_card, card))
            return;

        Clear();
        _card = card;
    }

    public void Clear()
    {
        InvokeHoverOut();
        _card = null;
    }

    public bool InvokeHover()
    {
        if (_card == null)
            return false;
        if (_hovered)
            return true;

        if (!InvokeSafe(_card, OnHoverMethod, NativeCardPreviewOperation.InvokeHover))
            return false;

        _hovered = true;
        NativeCardPreviewHoverTracker.NotifyHover(_card);
        return true;
    }

    public void InvokeHoverOut()
    {
        if (_card == null || !_hovered)
            return;

        var invoked = InvokeSafe(
            _card,
            OnHoverOutMethod,
            NativeCardPreviewOperation.InvokeHoverOut
        );
        _hovered = false;
        if (invoked)
            NativeCardPreviewHoverTracker.NotifyHoverOut(_card);
    }

    private bool InvokeSafe(
        Component target,
        MethodInfo? method,
        NativeCardPreviewOperation operation
    )
    {
        if (target == null || method == null)
        {
            if (method == null)
            {
                _reportFailure?.Invoke(
                    new NativeCardPreviewFailure(
                        operation,
                        NativeCardPreviewFailureReason.ReflectionUnavailable,
                        templateId: null
                    )
                );
            }
            return false;
        }

        try
        {
            method.Invoke(target, Array.Empty<object>());
            return true;
        }
        catch (TargetInvocationException ex)
        {
            _reportFailure?.Invoke(
                new NativeCardPreviewFailure(
                    operation,
                    NativeCardPreviewFailureReason.ReflectionException,
                    templateId: null,
                    ex.InnerException ?? ex
                )
            );
            return false;
        }
        catch (Exception ex)
        {
            _reportFailure?.Invoke(
                new NativeCardPreviewFailure(
                    operation,
                    NativeCardPreviewFailureReason.ReflectionException,
                    templateId: null,
                    ex
                )
            );
            return false;
        }
    }
}
