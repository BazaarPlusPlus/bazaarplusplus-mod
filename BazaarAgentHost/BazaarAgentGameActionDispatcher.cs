#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using BazaarGameClient.Domain.Models.Cards;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarPlusPlus.BazaarAgent;
using BazaarPlusPlus.GameInterop;
using HarmonyLib;
using TheBazaar;

namespace BazaarPlusPlus.BazaarAgentHost;

internal sealed class BazaarAgentGameActionDispatcher : IBazaarAgentActionDispatcher
{
    private readonly IBazaarAgentLogger _logger;

    public BazaarAgentGameActionDispatcher(IBazaarAgentLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Main thread only. Routes the action through AppState.CurrentState.*Command()
    /// so the game's UI animation + state-validation chain runs the same way a real click does.</summary>
    public BazaarAgentDispatchResult Execute(
        BazaarAgentAction action,
        BazaarAgentContextSnapshot snapshot
    )
    {
        try
        {
            return Dispatch(action, snapshot);
        }
        catch (Exception ex)
        {
            _logger.Error($"dispatch threw for {action.ActionKind}", ex);
            return new(false, $"dispatcher exception: {ex.GetType().Name}");
        }
    }

    private static BazaarAgentDispatchResult Dispatch(
        BazaarAgentAction action,
        BazaarAgentContextSnapshot snapshot
    )
    {
        switch (action.ActionKind)
        {
            case BazaarAgentActionKind.Wait:
                return new(true, null);

            case BazaarAgentActionKind.StartOrContinueRun:
            {
                if (action.Hero is { } heroStr)
                {
                    if (!Enum.TryParse<EHero>(heroStr, ignoreCase: true, out var hero))
                        return new(false, "unknown hero");
                    var setHeroErr = SetRunConfigSelectedHero(hero);
                    if (setHeroErr is not null)
                        return new(false, setHeroErr);
                }
                if (action.PlayMode is { } modeStr)
                {
                    if (!Enum.TryParse<EPlayMode>(modeStr, ignoreCase: true, out var mode))
                        return new(false, "unknown playMode");
                    var setModeErr = SetRunConfigSelectedPlaymode(mode);
                    if (setModeErr is not null)
                        return new(false, setModeErr);
                }
                if (GameInstance.Instance is null)
                    return new(false, "GameInstance.Instance is null");
                GameInstance.Instance.StartNewRun();
                return new(true, null);
            }

            case BazaarAgentActionKind.AbandonRun:
                return InvokeAppStateCommand("AbandonRunCommand");

            case BazaarAgentActionKind.SelectItem:
            {
                var card = ResolveCard<ItemCard>(action.CardInstanceId);
                if (card is null)
                    return new(false, "item not found in Data.Entities");
                if (!TryParseSection(action.TargetSection, out var section))
                    return new(false, "unsupported target section");
                var sockets = ParseSockets(action.TargetSockets);
                return InvokeAppStateCommand("BuyItemCommand", card, sockets, section);
            }

            case BazaarAgentActionKind.SelectSkill:
            {
                var skill = ResolveCard<SkillCard>(action.CardInstanceId);
                if (skill is null)
                    return new(false, "skill not found in Data.Entities");
                return InvokeAppStateCommand("SelectSkillCommand", skill);
            }

            case BazaarAgentActionKind.SelectEncounter:
                return InvokeAppStateCommand(
                    "SelectEncounterCommand",
                    new InstanceId(action.CardInstanceId ?? "")
                );

            case BazaarAgentActionKind.CommitToPedestal:
                return InvokeAppStateCommand(
                    "CommitToPedestalCommand",
                    new InstanceId(action.CardInstanceId ?? "")
                );

            case BazaarAgentActionKind.MoveItem:
            {
                var card = ResolveCard<ItemCard>(action.CardInstanceId);
                if (card is null)
                    return new(false, "item not found in Data.Entities");
                if (!TryParseSection(action.TargetSection, out var section))
                    return new(false, "unsupported target section");
                var sockets = ParseSockets(action.TargetSockets);
                return InvokeAppStateCommand("MoveCardCommand", card, sockets, section);
            }

            case BazaarAgentActionKind.SellItem:
            {
                var card = ResolveCard<ItemCard>(action.CardInstanceId);
                if (card is null)
                    return new(false, "item not found in Data.Entities");
                return InvokeAppStateCommand("SellCardCommand", card);
            }

            case BazaarAgentActionKind.Reroll:
                return InvokeAppStateCommand("RerollCommand");

            case BazaarAgentActionKind.ExitState:
                return InvokeAppStateCommand("ExitStateCommand");

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
        if (clientCacheType is null)
            return "ClientCache type not found";
        var runConfigField = clientCacheType.GetField(
            "RunConfig",
            BindingFlags.Static | BindingFlags.Public
        );
        var runConfig = runConfigField?.GetValue(null);
        if (runConfig is null)
            return "ClientCache.RunConfig not found";
        var method = runConfig
            .GetType()
            .GetMethod("SetSelectedHero", BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
            return "RunConfigurationCache.SetSelectedHero not found";
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
        if (clientCacheType is null)
            return "ClientCache type not found";
        var runConfigField = clientCacheType.GetField(
            "RunConfig",
            BindingFlags.Static | BindingFlags.Public
        );
        var runConfig = runConfigField?.GetValue(null);
        if (runConfig is null)
            return "ClientCache.RunConfig not found";
        var method = runConfig
            .GetType()
            .GetMethod("SetSelectedPlaymode", BindingFlags.Instance | BindingFlags.Public);
        if (method is null)
            return "RunConfigurationCache.SetSelectedPlaymode not found";
        method.Invoke(runConfig, new object[] { mode });
        return null;
    }

    private static T? ResolveCard<T>(string? instanceIdValue)
        where T : class
    {
        if (string.IsNullOrEmpty(instanceIdValue))
            return null;
        var id = new InstanceId(instanceIdValue);
        if (!Data.Entities.TryGetValue(id, out var entity))
            return null;
        return entity as T;
    }

    private static bool TryParseSection(
        BazaarAgentTargetSection? src,
        out EInventorySection section
    )
    {
        try
        {
            section = (EInventorySection)
                Enum.Parse(
                    typeof(EInventorySection),
                    (src ?? BazaarAgentTargetSection.Hand).ToString()
                );
            return true;
        }
        catch
        {
            section = default;
            return false;
        }
    }

    private static List<EContainerSocketId> ParseSockets(IReadOnlyList<string>? raw)
    {
        var list = new List<EContainerSocketId>();
        if (raw is null)
            return list;
        foreach (var s in raw)
        {
            if (Enum.TryParse<EContainerSocketId>(s, ignoreCase: true, out var v))
                list.Add(v);
        }
        return list;
    }

    /// <summary>
    /// Invokes <c>AppState.CurrentState.{methodName}</c> via reflection. Picks the first overload
    /// whose first N parameter types are compatible with the supplied arguments; trailing parameters
    /// with default values are filled with their defaults. Routing through AppState's command
    /// methods (rather than Cmd directly) plays the game's UI animation + state-machine chain.
    /// </summary>
    private static BazaarAgentDispatchResult InvokeAppStateCommand(
        string methodName,
        params object[] args
    )
    {
        var appState = AppState.CurrentState;
        if (appState is null)
            return new(false, "AppState.CurrentState is null");
        var methods = appState.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
        foreach (var m in methods)
        {
            if (m.Name != methodName)
                continue;
            var ps = m.GetParameters();
            if (ps.Length < args.Length)
                continue;
            var match = true;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is not null && !ps[i].ParameterType.IsAssignableFrom(args[i].GetType()))
                {
                    match = false;
                    break;
                }
            }
            if (!match)
                continue;
            var fullArgs = new object?[ps.Length];
            for (var i = 0; i < args.Length; i++)
                fullArgs[i] = args[i];
            for (var i = args.Length; i < ps.Length; i++)
            {
                fullArgs[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;
            }
            m.Invoke(appState, fullArgs);
            return new(true, null);
        }
        return new(false, $"AppState.{methodName} not found with compatible signature");
    }
}
