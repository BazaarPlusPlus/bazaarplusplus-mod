#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using HarmonyLib;
using TheBazaar;
using TheBazaar.UI.EndOfRun;

namespace BazaarPlusPlus.Game.AutoBazaar;

internal readonly record struct AutoBazaarDispatchResult(bool Executed, string? Error);

internal static class AutoBazaarActionDispatcher
{
    /// <summary>Main thread only. Routes the action to the appropriate game API.</summary>
    public static AutoBazaarDispatchResult Execute(AutoBazaarAction action, AutoBazaarContextSnapshot snapshot)
    {
        try
        {
            return Dispatch(action, snapshot);
        }
        catch (Exception ex)
        {
            BppLog.Error("AutoBazaar", $"dispatch threw for {action.ActionKind}", ex);
            return new(false, $"dispatcher exception: {ex.GetType().Name}");
        }
    }

    private static AutoBazaarDispatchResult Dispatch(AutoBazaarAction action, AutoBazaarContextSnapshot snapshot)
    {
        switch (action.ActionKind)
        {
            case AutoBazaarActionKind.Wait:
                return new(true, null);

            case AutoBazaarActionKind.StartOrContinueRun:
            {
                if (action.Hero is { } heroStr)
                {
                    if (!Enum.TryParse<EHero>(heroStr, ignoreCase: true, out var hero))
                        return new(false, "unknown hero");
                    var setHeroErr = SetRunConfigSelectedHero(hero);
                    if (setHeroErr is not null) return new(false, setHeroErr);
                }
                if (action.PlayMode is { } modeStr)
                {
                    if (!Enum.TryParse<EPlayMode>(modeStr, ignoreCase: true, out var mode))
                        return new(false, "unknown playMode");
                    var setModeErr = SetRunConfigSelectedPlaymode(mode);
                    if (setModeErr is not null) return new(false, setModeErr);
                }
                if (GameInstance.Instance is null)
                    return new(false, "GameInstance.Instance is null");
                GameInstance.Instance.StartNewRun();
                return new(true, null);
            }

            case AutoBazaarActionKind.AbandonRun:
                Cmd.GetInstance().SendAbandonRun();
                return new(true, null);

            case AutoBazaarActionKind.SelectItem:
            {
                var card = ResolveItemCard(action.CardInstanceId);
                if (card is null) return new(false, "item not found in Data.Entities");
                EInventorySection section;
                try
                {
                    section = (EInventorySection)Enum.Parse(typeof(EInventorySection),
                        (action.TargetSection ?? AutoBazaarTargetSection.Hand).ToString());
                }
                catch
                {
                    return new(false, "unsupported target section");
                }
                var sockets = ParseSockets(action.TargetSockets);
                return InvokeCmd("SelectItem", card, sockets, section);
            }

            case AutoBazaarActionKind.SelectSkill:
                Cmd.GetInstance().SendSelectSkill(new InstanceId(action.CardInstanceId ?? ""));
                return new(true, null);

            case AutoBazaarActionKind.SelectEncounter:
                Cmd.GetInstance().SelectEncounter(new InstanceId(action.CardInstanceId ?? ""));
                return new(true, null);

            case AutoBazaarActionKind.CommitToPedestal:
                Cmd.GetInstance().SendCommitToPedestal(new InstanceId(action.CardInstanceId ?? ""));
                return new(true, null);

            case AutoBazaarActionKind.MoveItem:
            {
                var card = ResolveItemCard(action.CardInstanceId);
                if (card is null) return new(false, "item not found in Data.Entities");
                EInventorySection section;
                try
                {
                    section = (EInventorySection)Enum.Parse(typeof(EInventorySection),
                        (action.TargetSection ?? AutoBazaarTargetSection.Hand).ToString());
                }
                catch
                {
                    return new(false, "unsupported target section");
                }
                var sockets = ParseSockets(action.TargetSockets);
                return InvokeCmd("SendMoveItem", card, sockets, section);
            }

            case AutoBazaarActionKind.SellItem:
                return InvokeCmd("SendSellCard", new InstanceId(action.CardInstanceId ?? ""));

            case AutoBazaarActionKind.Reroll:
                Cmd.GetInstance().SendReRollSelection();
                return new(true, null);

            case AutoBazaarActionKind.ExitState:
                Cmd.GetInstance().SendExitCurrentState();
                return new(true, null);

            case AutoBazaarActionKind.AdvanceEndRun:
            {
                var controller = UnityEngine.Object.FindObjectOfType<EndOfRunScreenController>();
                if (controller is null) return new(false, "EndOfRunScreenController not in scene");
                var method = typeof(EndOfRunScreenController).GetMethod(
                    "OnContinueClick",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method is null) return new(false, "OnContinueClick not found via reflection");
                method.Invoke(controller, null);
                return new(true, null);
            }

            default:
                return new(false, $"unhandled ActionKind: {action.ActionKind}");
        }
    }

