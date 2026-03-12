using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed partial class CombatStatusBar : MonoBehaviour
{
    private static readonly GUIStyle SegmentBoxStyle = new GUIStyle();
    private static readonly GUIStyle LabelStyle = new GUIStyle();
    private static readonly GUIStyle ValueStyle = new GUIStyle();
    private static readonly GUIStyle StepButtonStyle = new GUIStyle();
    private static readonly GUIStyle PauseButtonStyle = new GUIStyle();
    private static bool _stylesInitialized;

    private bool _visible = true;
    private float _visualBlend;

    private void OnEnable()
    {
        Events.CombatStarted.AddListener(OnCombatStarted, this);
        Events.CombatEnded.AddListener(OnCombatEnded, this);
    }

    private void OnDisable()
    {
        Events.CombatStarted.RemoveListener(OnCombatStarted);
        Events.CombatEnded.RemoveListener(OnCombatEnded);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f6Key.wasPressedThisFrame)
            _visible = !_visible;

        _visualBlend = AdvanceVisualBlend(_visualBlend, IsCombatPlaybackActive, Time.unscaledDeltaTime);
    }

    private void OnGUI()
    {
        if (!ShouldDraw())
            return;

        InitStyles();

        const float height = 72f;
        const float timeWidth = 128f;
        const float frameWidth = 128f;
        const float multiplierWidth = 188f;
        const float pauseWidth = 88f;
        const float bottomMargin = 16f;
        var width = timeWidth + frameWidth + multiplierWidth + pauseWidth;
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - bottomMargin, width, height);

        DrawController(rect, timeWidth, frameWidth, multiplierWidth, pauseWidth, _visualBlend);
    }

    private static void InitStyles()
    {
        if (_stylesInitialized)
            return;

        SegmentBoxStyle.normal.background = Texture2D.whiteTexture;
        SegmentBoxStyle.normal.textColor = Color.white;
        SegmentBoxStyle.border = new RectOffset(1, 1, 1, 1);
        SegmentBoxStyle.alignment = TextAnchor.MiddleCenter;

        LabelStyle.normal.textColor = new Color(0.87f, 0.82f, 0.67f);
        LabelStyle.fontSize = 11;
        LabelStyle.alignment = TextAnchor.UpperCenter;

        ValueStyle.normal.textColor = Color.white;
        ValueStyle.fontSize = 17;
        ValueStyle.fontStyle = FontStyle.Bold;
        ValueStyle.alignment = TextAnchor.MiddleCenter;

        StepButtonStyle.fontSize = 16;
        StepButtonStyle.fontStyle = FontStyle.Bold;
        StepButtonStyle.alignment = TextAnchor.MiddleCenter;

        PauseButtonStyle.fontSize = 18;
        PauseButtonStyle.fontStyle = FontStyle.Bold;
        PauseButtonStyle.alignment = TextAnchor.MiddleCenter;

        _stylesInitialized = true;
    }

    private bool ShouldDraw()
    {
        return ShouldRenderForState(_visible, IsEnabled());
    }

    private static void OnCombatStarted()
    {
        BeginCombatPlayback();
    }

    private static void OnCombatEnded()
    {
        EndCombatPlayback();
    }

    private static void DrawController(Rect rect, float timeWidth, float frameWidth, float multiplierWidth, float pauseWidth, float visualBlend)
    {
        var contentColor = GUI.color;
        GUI.color = Color.Lerp(
            new Color(0.06f, 0.07f, 0.09f, 0.84f),
            new Color(0.16f, 0.12f, 0.08f, 0.96f),
            visualBlend
        );
        GUI.Box(rect, string.Empty, SegmentBoxStyle);
        GUI.color = contentColor;

        var timeRect = new Rect(rect.x, rect.y, timeWidth, rect.height);
        var frameRect = new Rect(timeRect.xMax, rect.y, frameWidth, rect.height);
        var multiplierRect = new Rect(frameRect.xMax, rect.y, multiplierWidth, rect.height);
        var pauseRect = new Rect(multiplierRect.xMax, rect.y, pauseWidth, rect.height);

        DrawReadoutSegment(timeRect, "Time", GetDisplayedTimeText(), visualBlend);
        DrawReadoutSegment(frameRect, "Frame", GetDisplayedFrameText(), visualBlend);
        DrawMultiplierSegment(multiplierRect, visualBlend);
        DrawPauseSegment(pauseRect, visualBlend);

        DrawSeparator(frameRect.x, rect, visualBlend);
        DrawSeparator(multiplierRect.x, rect, visualBlend);
        DrawSeparator(pauseRect.x, rect, visualBlend);
    }

    private static void DrawReadoutSegment(Rect rect, string label, string value, float visualBlend)
    {
        ApplySegmentTextColors(visualBlend);
        GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 16f), label, LabelStyle);
        GUI.Label(new Rect(rect.x + 8f, rect.y + 28f, rect.width - 16f, 28f), value, ValueStyle);
    }

    private static void DrawMultiplierSegment(Rect rect, float visualBlend)
    {
        ApplySegmentTextColors(visualBlend);
        GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 16f), "Multiplier", LabelStyle);

        var buttonWidth = 38f;
        var buttonHeight = 34f;
        var buttonY = rect.y + 28f;
        var leftButtonRect = new Rect(rect.x + 10f, buttonY, buttonWidth, buttonHeight);
        var rightButtonRect = new Rect(rect.xMax - buttonWidth - 10f, buttonY, buttonWidth, buttonHeight);
        var valueRect = new Rect(leftButtonRect.xMax + 8f, rect.y + 26f, rect.width - (buttonWidth * 2f) - 36f, 36f);

        var previousEnabled = CanStepCombatSpeed(-1);
        var nextEnabled = CanStepCombatSpeed(1);

        var previousColor = GUI.color;
        GUI.color = Color.Lerp(new Color(0.58f, 0.60f, 0.64f, 0.75f), new Color(0.90f, 0.72f, 0.33f, 0.95f), visualBlend);
        GUI.enabled = previousEnabled;
        if (GUI.Button(leftButtonRect, "<", StepButtonStyle))
            StepCombatSpeed(-1);

        GUI.enabled = true;
        GUI.Label(valueRect, FormatCombatSpeedLabel(), ValueStyle);

        GUI.enabled = nextEnabled;
        if (GUI.Button(rightButtonRect, ">", StepButtonStyle))
            StepCombatSpeed(1);

        GUI.enabled = true;
        GUI.color = previousColor;
    }

    private static void DrawPauseSegment(Rect rect, float visualBlend)
    {
        ApplySegmentTextColors(visualBlend);
        GUI.Label(new Rect(rect.x, rect.y + 10f, rect.width, 16f), "Pause", LabelStyle);
        var previousColor = GUI.color;
        GUI.color = Color.Lerp(new Color(0.38f, 0.40f, 0.45f, 0.55f), new Color(0.52f, 0.44f, 0.30f, 0.70f), visualBlend);
        GUI.enabled = false;
        GUI.Button(new Rect(rect.x + 18f, rect.y + 26f, rect.width - 36f, 36f), "||", PauseButtonStyle);
        GUI.enabled = true;
        GUI.color = previousColor;
    }

    private static void DrawSeparator(float x, Rect containerRect, float visualBlend)
    {
        var previousColor = GUI.color;
        GUI.color = Color.Lerp(
            new Color(0.42f, 0.46f, 0.53f, 0.22f),
            new Color(0.84f, 0.74f, 0.44f, 0.42f),
            visualBlend
        );
        GUI.DrawTexture(new Rect(x, containerRect.y + 8f, 1f, containerRect.height - 16f), Texture2D.whiteTexture);
        GUI.color = previousColor;
    }

    private static void ApplySegmentTextColors(float visualBlend)
    {
        LabelStyle.normal.textColor = Color.Lerp(
            new Color(0.62f, 0.67f, 0.74f, 0.86f),
            new Color(0.90f, 0.84f, 0.62f, 0.96f),
            visualBlend
        );
        ValueStyle.normal.textColor = Color.Lerp(
            new Color(0.80f, 0.84f, 0.90f, 0.90f),
            new Color(1f, 0.96f, 0.90f, 1f),
            visualBlend
        );
    }
}
