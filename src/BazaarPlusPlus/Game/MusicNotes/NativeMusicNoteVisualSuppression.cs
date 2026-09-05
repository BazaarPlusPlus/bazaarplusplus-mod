#nullable enable
using HarmonyLib;
using UnityEngine;

namespace BazaarPlusPlus.Game.MusicNotes;

internal sealed class NativeMusicNoteVisualSuppression
{
    private static readonly System.Reflection.FieldInfo? VfxField = AccessTools.Field(
        typeof(SocketEffectController),
        "_socketVfxController"
    );
    private readonly List<Renderer> _renderers = [];
    private readonly MusicNoteVisualLease<Renderer> _lease = new(
        renderer => renderer != null && renderer.forceRenderingOff,
        (renderer, hidden) =>
        {
            if (renderer != null)
                renderer.forceRenderingOff = hidden;
        }
    );

    internal void BeginRefresh() => _lease.BeginRefresh();

    internal void Suppress(SocketEffectController? controller)
    {
        if (
            controller == null
            || VfxField?.GetValue(controller) is not SocketEffectVFXController vfx
            || vfx == null
        )
            return;
        // Scope to the real card's VFX root, never the socket parent containing our previews.
        _renderers.Clear();
        vfx.GetComponentsInChildren(includeInactive: true, _renderers);
        foreach (var renderer in _renderers)
            if (renderer != null)
                _lease.Suppress(renderer);
    }

    internal void EndRefresh() => _lease.EndRefresh();

    internal void Restore() => _lease.Restore();
}
