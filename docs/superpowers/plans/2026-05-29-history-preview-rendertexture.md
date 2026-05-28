# History Panel Preview RenderTexture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render the History Panel battle-board preview with an offscreen orthographic Camera into a fixed 2400×600 RenderTexture, displayed in the existing UI Toolkit `_previewImage` (`ScaleToFit`), so UI Toolkit owns all scaling and the preview survives resolution changes with zero manual tuning.

**Architecture:** Rewrite `BattleBoardPreview` internals from a `ScreenSpaceOverlay` Canvas to a `ScreenSpaceCamera` Canvas + ortho Camera (live, transparent clear, culling mask = preview layer) + fixed RenderTexture, reusing the proven setup from the pre-`ce20923` renderer. HistoryPanel pushes `OutputTexture` into the view via the already-existing `SetPreviewTexture`, and the overlay positioning / tuner / panelScale plumbing is deleted.

**Tech Stack:** C#, Unity uGUI (Camera/Canvas/RenderTexture), Unity UI Toolkit (`Image`), HarmonyLib reflection (existing card pool), BepInEx.

**Testing note (per repo `.rules`):** The RT rendering has no meaningful unit-test seam (needs Unity runtime + the game's `MonsterBoardTooltip`). The extractable logic (`HistoryPanelPreviewSignatureGate`, `HistoryPanelPreviewGenerationGuard`) is reused unchanged and already covered by `HistoryPanelPreview.Tests`. Per `.rules` ("acceptable to ship without a new test when there is no meaningful seam"; "do not add coverage-only tests"), no new unit tests are added. Verification = `dotnet build` clean + existing tests stay green + a manual in-game visual check (must be done by the user — the game locks the plugin DLLs during build copy).

**Compilation-coupling note:** `HistoryPanel.cs::ApplyPreviewContainerBounds` reads tuner fields (`_debugX`…) defined in `HistoryPanel.PreviewTunerDebug.cs`, and that tuner file calls `BattleBoardPreview.SetClipMaskEnabled` / `ApplyPreviewContainerBounds`. These are mutually dependent, so Task 1–3 must all land before the project compiles again; they form one coherent commit (Task 4).

---

## File Structure

- **Rewrite:** `Game/HistoryPanel/Preview/BattleBoardPreview.cs` — same public API (`Render` / `Hide` / `Dispose` / `CancelPending`) **+ new `Texture? OutputTexture`**; internals become Camera+RT. Removes overlay-only `SetPosition` / `SetClipSize` / `SetCardScale` / `SetClipMaskEnabled` and the clip/board RectTransforms + RectMask2D.
- **Modify:** `Game/HistoryPanel/HistoryPanel.cs` — delete `ApplyPreviewContainerBounds` + `ComputePreviewPanelScale`; simplify `EnsurePreviewRenderer`; push texture in `OnPreviewPhase`; clear texture in `DisposePreviewRenderer`.
- **Modify:** `Game/HistoryPanel/HistoryPanel.UiToolkit.cs` — add `SetPreviewTexture` wrapper; drop the `ApplyPreviewContainerBounds` call from `OnPreviewContainerBoundsChanged` (leave the bounds fields/event dormant — conservative, no view-side surgery).
- **Delete:** `Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs` — the entire temporary tuner.
- **Unchanged (reused):** `HistoryPanelPreviewCardPool`, `HistoryPanelPreviewLayout`, `HistoryPanelPreviewGenerationGuard`, `HistoryPanelPreviewSignatureGate`, `HistoryPanelPreviewTextureGeometry` (constants), `HistoryItemSpec`, `HistoryPanelUiToolkitView.SetPreviewTexture`, `_previewImage`.

---

### Task 1: Rewrite BattleBoardPreview to offscreen Camera + fixed RenderTexture

**Files:**
- Rewrite: `Game/HistoryPanel/Preview/BattleBoardPreview.cs`

- [ ] **Step 1: Replace the whole file with the Camera+RT implementation**

```csharp
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Cards;
using BazaarGameShared.Domain.Cards.Item;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.Game.HistoryPanel.Data;
using BazaarPlusPlus.GameInterop;
using BazaarPlusPlus.Infrastructure;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarPlusPlus.Game.HistoryPanel.Preview;

internal enum BattleBoardRenderPhase
{
    Empty,
    InitFailed,
    Loading,
    Done,
}

// Offscreen Camera + fixed-aspect RenderTexture board preview. Spawns CardPreviewBase clones
// (reflected off MonsterBoardTooltip) into 10 sockets under a ScreenSpaceCamera canvas; a
// dedicated orthographic Camera renders them continuously into a fixed 2400x600 RenderTexture.
// The caller shows OutputTexture in a UI Toolkit Image (ScaleToFit), so UI Toolkit owns all
// container scaling / letterboxing / resolution adaptation. The camera clears transparent so
// letterbox margins show the container background.
internal sealed class BattleBoardPreview
{
    private const int DefaultLayer = 30;
    private const int TextureWidth = HistoryPanelPreviewTextureGeometry.NativeBoardWidth; // 2400
    private const int TextureHeight = HistoryPanelPreviewTextureGeometry.NativeBoardHeight; // 600
    private const float CanvasPlaneDistance = 100f;
    private const float CanvasReferencePixelsPerUnit = 100f;

    private readonly int _layer;
    private GameObject? _root;
    private Canvas? _canvas;
    private Camera? _camera;
    private RenderTexture? _texture;
    private RectTransform[]? _sockets;
    private HistoryPanelPreviewCardPool? _pool;
    private readonly List<Component> _active = new();
    private readonly List<Task> _activeSetUpTasks = new();
    private readonly HistoryPanelPreviewGenerationGuard _generation = new();
    private string? _renderedSignature;

    public BattleBoardPreview(int layer = DefaultLayer)
    {
        _layer = layer;
    }

    // The live RenderTexture the caller displays in a UI Toolkit Image. Null until the first
    // successful EnsureInitialized inside Render.
    public Texture? OutputTexture => _texture;

    public void CancelPending()
    {
        _generation.Bump();
    }

    public IEnumerator Render(
        IReadOnlyList<HistoryItemSpec>? cards,
        string? signature = null,
        Action<BattleBoardRenderPhase>? onPhase = null,
        Action? onComplete = null
    )
    {
        var snapshot = _generation.Bump();

        if (cards == null || cards.Count == 0)
        {
            onPhase?.Invoke(BattleBoardRenderPhase.Empty);
            Hide();
            onComplete?.Invoke();
            yield break;
        }

        if (!EnsureInitialized())
        {
            onPhase?.Invoke(BattleBoardRenderPhase.InitFailed);
            onComplete?.Invoke();
            yield break;
        }

        if (
            !string.IsNullOrEmpty(_renderedSignature)
            && !string.IsNullOrEmpty(signature)
            && string.Equals(_renderedSignature, signature, StringComparison.Ordinal)
        )
        {
            onPhase?.Invoke(BattleBoardRenderPhase.Done);
            onComplete?.Invoke();
            yield break;
        }

        onPhase?.Invoke(BattleBoardRenderPhase.Loading);

        _root!.SetActive(true);
        ReturnActiveCardsToPool();
        _activeSetUpTasks.Clear();

        SpawnCards(cards);

        var aggregate = Task.WhenAll(_activeSetUpTasks);
        while (!aggregate.IsCompleted && _generation.IsCurrent(snapshot))
            yield return null;

        if (!_generation.IsCurrent(snapshot))
            yield break;

        ShowAllActiveCards();
        Canvas.ForceUpdateCanvases();

        // Settle layout for one frame so Resize/Show activation is committed before the live
        // camera samples the canvas.
        yield return null;
        if (!_generation.IsCurrent(snapshot))
            yield break;

        Canvas.ForceUpdateCanvases();

        // Cache the signature only when all SetUp tasks succeeded; faulted frames stay
        // un-cached so the next selection retries instead of locking in a half-rendered board.
        if (HistoryPanelPreviewSignatureGate.ShouldCache(aggregate))
            _renderedSignature = signature;

        onPhase?.Invoke(BattleBoardRenderPhase.Done);
        onComplete?.Invoke();
    }

    public void Hide()
    {
        CancelPending();
        _renderedSignature = null;
        if (_root != null)
            _root.SetActive(false);
    }

    public void Dispose()
    {
        CancelPending();
        _renderedSignature = null;
        DisposeRuntimeObjects();
    }

    private bool EnsureInitialized()
    {
        if (HistoryPanelCardPreviewReflection.SetUpMethod == null)
            return false;

        if (
            _root != null
            && _canvas != null
            && _camera != null
            && _texture != null
            && _sockets != null
            && _pool != null
        )
            return _pool.TryEnsurePrefabRefs();

        DisposeRuntimeObjects();

        _pool = new HistoryPanelPreviewCardPool(_layer);
        if (!_pool.TryEnsurePrefabRefs())
        {
            DisposeRuntimeObjects();
            return false;
        }

        _root = new GameObject("HistoryPanelPreviewRoot");
        _root.layer = _layer;
        _root.SetActive(false);
        var rootTransform = _root.transform;

        var cameraObject = new GameObject("HistoryPanelPreviewCamera");
        cameraObject.layer = _layer;
        cameraObject.transform.SetParent(rootTransform, worldPositionStays: false);
        _camera = cameraObject.AddComponent<Camera>();
        _camera.enabled = true; // live: renders every frame while the root is active
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent letterbox
        _camera.cullingMask = 1 << _layer;
        _camera.orthographic = true;
        _camera.nearClipPlane = 0.1f;
        _camera.farClipPlane = 200f;
        _camera.allowMSAA = true;
        _camera.allowHDR = false;
        _camera.transform.localPosition = Vector3.zero;
        _camera.transform.localRotation = Quaternion.identity;

        var canvasObject = new GameObject(
            "HistoryPanelPreviewCanvas",
            typeof(RectTransform),
            typeof(Canvas)
        );
        canvasObject.layer = _layer;
        canvasObject.transform.SetParent(rootTransform, worldPositionStays: false);
        _canvas = canvasObject.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceCamera;
        _canvas.worldCamera = _camera;
        _canvas.planeDistance = CanvasPlaneDistance;
        _canvas.sortingLayerID = 0;
        _canvas.sortingOrder = 0;

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        _sockets = HistoryPanelPreviewLayout.BuildSockets(canvasRect, _layer);

        CreateRenderTexture();
        return true;
    }

    private void CreateRenderTexture()
    {
        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        _texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 2,
            useMipMap = false,
            autoGenerateMips = false,
            name = "HistoryPanelPreviewRT",
        };
        _texture.Create();

        if (_camera != null)
        {
            _camera.targetTexture = _texture;
            // ortho size = half-height in world units = (texture-pixels / 2) / referencePixelsPerUnit.
            _camera.orthographicSize = TextureHeight * 0.5f / CanvasReferencePixelsPerUnit;
            _camera.aspect = (float)TextureWidth / Mathf.Max(1, TextureHeight);
        }
    }

    private int SpawnCards(IReadOnlyList<HistoryItemSpec> items)
    {
        if (_pool == null || _sockets == null)
            return 0;

        var staticData = BppStaticDataAccess.TryGet();
        if (staticData == null)
            return 0;

        var i = 0;
        foreach (var spec in items)
        {
            if (spec == null || spec.TemplateId == Guid.Empty)
                continue;

            var template = HistoryPanelPreviewTemplateLookup.GetCardTemplate(
                staticData,
                spec.TemplateId
            );
            if (template == null)
                continue;

            var size = ResolveCardSize(template);
            var socket = ResolveSocket(spec.SocketId, i, size);
            if (socket == null)
                continue;

            var card = _pool.Take(size, socket);
            if (card == null)
                continue;

            var instance = BuildSyntheticInstance(spec, i);
            _activeSetUpTasks.Add(InvokeSetUpSafe(card, template, instance));
            _active.Add(card);
            i++;
        }

        return i;
    }

    private RectTransform? ResolveSocket(
        EContainerSocketId? requested,
        int fallbackIndex,
        ECardSize size
    )
    {
        if (_sockets == null || _sockets.Length == 0)
            return null;

        var span = size switch
        {
            ECardSize.Small => 1,
            ECardSize.Medium => 2,
            ECardSize.Large => 3,
            _ => 1,
        };
        var lastValidStart = _sockets.Length - span;
        if (lastValidStart < 0)
            return null;

        int index;
        if (requested.HasValue)
            index = (int)requested.Value;
        else
            index = fallbackIndex;

        index = Mathf.Clamp(index, 0, lastValidStart);
        return _sockets[index];
    }

    private static ECardSize ResolveCardSize(TCardBase template)
    {
        return template.Size switch
        {
            ECardSize.Small => ECardSize.Small,
            ECardSize.Medium => ECardSize.Medium,
            ECardSize.Large => ECardSize.Large,
            _ => ECardSize.Small,
        };
    }

    private static TCardInstanceItem BuildSyntheticInstance(HistoryItemSpec spec, int index)
    {
        return new TCardInstanceItem
        {
            TemplateId = spec.TemplateId,
            TemplateVersion = string.Empty,
            InstanceId = $"bpp-battleboard-{index}",
            Tier = spec.Tier,
            SocketId = spec.SocketId ?? (EContainerSocketId)Mathf.Clamp(index, 0, 9),
            EnchantmentType = spec.EnchantmentType,
            Attributes =
                spec.Attributes != null
                    ? new Dictionary<ECardAttributeType, int>(spec.Attributes)
                    : new Dictionary<ECardAttributeType, int>(),
        };
    }

    private static async Task InvokeSetUpSafe(
        Component card,
        TCardBase template,
        TCardInstanceItem instance
    )
    {
        var method = HistoryPanelCardPreviewReflection.SetUpMethod;
        if (method == null)
            return;

        try
        {
            var raw = method.Invoke(card, new object[] { template, false, instance });
            if (raw is Task task)
                await task;
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "BattleBoardPreview",
                $"CardPreviewBase.SetUp threw for template={template?.Id}: {ex.InnerException?.Message ?? ex.Message}"
            );
            throw;
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "BattleBoardPreview",
                $"CardPreviewBase.SetUp invocation failed for template={template?.Id}: {ex.Message}"
            );
            throw;
        }
    }

    private void ShowAllActiveCards()
    {
        var show = HistoryPanelCardPreviewReflection.ShowMethod;
        if (show == null)
            return;

        var args = new object[] { true };
        foreach (var card in _active)
        {
            if (card == null)
                continue;
            try
            {
                show.Invoke(card, args);
            }
            catch (Exception ex)
            {
                BppLog.Warn("BattleBoardPreview", $"CardPreviewBase.Show threw: {ex.Message}");
            }
        }
    }

    private void ReturnActiveCardsToPool()
    {
        if (_pool == null)
        {
            _active.Clear();
            return;
        }

        foreach (var card in _active)
            _pool.Return(card);

        _active.Clear();
    }

    private void DisposeRuntimeObjects()
    {
        _active.Clear();
        _activeSetUpTasks.Clear();

        _pool?.DestroyAll();
        _pool = null;
        _sockets = null;

        if (_texture != null)
        {
            _texture.Release();
            Object.Destroy(_texture);
            _texture = null;
        }

        if (_root != null)
        {
            Object.Destroy(_root);
            _root = null;
            _canvas = null;
            _camera = null;
        }
    }
}

internal static class HistoryPanelPreviewTemplateLookup
{
    private static MethodInfo? _getCardByIdMethod;
    private static Type? _lastStaticDataType;

    public static TCardBase? GetCardTemplate(object? staticData, Guid templateId)
    {
        if (staticData == null || templateId == Guid.Empty)
            return null;

        var staticType = staticData.GetType();
        if (!ReferenceEquals(_lastStaticDataType, staticType))
        {
            _lastStaticDataType = staticType;
            _getCardByIdMethod = staticType.GetMethod(
                "GetCardById",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(Guid) },
                null
            );
        }

        return _getCardByIdMethod?.Invoke(staticData, new object[] { templateId }) as TCardBase;
    }
}
```

- [ ] **Step 2: Do NOT build yet** — the project will not compile until Task 2 + Task 3 land (tuner file still references the now-removed `SetClipMaskEnabled`, and `HistoryPanel.cs` still references tuner fields). Proceed to Task 2.

---

### Task 2: Rewire HistoryPanel to push the RT and drop the overlay/tuner plumbing

**Files:**
- Modify: `Game/HistoryPanel/HistoryPanel.cs`
- Modify: `Game/HistoryPanel/HistoryPanel.UiToolkit.cs`

- [ ] **Step 1: `HistoryPanel.cs` — simplify `EnsurePreviewRenderer`**

Replace the current body:

```csharp
    private void EnsurePreviewRenderer()
    {
        _battleBoardPreview ??= new BattleBoardPreview();
        if (_hasPreviewContainerBounds)
            ApplyPreviewContainerBounds(_previewContainerBounds);
    }
```

with:

```csharp
    private void EnsurePreviewRenderer()
    {
        _battleBoardPreview ??= new BattleBoardPreview();
    }
```

- [ ] **Step 2: `HistoryPanel.cs` — delete `ApplyPreviewContainerBounds` and `ComputePreviewPanelScale` entirely**

Delete the whole `ApplyPreviewContainerBounds(Rect bounds)` method and the `ComputePreviewPanelScale()` helper (the block added in the tuner work, including their doc comments). Nothing else may reference them after Step 4 below.

- [ ] **Step 3: `HistoryPanel.cs` — push the texture in `OnPreviewPhase`**

Replace the `OnPreviewPhase` method with:

```csharp
    private void OnPreviewPhase(BattleBoardRenderPhase phase)
    {
        switch (phase)
        {
            case BattleBoardRenderPhase.Empty:
                SetPreviewTexture(null);
                SetPreviewStatus(HistoryPanelText.NoLocallyRenderableCards(), true);
                break;
            case BattleBoardRenderPhase.InitFailed:
                SetPreviewTexture(null);
                SetPreviewStatus(HistoryPanelText.PreviewRendererInitFailed(), true);
                break;
            case BattleBoardRenderPhase.Loading:
                SetPreviewTexture(_battleBoardPreview?.OutputTexture);
                SetPreviewStatus(HistoryPanelText.LoadingPreview(), true);
                break;
            case BattleBoardRenderPhase.Done:
                SetPreviewTexture(_battleBoardPreview?.OutputTexture);
                SetPreviewStatus(null, false);
                break;
        }
    }
```

- [ ] **Step 4: `HistoryPanel.cs` — clear the texture in `DisposePreviewRenderer`**

Replace:

```csharp
    private void DisposePreviewRenderer()
    {
        StopPreviewRender();
        _battleBoardPreview?.Dispose();
        _battleBoardPreview = null;
    }
```

with:

```csharp
    private void DisposePreviewRenderer()
    {
        StopPreviewRender();
        SetPreviewTexture(null);
        _battleBoardPreview?.Dispose();
        _battleBoardPreview = null;
    }
```

- [ ] **Step 5: `HistoryPanel.UiToolkit.cs` — add a `SetPreviewTexture` wrapper next to `SetPreviewStatus`**

After the existing `SetPreviewStatus` wrapper add:

```csharp
    private void SetPreviewTexture(UnityEngine.Texture? texture)
    {
        _uiView?.SetPreviewTexture(texture);
    }
```

- [ ] **Step 6: `HistoryPanel.UiToolkit.cs` — stop calling the deleted method from `OnPreviewContainerBoundsChanged`**

Replace:

```csharp
    private void OnPreviewContainerBoundsChanged(Rect bounds)
    {
        _previewContainerBounds = bounds;
        _hasPreviewContainerBounds = true;

        if (_battleBoardPreview == null)
            return;

        if (ApplyPreviewContainerBounds(bounds) && IsVisible)
            RefreshSelectedBattlePreview();
    }
```

with (fixed RT no longer needs container bounds; leave the fields dormant to avoid view-side surgery):

```csharp
    private void OnPreviewContainerBoundsChanged(Rect bounds)
    {
        // Fixed-aspect RenderTexture: UI Toolkit's Image (ScaleToFit) handles container
        // resizing, so the preview no longer needs the container's screen rect. Kept as a
        // no-op subscriber so the view's GeometryChangedEvent wiring stays intact.
    }
```

- [ ] **Step 7: Do NOT build yet** — the tuner file (`HistoryPanel.PreviewTunerDebug.cs`) still references `SetClipMaskEnabled` / `ApplyPreviewContainerBounds` / `ComputePreviewPanelScale` and its own `_debug*` fields. Delete it in Task 3, then build.

---

### Task 3: Delete the temporary tuner

**Files:**
- Delete: `Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs`

- [ ] **Step 1: Delete the file**

Run: `git rm Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs`

- [ ] **Step 2: Grep for any dangling references**

Run (PowerShell-safe, via the Grep tool): search the repo for `_debugX`, `_debugTuner`, `ApplyPreviewContainerBounds`, `ComputePreviewPanelScale`, `SetClipMaskEnabled`, `_debugClipMask`.
Expected: zero matches outside the just-deleted file / this plan / the spec doc.

---

### Task 4: Build clean, run preview tests, commit

**Files:** none (verification + commit)

- [ ] **Step 1: Build the mod (Debug)**

Run: `dotnet build BazaarPlusPlus.csproj -c Debug -nologo -v minimal`
Expected: `0 个警告 0 个错误` for compilation. The post-build copy to `BepInEx/plugins` MAY fail with `MSB3061 ... 文件被"TheBazaar.exe"锁定` if the game is running — that is a copy-step failure, not a compile error, and is acceptable (user restarts the game to test). If compilation itself fails, fix and rebuild.

- [ ] **Step 2: Run the existing preview tests (best-effort)**

Run: `dotnet test HistoryPanelPreview.Tests/HistoryPanelPreview.Tests.csproj -nologo -v minimal` (adjust path if the csproj lives elsewhere; locate via Glob `**/HistoryPanelPreview.Tests*.csproj`).
Expected: signature-gate + generation-guard tests PASS (these classes were reused unchanged). If the test project cannot resolve game assemblies in this environment, note it and rely on the mod build as the compile gate.

- [ ] **Step 3: Commit the coherent change**

```bash
git add Game/HistoryPanel/Preview/BattleBoardPreview.cs \
        Game/HistoryPanel/HistoryPanel.cs \
        Game/HistoryPanel/HistoryPanel.UiToolkit.cs \
        docs/superpowers/plans/2026-05-29-history-preview-rendertexture.md
git rm Game/HistoryPanel/HistoryPanel.PreviewTunerDebug.cs
git commit -m "Render History preview via offscreen Camera + RenderTexture in UI Toolkit"
```

(Commit message body should note: replaces the overlay ScreenSpaceOverlay canvas + manual worldBound sync with a fixed 2400×600 RT shown in `_previewImage` (ScaleToFit); removes the temporary tuner and panelScale plumbing.)

---

## Manual Verification (user — requires running the game)

The agent cannot see in-game pixels (the build copy is blocked while the game runs). After the user restarts the game with the new DLL:

1. Open History Panel (F8), select a battle → **a card / board should appear in the preview area** (this is the de-risk check: confirms CardPreviewBase renders into the offscreen RT; if blank/black, that is the historical RT failure mode → report back).
2. Switch resolution (e.g., 1920×1080 → 3440×1440 → windowed, resized) → the preview should **stay centered and correctly scaled with no manual tuning and no misplacement**.
3. Non-16:9 aspect → board letterboxes inside the container with the container background showing in the margins (no stretch, no black bars).

## Risk

- **Historical (highest):** `ce20923` dropped Camera+RT for unknown reasons; the likely cause is CardPreviewBase not rendering cleanly into an offscreen camera/RT. The plan front-loads this as the very first manual check (step 1 above). If the preview is blank, debug camera/canvas/material/layer before any further polish.
- Live camera cost is negligible (one small ortho camera, only while the panel root is active).
