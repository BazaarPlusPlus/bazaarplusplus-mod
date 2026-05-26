#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages.GameSimEvents;
using BazaarGameShared.TempoNet.Models;
using TheBazaar;
using TheBazaar.AppFramework;
using TheBazaar.Assets.Scripts.ScriptableObjectsScripts;
using TheBazaar.UI.Components;
using TheBazaar.UI.EncounterPicker;
using UnityEngine;

namespace BazaarPlusPlus.Game.CombatReplay;

// "Initialized" board UI controllers are tracked across replay sessions to avoid double-binding the
// game's BoardUIController.Init, which throws on repeat. Owner clears this when sessions start/end.
internal static class ReplayHealthBarRebuilder
{
    public static readonly HashSet<int> InitializedBoardUiControllers = new();

    public static EncounterController? ActiveOpponentPortrait { get; set; }

    public static void HideEncounterPickerOverlays()
    {
        HideObjectsOfType<EncounterPickerMapController>();
        HideObjectsOfType<InjectedEncounterPickerMapController>();

        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (
                transform?.gameObject != null
                && string.Equals(
                    transform.gameObject.name,
                    "EncounterPicker_Map(Clone)",
                    StringComparison.Ordinal
                )
            )
            {
                transform.gameObject.SetActive(false);
            }
        }
    }

    public static void EnsureOpponentPortraitVisible()
    {
        var replayPortrait = ActiveOpponentPortrait;
        if (replayPortrait != null)
        {
            if (Data.CurrentEncounterController != null)
                Data.CurrentEncounterController.ShowCard(show: false);

            replayPortrait.gameObject.SetActive(true);
            replayPortrait.ShowCard(show: true);
            return;
        }

        var encounterController = Data.CurrentEncounterController;
        if (encounterController?.gameObject == null)
            return;

        encounterController.gameObject.SetActive(true);
        encounterController.ShowCard(show: true);
    }

    public static async Task PrepareHealthBarsAsync()
    {
        var bindings = await RefreshHealthBarBindingsAsync();
        ShowPlayerHealthBar(bindings.PlayerController);
        Data.PlayerExperienceBar?.ToggleExperienceBarAndText(isVisible: false);
        Events.TryShowEmptyOpponentHealthBar.Trigger();
    }

    public static void RefillOpponentHealthBar()
    {
        Events.TryRefillOpponentHealthBar.Trigger();
    }

    public static void EnsureSequencePlayerAttributes(CombatSequenceMessages sequence)
    {
        EnsurePlayerAttributes(sequence.SpawnMessage?.Data?.Player, ECombatantId.Player);
        EnsurePlayerAttributes(sequence.SpawnMessage?.Data?.Opponent, ECombatantId.Opponent);
        EnsurePlayerAttributes(sequence.DespawnMessage?.Data?.Player, ECombatantId.Player);
        EnsurePlayerAttributes(sequence.DespawnMessage?.Data?.Opponent, ECombatantId.Opponent);
    }

    public static void EnsureRunPlayerAttributes()
    {
        EnsurePlayerAttributes(Data.Run?.Player, ECombatantId.Player);
        EnsurePlayerAttributes(Data.Run?.Opponent, ECombatantId.Opponent);
    }

    private static async Task<ReplayBoardUiBindings> RefreshHealthBarBindingsAsync()
    {
        var bindings = ResolveBoardUiControllers();

        if (bindings.PlayerController != null)
            BindBoardUiController(bindings.PlayerController, registerPlayerHealthBar: true);

        if (bindings.OpponentController != null)
            BindBoardUiController(
                bindings.OpponentController,
                registerPlayerHealthBar: false
            );

        await Task.Delay(150);
        return bindings;
    }

    private static IEnumerable<BoardUIController> GetSceneBoardUiControllers()
    {
        return UnityEngine
            .Object.FindObjectsOfType<BoardUIController>(true)
            .Where(controller => controller != null && controller.gameObject.scene.rootCount > 0);
    }

    private static ReplayBoardUiBindings ResolveBoardUiControllers()
    {
        var controllers = GetSceneBoardUiControllers().ToList();
        return new ReplayBoardUiBindings(
            SelectBoardUiController(controllers, ECombatantId.Player, AnchorSide.Player),
            SelectBoardUiController(controllers, ECombatantId.Opponent, AnchorSide.Opponent)
        );
    }

    private static BoardUIController? SelectBoardUiController(
        IEnumerable<BoardUIController> controllers,
        ECombatantId combatantId,
        AnchorSide anchorSide
    )
    {
        var anchor = Singleton<BoardManager>.Instance?.GetAnchor(anchorSide, AnchorType.Portrait);
        return controllers
            .Where(controller => controller.combatantId == combatantId)
            .OrderByDescending(controller => controller.gameObject.activeInHierarchy)
            .ThenByDescending(controller => controller.isActiveAndEnabled)
            .ThenByDescending(HasActiveHealthBar)
            .ThenBy(controller => GetControllerAnchorDistance(controller, anchor))
            .FirstOrDefault();
    }

    private static bool HasActiveHealthBar(BoardUIController controller)
    {
        var healthBar = GetBoardUiHealthBar(controller) as Component;
        return healthBar?.gameObject.activeInHierarchy == true;
    }

    private static float GetControllerAnchorDistance(
        BoardUIController controller,
        Transform? anchor
    )
    {
        if (anchor == null)
            return float.MaxValue;

        return Vector3.SqrMagnitude(controller.transform.position - anchor.position);
    }

    private static void BindBoardUiController(
        BoardUIController controller,
        bool registerPlayerHealthBar
    )
    {
        var player =
            controller.combatantId == ECombatantId.Player ? Data.Run?.Player : Data.Run?.Opponent;
        if (player == null)
            return;

        EnsurePlayerAttributes(player, controller.combatantId);

        if (InitializedBoardUiControllers.Add(controller.GetInstanceID()))
            InvokeBoardUiMethod(controller, "Init", player);

        if (controller.combatantId == ECombatantId.Player)
            UnregisterPlayerPortraitPlacedHandler(controller);

        InvokeBoardUiMethod(controller, "SetBattlePlayer", player);
        ApplyBoardUiDividerConfig(controller);
        InitializeBoardUiHealthBar(controller, player);

        if (registerPlayerHealthBar && controller.combatantId == ECombatantId.Player)
            Data.RegisterPlayerHealthBar(controller);
    }

    private static void ShowPlayerHealthBar(BoardUIController? playerController)
    {
        if (playerController != null)
        {
            if (Data.Run?.Player != null)
                EnsurePlayerAttributes(Data.Run.Player, ECombatantId.Player);

            InvokeBoardUiMethod(playerController, "SetBattlePlayer", Data.Run?.Player);
            InitializeBoardUiHealthBar(playerController, Data.Run?.Player);
            playerController.ShowEmptyPlayerHealthBar();
            RevealBoardUiHealthBar(playerController, showStatusNumbers: true);
            RecalculateHealthBarDividers(playerController, Data.Run?.Player);
            return;
        }

        Data.PlayerHealthBar?.ShowEmptyPlayerHealthBar();
    }

    private static void EnsurePlayerAttributes(object? player, ECombatantId combatantId)
    {
        if (player == null)
            return;

        try
        {
            var attributesProperty = player
                .GetType()
                .GetProperty(
                    "Attributes",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
            if (
                attributesProperty?.GetValue(player)
                is not System.Collections.IDictionary attributes
            )
                return;

            EnsurePlayerAttributeDefaults(attributes);
            EnsureHealthMax(attributes);
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "ReplayHealthBarRebuilder",
                $"Failed to backfill replay player attributes for {combatantId}: {ex.Message}"
            );
        }
    }

    private static void EnsureHealthMax(System.Collections.IDictionary attributes)
    {
        if (
            attributes.Contains(EPlayerAttributeType.HealthMax)
            && Convert.ToInt32(attributes[EPlayerAttributeType.HealthMax]) > 0
        )
            return;

        if (!attributes.Contains(EPlayerAttributeType.Health))
            return;

        var healthValue = Convert.ToInt32(attributes[EPlayerAttributeType.Health]);
        if (healthValue <= 0)
            return;

        attributes[EPlayerAttributeType.HealthMax] = healthValue;
    }

    private static void EnsurePlayerAttributeDefaults(
        System.Collections.IDictionary attributes
    )
    {
        foreach (EPlayerAttributeType attributeType in Enum.GetValues(typeof(EPlayerAttributeType)))
        {
            EnsurePlayerAttribute(
                attributes,
                attributeType,
                attributeType == EPlayerAttributeType.Level ? 1 : 0
            );
        }
    }

    private static void EnsurePlayerAttribute(
        System.Collections.IDictionary attributes,
        EPlayerAttributeType attributeType,
        int defaultValue
    )
    {
        if (!attributes.Contains(attributeType))
            attributes[attributeType] = defaultValue;
    }

    private static void RecalculateHealthBarDividers(BoardUIController controller, object? player)
    {
        if (player == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        ApplyHealthBarMaxValue(healthBar, player);
    }

    private static void UnregisterPlayerPortraitPlacedHandler(BoardUIController controller)
    {
        try
        {
            var handlerMethod = controller
                .GetType()
                .GetMethod(
                    "HandleOnPlayerPortraitPlaced",
                    BindingFlags.Instance | BindingFlags.NonPublic
                );
            if (handlerMethod == null)
                return;

            var handler = Delegate.CreateDelegate(typeof(Action), controller, handlerMethod);
            var eventField = typeof(BoardManager).GetField(
                "_playerPortraitPlaced",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            if (eventField?.GetValue(null) is Action currentDelegate)
            {
                eventField.SetValue(null, (Action)Delegate.Remove(currentDelegate, handler));
            }
        }
        catch (Exception ex)
        {
            BppLog.Warn(
                "ReplayHealthBarRebuilder",
                $"Failed to unregister PlayerPortraitPlaced handler: {ex.Message}"
            );
        }
    }

    private static void InitializeBoardUiHealthBar(BoardUIController controller, object? player)
    {
        if (player == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        var initMethod = healthBar
            .GetType()
            .GetMethod(
                "Init",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (initMethod == null)
            return;

        try
        {
            initMethod.Invoke(healthBar, [player]);
            ApplyHealthBarMaxValue(healthBar, player);
        }
        catch (TargetInvocationException ex)
        {
            BppLog.Warn(
                "ReplayHealthBarRebuilder",
                $"Skipping health bar init for {controller.combatantId}: {ex.InnerException?.Message ?? ex.Message}"
            );
        }
    }

    private static void ApplyBoardUiDividerConfig(BoardUIController controller)
    {
        var healthBarDividerConfigField = controller
            .GetType()
            .GetField(
                "healthBarDividerConfigSO",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        var dividerConfig =
            healthBarDividerConfigField?.GetValue(controller) as HealthBarDividerConfigSO;
        if (dividerConfig == null)
            return;

        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        InvokeOptionalMethod(healthBar, "SetDividerConfig", dividerConfig);
    }

    private static void ApplyHealthBarMaxValue(object healthBar, object player)
    {
        var healthMax = TryGetPlayerAttribute(player, EPlayerAttributeType.HealthMax);
        if (!healthMax.HasValue)
            return;

        var updateMaxHealth = healthBar
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "UpdateMaxHealth", StringComparison.Ordinal))
                    return false;

                var parameters = method.GetParameters();
                return parameters.Length == 3
                    && parameters[0].ParameterType == typeof(uint)
                    && parameters[1].ParameterType == typeof(uint)
                    && parameters[2].ParameterType == typeof(bool);
            });
        if (updateMaxHealth == null)
            return;

        updateMaxHealth.Invoke(healthBar, [healthMax.Value, healthMax.Value, false]);
    }

    private static uint? TryGetPlayerAttribute(object player, EPlayerAttributeType attributeType)
    {
        var attributesProperty = player
            .GetType()
            .GetProperty(
                "Attributes",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            );
        if (attributesProperty?.GetValue(player) is not System.Collections.IDictionary attributes)
            return null;
        if (!attributes.Contains(attributeType))
            return null;

        return Convert.ToUInt32(attributes[attributeType]);
    }

    private static void RevealBoardUiHealthBar(BoardUIController controller, bool showStatusNumbers)
    {
        var healthBar = GetBoardUiHealthBar(controller);
        if (healthBar == null)
            return;

        InvokeOptionalMethod(healthBar, "ToggleBarParent", true);
        InvokeOptionalMethod(healthBar, "ToggleStatusNumbers", showStatusNumbers);
        InvokeOptionalMethod(healthBar, "RefillHealthBar", 1f);
    }

    private static object? GetBoardUiHealthBar(BoardUIController controller)
    {
        return controller
            .GetType()
            .GetField(
                "HealthBar",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            )
            ?.GetValue(controller);
    }

    private static void InvokeOptionalMethod(object target, string methodName, object argument)
    {
        var method = target
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(candidate =>
            {
                if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    return false;

                var parameters = candidate.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType.IsInstanceOfType(argument);
            });

        method?.Invoke(target, [argument]);
    }

    private static void InvokeBoardUiMethod(
        BoardUIController controller,
        string methodName,
        object? argument = null
    )
    {
        var methods = controller
            .GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => string.Equals(method.Name, methodName, StringComparison.Ordinal));

        MethodInfo? targetMethod = null;
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            if (argument == null)
            {
                if (parameters.Length == 0)
                {
                    targetMethod = method;
                    break;
                }

                continue;
            }

            if (parameters.Length == 1 && parameters[0].ParameterType.IsInstanceOfType(argument))
            {
                targetMethod = method;
                break;
            }
        }

        if (targetMethod == null)
            return;

        if (argument == null)
        {
            targetMethod.Invoke(controller, null);
            return;
        }

        targetMethod.Invoke(controller, [argument]);
    }

    private static void HideObjectsOfType<T>()
        where T : Component
    {
        foreach (var component in Resources.FindObjectsOfTypeAll<T>())
        {
            if (component?.gameObject != null)
                component.gameObject.SetActive(false);
        }
    }
}

internal sealed class ReplayBoardUiBindings
{
    public ReplayBoardUiBindings(
        BoardUIController? playerController,
        BoardUIController? opponentController
    )
    {
        PlayerController = playerController;
        OpponentController = opponentController;
    }

    public BoardUIController? PlayerController { get; }

    public BoardUIController? OpponentController { get; }
}
