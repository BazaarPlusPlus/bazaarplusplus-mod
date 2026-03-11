using System;
using TheBazaar;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BazaarPlusPlus;

internal sealed class CombatStatusBar : MonoBehaviour
{
    private static readonly GUIStyle HeaderStyle = new GUIStyle();
    private static readonly GUIStyle LabelStyle = new GUIStyle();
    private static readonly GUIStyle ButtonStyle = new GUIStyle();
    private static bool _stylesInitialized;

    private bool _visible = true;

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
        if (keyboard == null)
            return;

        if (keyboard.f6Key.wasPressedThisFrame)
            _visible = !_visible;
    }

    private void OnGUI()
    {
        if (!ShouldDraw())
            return;

        InitStyles();

        const float width = 620f;
        const float height = 56f;
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 14f, width, height);

        GUI.Box(rect, string.Empty);
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 12f));
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(GUILayout.Width(240f));
        GUILayout.Label("Combat Status  [F6 toggle]", HeaderStyle);
        GUILayout.Label(
            $"Sim: {FormatElapsed(ModState.GetCombatLogicalElapsed())}    Frame: {FormatFrameProgress()}    Speed: {ModState.CombatSpeedMultiplier:F2}x",
            LabelStyle
        );
        GUILayout.EndVertical();

        GUILayout.FlexibleSpace();

        GUILayout.Space(12f);
        GUILayout.Label("Preset", LabelStyle, GUILayout.Width(42f));

        foreach (var preset in ModState.CombatSpeedSteps)
        {
            if (GUILayout.Button($"{preset:0.##}x", ButtonStyle, GUILayout.Width(50f), GUILayout.Height(28f)))
                ModState.SetCombatSpeed(preset);
        }

        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss\.ff")
            : elapsed.ToString(@"mm\:ss\.ff");
    }

    private static string FormatFrameProgress()
    {
        return ModState.TotalCombatFrames > 0
            ? $"{ModState.ProcessedCombatFrames}/{ModState.TotalCombatFrames}"
            : ModState.ProcessedCombatFrames.ToString();
    }

    private static void InitStyles()
    {
        if (_stylesInitialized)
            return;

        HeaderStyle.normal.textColor = new Color(1f, 0.88f, 0.5f);
        HeaderStyle.fontStyle = FontStyle.Bold;
        HeaderStyle.fontSize = 13;

        LabelStyle.normal.textColor = Color.white;
        LabelStyle.fontSize = 12;
        LabelStyle.alignment = TextAnchor.MiddleLeft;

        ButtonStyle.normal.textColor = Color.white;
        ButtonStyle.fontSize = 12;
        ButtonStyle.alignment = TextAnchor.MiddleCenter;

        _stylesInitialized = true;
    }

    private bool ShouldDraw()
    {
        if (!_visible)
            return false;

        if (!ModState.EnableCombatStatusBarConfig.Value)
            return false;

        return ModState.CombatPlaybackActive;
    }

    private static void OnCombatStarted()
    {
        ModState.BeginCombatPlayback();
    }

    private static void OnCombatEnded()
    {
        ModState.EndCombatPlayback();
    }
}
