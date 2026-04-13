#nullable enable
using TheBazaar.UI.EndOfRun;
using UnityEngine;
using UnityEngine.UI;

namespace BazaarPlusPlus.Game.Screenshots;

internal sealed class EndOfRunMouseBlocker
{
    private const string BlockerObjectName = "BPP_EndOfRunMouseBlocker";
    private EndOfRunScreenController? _owner;
    private GameObject? _blockerObject;

    public void Attach(EndOfRunScreenController screenController)
    {
        if (_owner != null && !ReferenceEquals(_owner, screenController))
            Detach();

        if (_blockerObject == null)
            CreateBlocker(screenController);

        _owner = screenController;
        _blockerObject?.SetActive(true);
        _blockerObject?.transform.SetAsLastSibling();
    }

    public void Detach()
    {
        if (_blockerObject != null)
            Object.Destroy(_blockerObject);

        _blockerObject = null;
        _owner = null;
    }

    private void CreateBlocker(EndOfRunScreenController screenController)
    {
        var hostRect = ResolveHostRect(screenController);
        if (hostRect == null)
            return;

        _blockerObject = new GameObject(
            BlockerObjectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );

        var blockerTransform = _blockerObject.GetComponent<RectTransform>();
        blockerTransform.SetParent(hostRect, worldPositionStays: false);
        blockerTransform.anchorMin = Vector2.zero;
        blockerTransform.anchorMax = Vector2.one;
        blockerTransform.offsetMin = Vector2.zero;
        blockerTransform.offsetMax = Vector2.zero;
        blockerTransform.localScale = Vector3.one;
        blockerTransform.SetAsLastSibling();

        var image = _blockerObject.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0f);
        image.raycastTarget = true;
    }

    private static RectTransform? ResolveHostRect(EndOfRunScreenController screenController)
    {
        if (screenController.transform is RectTransform screenRect)
            return screenRect;

        var parentCanvas = screenController.GetComponentInParent<Canvas>();
        if (parentCanvas?.transform is RectTransform canvasRect)
            return canvasRect;

        BppLog.Warn(
            "EndOfRunScreenshot",
            "Failed to resolve a RectTransform host for the end-of-run mouse blocker."
        );
        return null;
    }
}
