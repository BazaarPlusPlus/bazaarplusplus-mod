#pragma warning disable CS0436
#nullable enable
using System;
using System.Reflection;
using HarmonyLib;
using TheBazaar.Feature.Chest.Scene;
using TheBazaar.Feature.Chest.Scene.States;
using TheBazaar.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace BazaarPlusPlus;

[HarmonyPatch(typeof(StateSelect), nameof(StateSelect.Enter))]
internal static class ChestUiDiagnosticsPatch
{
    private static readonly FieldInfo? StateMachineField = AccessTools.Field(
        typeof(ChestSceneState),
        "_stateMachine"
    );
    private static readonly FieldInfo? EventSystemField = AccessTools.Field(
        typeof(CollectionUIController),
        "eventSystem"
    );
    private static readonly FieldInfo? FadeOverlayField = AccessTools.Field(
        typeof(CollectionUIController),
        "fadeOverlay"
    );
    private static readonly FieldInfo? LeftAnchorField = AccessTools.Field(
        typeof(CollectionUIController),
        "leftAnchor"
    );
    private static readonly FieldInfo? RightAnchorField = AccessTools.Field(
        typeof(CollectionUIController),
        "rightAnchor"
    );
    private static readonly FieldInfo? BackButtonRectField = AccessTools.Field(
        typeof(CollectionUIController),
        "backButtonRect"
    );
    private static readonly FieldInfo? ActiveChestControllerField = AccessTools.Field(
        typeof(CollectionUIController),
        "activeChestController"
    );

    [HarmonyPostfix]
    private static void Postfix(StateSelect __instance)
    {
        try
        {
            var stateMachine = StateMachineField?.GetValue(__instance) as ChestSceneController;
            if (stateMachine == null)
            {
                BppLog.Warn(
                    "ChestUiDiagnostics",
                    "[Diag][ChestSelect] StateSelect.Enter could not resolve ChestSceneController."
                );
                return;
            }

            var collectionUiController = stateMachine.collectionUIController;
            if (collectionUiController == null)
            {
                BppLog.Warn(
                    "ChestUiDiagnostics",
                    $"[Diag][ChestSelect] scene='{GetSceneToken()}' stateMachine={DescribeUnityObject(stateMachine)} collectionUIController=<null>"
                );
                return;
            }

            var eventSystem = EventSystemField?.GetValue(collectionUiController) as EventSystem;
            var fadeOverlay = FadeOverlayField?.GetValue(collectionUiController) as Image;
            var leftAnchor = LeftAnchorField?.GetValue(collectionUiController) as RectTransform;
            var rightAnchor = RightAnchorField?.GetValue(collectionUiController) as RectTransform;
            var backButtonRect = BackButtonRectField?.GetValue(collectionUiController) as RectTransform;
            var activeChestController =
                ActiveChestControllerField?.GetValue(collectionUiController) as ChestSceneController;

            BppLog.Info(
                "ChestUiDiagnostics",
                $"[Diag][ChestSelect] scene='{GetSceneToken()}' stateMachine={DescribeUnityObject(stateMachine)} collectionUI={DescribeUnityObject(collectionUiController)} selectedSeasonNull={stateMachine.playerChestInventory?.selectedSeasonInventory == null} canShowDetailsPanel={stateMachine.canShowDetailsPanel} eventSystem={DescribeEventSystem(eventSystem)} fadeOverlay={DescribeGraphic(fadeOverlay)} leftAnchor={DescribeRectTransform(leftAnchor)} rightAnchor={DescribeRectTransform(rightAnchor)} backButtonRect={DescribeRectTransform(backButtonRect)} collectionsController={DescribeUnityObject(collectionUiController.collectionsController)} activeChestController={DescribeUnityObject(activeChestController)}"
            );
        }
        catch (Exception ex)
        {
            BppLog.Error("ChestUiDiagnostics", "[Diag][ChestSelect] Logging failed", ex);
        }
    }

    private static string GetSceneToken()
    {
        var scene = SceneManager.GetActiveScene();
        return $"{scene.name}|{scene.path}|{scene.buildIndex}|{scene.isLoaded}";
    }

    private static string DescribeEventSystem(EventSystem? eventSystem)
    {
        if (eventSystem == null)
            return "<null>";

        var modules = eventSystem.GetComponents<BaseInputModule>();
        var moduleSummary = modules.Length == 0
            ? string.Empty
            : string.Join(
                ", ",
                Array.ConvertAll(
                    modules,
                    module =>
                        $"{module.GetType().Name}(enabled={module.enabled},active={module.isActiveAndEnabled})"
                )
            );

        return
            $"{DescribeUnityObject(eventSystem)} enabled={eventSystem.enabled} isCurrent={ReferenceEquals(EventSystem.current, eventSystem)} modules=[{moduleSummary}]";
    }

    private static string DescribeGraphic(Graphic? graphic)
    {
        if (graphic == null)
            return "<null>";

        return
            $"{DescribeUnityObject(graphic)} enabled={graphic.enabled} colorA={graphic.color.a:0.###}";
    }

    private static string DescribeRectTransform(RectTransform? rectTransform)
    {
        if (rectTransform == null)
            return "<null>";

        return
            $"{DescribeUnityObject(rectTransform)} anchoredPosition={rectTransform.anchoredPosition} sizeDelta={rectTransform.sizeDelta}";
    }

    private static string DescribeUnityObject(UnityEngine.Object? obj)
    {
        if (obj == null)
            return "<null>";

        if (obj is Component component)
        {
            return
                $"{component.GetType().Name}(name='{component.name}',activeSelf={component.gameObject.activeSelf},activeInHierarchy={component.gameObject.activeInHierarchy},scene='{component.gameObject.scene.name}')";
        }

        if (obj is GameObject gameObject)
        {
            return
                $"{gameObject.GetType().Name}(name='{gameObject.name}',activeSelf={gameObject.activeSelf},activeInHierarchy={gameObject.activeInHierarchy},scene='{gameObject.scene.name}')";
        }

        return $"{obj.GetType().Name}(name='{obj.name}')";
    }
}