    /// <summary>
    /// Calls ClientCache.RunConfig.SetSelectedHero via reflection (ClientCache is not directly
    /// reachable at compile time in the mod project — same pattern as BppClientCacheBridge).
    /// Returns null on success, or an error string on failure.
    /// </summary>
    private static string? SetRunConfigSelectedHero(EHero hero)
    {
        var clientCacheType = AccessTools.TypeByName("TheBazaar.ClientCache");
        if (clientCacheType is null) return "ClientCache type not found";
        var runConfigField = clientCacheType.GetField(
            "RunConfig",
            BindingFlags.Static | BindingFlags.Public);
        var runConfig = runConfigField?.GetValue(null);
        if (runConfig is null) return "ClientCache.RunConfig not found";
        var method = runConfig.GetType().GetMethod(
            "SetSelectedHero",
            BindingFlags.Instance | BindingFlags.Public);
        if (method is null) return "RunConfigurationCache.SetSelectedHero not found";
        method.Invoke(runConfig, new object[] { hero });
        return null;
    }

    /// <summary>
    /// Calls ClientCache.RunConfig.SetSelectedPlaymode via reflection.
    /// Returns null on success, or an error string on failure.
    /// </summary>
    private static string? SetRunConfigSelectedPlaymode(EPlayMode mode)
    {
        var clientCacheType = AccessTools.TypeByName("TheBazaar.ClientCache");
        if (clientCacheType is null) return "ClientCache type not found";
        var runConfigField = clientCacheType.GetField(
            "RunConfig",
            BindingFlags.Static | BindingFlags.Public);
        var runConfig = runConfigField?.GetValue(null);
        if (runConfig is null) return "ClientCache.RunConfig not found";
        var method = runConfig.GetType().GetMethod(
            "SetSelectedPlaymode",
            BindingFlags.Instance | BindingFlags.Public);
        if (method is null) return "RunConfigurationCache.SetSelectedPlaymode not found";
        method.Invoke(runConfig, new object[] { mode });
        return null;
    }

    private static ItemCard? ResolveItemCard(string? instanceIdValue)
    {
        if (string.IsNullOrEmpty(instanceIdValue)) return null;
        var id = new InstanceId(instanceIdValue);
        if (!Data.Entities.TryGetValue(id, out var entity)) return null;
        return entity as ItemCard;
    }

    private static List<EContainerSocketId> ParseSockets(IReadOnlyList<string>? raw)
    {
        var list = new List<EContainerSocketId>();
        if (raw is null) return list;
        foreach (var s in raw)
        {
            if (Enum.TryParse<EContainerSocketId>(s, ignoreCase: true, out var v)) list.Add(v);
        }
        return list;
    }

    private static AutoBazaarDispatchResult InvokeCmd(string methodName, params object[] args)
    {
        var cmd = Cmd.GetInstance();
        if (cmd is null) return new(false, "Cmd.GetInstance() returned null");
        var methods = typeof(Cmd).GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (var m in methods)
        {
            if (m.Name != methodName) continue;
            var ps = m.GetParameters();
            if (ps.Length < args.Length) continue;
            var match = true;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is not null && !ps[i].ParameterType.IsAssignableFrom(args[i].GetType()))
                {
                    match = false;
                    break;
                }
            }
            if (!match) continue;
            var fullArgs = new object?[ps.Length];
            for (var i = 0; i < args.Length; i++) fullArgs[i] = args[i];
            for (var i = args.Length; i < ps.Length; i++)
            {
                fullArgs[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            }
            m.Invoke(cmd, fullArgs);
            return new(true, null);
        }
        return new(false, $"Cmd.{methodName} not found with compatible signature");
    }
}
