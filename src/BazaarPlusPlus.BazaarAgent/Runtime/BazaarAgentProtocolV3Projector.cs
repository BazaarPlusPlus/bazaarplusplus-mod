#nullable enable
using System.Globalization;

namespace BazaarPlusPlus.BazaarAgent;

/// <summary>
/// Maintains the small amount of server-side state needed to turn immutable game snapshots into
/// an Agent-applicable full snapshot or delta. Short IDs are scoped to this projector session.
/// </summary>
public sealed class BazaarAgentProtocolV3Projector
{
    private const int MaximumSessions = 16;
    private readonly object _gate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public BazaarAgentV3Projection Bootstrap(BazaarAgentContextSnapshot snapshot)
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            var sessionId = BazaarAgentUlid.New();
            var session = CreateSession(sessionId);
            session.LastAccessUtc = DateTime.UtcNow;
            var view = BuildView(snapshot.Context, session, full: true);
            session.LastContext = snapshot.Context;
            session.LastRevision = snapshot.TickId;
            return new BazaarAgentV3Projection(view, sessionId);
        }
    }

    public bool TryProjectDelta(
        BazaarAgentContextSnapshot snapshot,
        string? requestedSessionId,
        ulong revision,
        out BazaarAgentV3Projection projection
    )
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            if (
                string.IsNullOrWhiteSpace(requestedSessionId)
                || !_sessions.TryGetValue(requestedSessionId.Trim(), out var session)
                || session.LastRevision != revision
            )
            {
                projection = default;
                return false;
            }
            session.LastAccessUtc = DateTime.UtcNow;
            var view = BuildView(snapshot.Context, session, full: false);
            session.LastContext = snapshot.Context;
            session.LastRevision = snapshot.TickId;
            projection = new BazaarAgentV3Projection(view, requestedSessionId.Trim());
            return true;
        }
    }

    public bool IsCurrentSession(string? requestedSessionId, ulong revision, ulong currentRevision)
    {
        lock (_gate)
        {
            return !string.IsNullOrWhiteSpace(requestedSessionId)
                && _sessions.TryGetValue(requestedSessionId.Trim(), out var session)
                && session.LastRevision == revision
                && currentRevision == revision;
        }
    }

    public bool TryResolveInstanceId(
        string? requestedSessionId,
        string token,
        out string instanceId
    )
    {
        lock (_gate)
        {
            if (
                !string.IsNullOrWhiteSpace(requestedSessionId)
                && _sessions.TryGetValue(requestedSessionId.Trim(), out var session)
                && session.InstanceByToken.TryGetValue(token, out instanceId!)
            )
            {
                session.LastAccessUtc = DateTime.UtcNow;
                return true;
            }
            instanceId = "";
            return false;
        }
    }

    private Session CreateSession(string sessionId)
    {
        if (_sessions.Count >= MaximumSessions)
        {
            var oldest = _sessions.OrderBy(static entry => entry.Value.LastAccessUtc).First();
            _sessions.Remove(oldest.Key);
        }
        var created = new Session();
        _sessions.Add(sessionId, created);
        return created;
    }

    private static BazaarAgentV3Context BuildView(
        BazaarAgentContext current,
        Session session,
        bool full
    )
    {
        var previous = session.LastContext;
        var previousCards = full || previous is null ? null : IndexCards(previous);
        return new BazaarAgentV3Context
        {
            Revision = current.TickId,
            IsFull = full ? true : null,
            State = BuildState(current, full ? null : previous),
            Board = BuildChanges(
                current.BoardItems,
                full ? null : previous!.BoardItems,
                previousCards,
                session,
                full
            ),
            Chest = BuildChanges(
                current.ChestItems,
                full ? null : previous!.ChestItems,
                previousCards,
                session,
                full
            ),
            Skills = BuildChanges(
                current.PlayerSkills,
                full ? null : previous!.PlayerSkills,
                previousCards,
                session,
                full
            ),
            Selection = BuildChanges(
                current.SelectionOptions,
                full ? null : previous!.SelectionOptions,
                previousCards,
                session,
                full
            ),
            Operations = OperationsChanged(current, previous, full)
                ? BuildOperations(current)
                : null,
            LockedBoardSlots = LocksChanged(current, previous, full)
                ? ToSlotIndexes(current.LockedBoardSockets)
                : null,
            LastBattle =
                full || !ReferenceEquals(current.LastBattle, previous!.LastBattle)
                    ? current.LastBattle
                    : null,
            BattleCleared =
                !full && current.LastBattle is null && previous!.LastBattle is not null
                    ? true
                    : null,
        };
    }

    private static IReadOnlyDictionary<string, object?>? BuildState(
        BazaarAgentContext current,
        BazaarAgentContext? previous
    )
    {
        var now = StateValues(current);
        if (previous is null)
            return now;
        var before = StateValues(previous);
        var changed = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in now)
            if (!before.TryGetValue(pair.Key, out var old) || !Equals(pair.Value, old))
                changed.Add(pair.Key, pair.Value);
        return changed.Count == 0 ? null : changed;
    }

    private static Dictionary<string, object?> StateValues(BazaarAgentContext context) =>
        new(StringComparer.Ordinal)
        {
            ["state"] = context.StateName.ToString(),
            ["busy"] = context.IsClientBusy,
            ["run"] = context.RunId,
            ["mode"] = context.GameModeId,
            ["hero"] = context.PlayerHero,
            ["day"] = context.Day,
            ["hour"] = context.Hour,
            ["wins"] = context.Wins,
            ["losses"] = context.Losses,
            ["gold"] = context.PlayerGold,
            ["income"] = context.PlayerIncome,
            ["health"] = context.PlayerHealth,
            ["maxHealth"] = context.PlayerMaxHealth,
            ["prestige"] = context.PlayerPrestige,
            ["level"] = context.PlayerLevel,
            ["rerolls"] = context.RerollsRemaining,
            ["rerollCost"] = context.RerollCost,
            ["freeSelection"] = context.SelectionIsFree,
            ["encounter"] = context.CurrentEncounterId,
            ["encounterType"] = context.CurrentEncounterType,
            ["replay"] = context.ReplayPhase.ToString(),
        };

    private static BazaarAgentV3CardChanges? BuildChanges(
        IReadOnlyList<BazaarAgentCardSnapshot> current,
        IReadOnlyList<BazaarAgentCardSnapshot>? previous,
        IReadOnlyDictionary<string, BazaarAgentCardSnapshot>? previousCards,
        Session session,
        bool full
    )
    {
        var before = previous?.ToDictionary(static card => card.InstanceId, StringComparer.Ordinal);
        var upsert = new List<BazaarAgentV3Card>();
        foreach (var card in current)
        {
            if (full || before is null)
                upsert.Add(ProjectCard(card, session));
            else if (
                !before.TryGetValue(card.InstanceId, out var old)
                && previousCards is not null
                && previousCards.TryGetValue(card.InstanceId, out var moved)
            )
                upsert.Add(ProjectCardDelta(card, moved, session));
            else if (!before.TryGetValue(card.InstanceId, out _))
                upsert.Add(ProjectCard(card, session));
            else if (!CardEqual(card, old))
                upsert.Add(ProjectCardDelta(card, old, session));
        }

        List<string>? remove = null;
        if (!full && previous is not null)
        {
            var currentIds = current
                .Select(static card => card.InstanceId)
                .ToHashSet(StringComparer.Ordinal);
            remove = previous
                .Where(card => !currentIds.Contains(card.InstanceId))
                .Select(card => GetInstanceToken(session, card))
                .ToList();
        }
        return upsert.Count == 0 && (remove is null || remove.Count == 0)
            ? null
            : new BazaarAgentV3CardChanges
            {
                Upsert = upsert.Count == 0 ? null : upsert,
                Remove = remove is { Count: > 0 } ? remove : null,
            };
    }

    private static BazaarAgentV3Card ProjectCard(
        BazaarAgentCardSnapshot card,
        Session session,
        bool replace = false
    ) =>
        new()
        {
            Id = GetInstanceToken(session, card),
            Template = GetTemplateToken(session, card.TemplateId),
            Name = card.DisplayName ?? "",
            IsFull = replace ? true : null,
            Slots = Slots(card),
            Size = card.Size,
            Tier = card.Tier,
            Enchantment = card.Enchantment,
            Tags = card.Tags,
            HiddenTags = card.HiddenTags,
            Description = card.Description,
            CooldownSeconds = card.CooldownSeconds,
            Ammo = card.Ammo,
            AmmoMax = card.AmmoMax,
            BuyPrice = card.BuyPrice,
            SellPrice = card.Kind == BazaarAgentCardKind.Item ? card.SellPrice : null,
        };

    private static BazaarAgentV3Card ProjectCardDelta(
        BazaarAgentCardSnapshot current,
        BazaarAgentCardSnapshot previous,
        Session session
    )
    {
        if (RequiresFullReplacement(current, previous))
            return ProjectCard(current, session, replace: true);
        return new BazaarAgentV3Card
        {
            Id = GetInstanceToken(session, current),
            Slots = SequenceEqual(Slots(current), Slots(previous)) ? null : Slots(current),
            Size = current.Size != previous.Size ? current.Size : null,
            Tier = current.Tier != previous.Tier ? current.Tier : null,
            Enchantment = current.Enchantment != previous.Enchantment ? current.Enchantment : null,
            Tags = SequenceEqual(current.Tags, previous.Tags) ? null : current.Tags,
            HiddenTags = SequenceEqual(current.HiddenTags, previous.HiddenTags)
                ? null
                : current.HiddenTags,
            Description = current.Description != previous.Description ? current.Description : null,
            CooldownSeconds =
                current.CooldownSeconds != previous.CooldownSeconds
                    ? current.CooldownSeconds
                    : null,
            Ammo = current.Ammo != previous.Ammo ? current.Ammo : null,
            AmmoMax = current.AmmoMax != previous.AmmoMax ? current.AmmoMax : null,
            BuyPrice = current.BuyPrice != previous.BuyPrice ? current.BuyPrice : null,
            SellPrice =
                current.Kind == BazaarAgentCardKind.Item && current.SellPrice != previous.SellPrice
                    ? current.SellPrice
                    : null,
        };
    }

    private static bool RequiresFullReplacement(
        BazaarAgentCardSnapshot current,
        BazaarAgentCardSnapshot previous
    ) =>
        (Slots(current) is null && Slots(previous) is not null)
        || (current.Size is null && previous.Size is not null)
        || (current.Tier is null && previous.Tier is not null)
        || (current.Enchantment is null && previous.Enchantment is not null)
        || (current.Tags is null && previous.Tags is not null)
        || (current.HiddenTags is null && previous.HiddenTags is not null)
        || (current.Description is null && previous.Description is not null)
        || (current.CooldownSeconds is null && previous.CooldownSeconds is not null)
        || (current.Ammo is null && previous.Ammo is not null)
        || (current.AmmoMax is null && previous.AmmoMax is not null)
        || (current.BuyPrice is null && previous.BuyPrice is not null)
        || (current.SellPrice is null && previous.SellPrice is not null);

    private static IReadOnlyList<int>? Slots(BazaarAgentCardSnapshot card)
    {
        if (
            card.Location is not BazaarAgentCardLocation.Board and not BazaarAgentCardLocation.Chest
        )
            return null;
        if (!TryParseSlot(card.SocketId, out var start))
            return null;
        var size = BazaarAgentCardSize.Parse(card.Size, fallback: 1);
        return Enumerable.Range(start, size).ToArray();
    }

    private static IReadOnlyList<string> BuildOperations(BazaarAgentContext context) =>
        context
            .AvailableActions.Select(static action =>
                action.ActionKind switch
                {
                    BazaarAgentActionKind.StartOrContinueRun => "start",
                    BazaarAgentActionKind.SelectItem => "select",
                    BazaarAgentActionKind.SelectSkill => "select",
                    BazaarAgentActionKind.SelectEncounter => "select",
                    BazaarAgentActionKind.CommitToPedestal => "select",
                    BazaarAgentActionKind.MoveItem => "move",
                    BazaarAgentActionKind.SellItem => "sell",
                    BazaarAgentActionKind.Reroll => "reroll",
                    BazaarAgentActionKind.ExitState => "exit",
                    BazaarAgentActionKind.ReturnToMenu => "menu",
                    BazaarAgentActionKind.Continue => "continue",
                    _ => "",
                }
            )
            .Where(static operation => operation.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static operation => operation, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyDictionary<string, BazaarAgentCardSnapshot> IndexCards(
        BazaarAgentContext context
    )
    {
        var indexed = new Dictionary<string, BazaarAgentCardSnapshot>(StringComparer.Ordinal);
        foreach (
            var card in context
                .BoardItems.Concat(context.ChestItems)
                .Concat(context.PlayerSkills)
                .Concat(context.SelectionOptions)
        )
        {
            // Native purchase animations can expose the same card in an owned container and the
            // outgoing selection simultaneously. Owned sections deliberately win over selection.
            if (!indexed.ContainsKey(card.InstanceId))
                indexed.Add(card.InstanceId, card);
        }
        return indexed;
    }

    private static bool OperationsChanged(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        bool full
    ) =>
        full
        || previous is null
        || !SequenceEqual(BuildOperations(current), BuildOperations(previous));

    private static bool LocksChanged(
        BazaarAgentContext current,
        BazaarAgentContext? previous,
        bool full
    ) =>
        full
        || previous is null
        || !SequenceEqual(current.LockedBoardSockets, previous.LockedBoardSockets);

    private static IReadOnlyList<int> ToSlotIndexes(IReadOnlyList<string> sockets) =>
        sockets
            .Where(static socket => TryParseSlot(socket, out _))
            .Select(static socket =>
            {
                _ = TryParseSlot(socket, out var slot);
                return slot;
            })
            .ToArray();

    private static string GetInstanceToken(Session session, BazaarAgentCardSnapshot card)
    {
        if (session.TokenByInstance.TryGetValue(card.InstanceId, out var token))
            return token;
        var prefix = card.Kind switch
        {
            BazaarAgentCardKind.Item => "i",
            BazaarAgentCardKind.Skill => "s",
            BazaarAgentCardKind.Encounter => "e",
            _ => "c",
        };
        token = prefix + (++session.NextInstanceToken).ToString("D5", CultureInfo.InvariantCulture);
        session.TokenByInstance.Add(card.InstanceId, token);
        session.InstanceByToken.Add(token, card.InstanceId);
        return token;
    }

    private static string GetTemplateToken(Session session, string? templateId)
    {
        var value = templateId ?? "";
        if (session.TokenByTemplate.TryGetValue(value, out var token))
            return token;
        token = "t" + (++session.NextTemplateToken).ToString("D5", CultureInfo.InvariantCulture);
        session.TokenByTemplate.Add(value, token);
        return token;
    }

    private static bool CardEqual(BazaarAgentCardSnapshot left, BazaarAgentCardSnapshot right) =>
        left.Kind == right.Kind
        && left.Type == right.Type
        && left.TemplateId == right.TemplateId
        && left.DisplayName == right.DisplayName
        && left.Tier == right.Tier
        && left.Size == right.Size
        && left.Enchantment == right.Enchantment
        && left.SocketId == right.SocketId
        && left.Location == right.Location
        && SequenceEqual(left.Tags, right.Tags)
        && SequenceEqual(left.HiddenTags, right.HiddenTags)
        && left.Description == right.Description
        && left.CooldownSeconds == right.CooldownSeconds
        && left.Ammo == right.Ammo
        && left.AmmoMax == right.AmmoMax
        && left.BuyPrice == right.BuyPrice
        && left.SellPrice == right.SellPrice;

    private static bool SequenceEqual<T>(IReadOnlyList<T>? left, IReadOnlyList<T>? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null || left.Count != right.Count)
            return false;
        for (var index = 0; index < left.Count; index++)
            if (!EqualityComparer<T>.Default.Equals(left[index], right[index]))
                return false;
        return true;
    }

    private static bool TryParseSlot(string? socket, out int slot)
    {
        slot = -1;
        const string prefix = "Socket_";
        return socket is not null
            && socket.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(
                socket.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out slot
            )
            && slot >= 0;
    }

    private sealed class Session
    {
        internal BazaarAgentContext? LastContext { get; set; }
        internal ulong LastRevision { get; set; }
        internal DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
        internal int NextInstanceToken { get; set; }
        internal int NextTemplateToken { get; set; }
        internal Dictionary<string, string> TokenByInstance { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> InstanceByToken { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string> TokenByTemplate { get; } = new(StringComparer.Ordinal);
    }
}

public readonly record struct BazaarAgentV3Projection(BazaarAgentV3Context View, string SessionId);
