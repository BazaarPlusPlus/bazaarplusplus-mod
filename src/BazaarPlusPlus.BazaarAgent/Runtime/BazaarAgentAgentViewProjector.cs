#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace BazaarPlusPlus.BazaarAgent;

/// <summary>
/// Projects verbose game snapshots into the cache-aware Agent View. State is scoped to an
/// external Agent session, never to a game run, so a reconnect can explicitly reset safely.
/// </summary>
public sealed class BazaarAgentAgentViewProjector
{
    private const int MaximumSessions = 16;
    private readonly object _gate = new();
    private readonly Dictionary<string, SessionKnowledge> _sessions = new(StringComparer.Ordinal);

    public BazaarAgentAgentViewProjection Project(
        BazaarAgentContextSnapshot snapshot,
        string? requestedSessionId,
        bool resetKnowledge = false
    )
    {
        if (snapshot is null)
            throw new ArgumentNullException(nameof(snapshot));

        lock (_gate)
        {
            var sessionId = NormalizeSessionId(requestedSessionId);
            if (resetKnowledge)
                _sessions.Remove(sessionId);

            var session = GetOrCreateSession(sessionId);
            var view = BuildView(snapshot.Context, sessionId, session);
            return new BazaarAgentAgentViewProjection(view, sessionId, session.CacheEpoch);
        }
    }

    private SessionKnowledge GetOrCreateSession(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var existing))
        {
            existing.LastAccessUtc = DateTime.UtcNow;
            return existing;
        }

        if (_sessions.Count >= MaximumSessions)
        {
            var oldest = _sessions.OrderBy(static pair => pair.Value.LastAccessUtc).First();
            _sessions.Remove(oldest.Key);
        }

        var created = new SessionKnowledge();
        _sessions.Add(sessionId, created);
        return created;
    }

    private static BazaarAgentAgentView BuildView(
        BazaarAgentContext context,
        string sessionId,
        SessionKnowledge session
    )
    {
        var knowledge = new List<BazaarAgentCardKnowledge>();
        var knowledgeEmittedThisView = new HashSet<string>(StringComparer.Ordinal);

        BazaarAgentCardRef ProjectCard(BazaarAgentCardSnapshot card)
        {
            var detail = BuildKnowledge(card);
            if (
                session.SeenKnowledgeIds.Add(detail.KnowledgeId)
                && knowledgeEmittedThisView.Add(detail.KnowledgeId)
            )
            {
                knowledge.Add(detail);
            }

            return new BazaarAgentCardRef
            {
                InstanceId = card.InstanceId,
                Kind = card.Kind,
                TemplateId = card.TemplateId,
                DisplayName = card.DisplayName,
                Size = card.Size,
                Location = card.Location,
                SocketId = card.SocketId,
                Order = card.Order,
                KnowledgeId = detail.KnowledgeId,
                BuyPrice = card.BuyPrice,
                SellPrice = card.SellPrice,
                CanAfford = card.CanAfford,
                CanFit = card.CanFit,
                CanSelect = card.CanSelect,
                IsFree = card.IsFree,
                TargetSection = card.TargetSection,
                TargetSockets = card.TargetSockets,
                UnavailableReason = card.UnavailableReason,
                CanSell = card.CanSell,
            };
        }

        return new BazaarAgentAgentView
        {
            AgentSessionId = sessionId,
            CacheEpoch = session.CacheEpoch,
            TickId = context.TickId,
            ServerTimeUtc = context.ServerTimeUtc,
            IsInRun = context.IsInRun,
            HasActiveRun = context.HasActiveRun,
            CanStartOrContinueRun = context.CanStartOrContinueRun,
            IsClientBusy = context.IsClientBusy,
            RunId = context.RunId,
            GameModeId = context.GameModeId,
            StateName = context.StateName,
            PlayerHero = context.PlayerHero,
            Day = context.Day,
            Hour = context.Hour,
            Wins = context.Wins,
            Losses = context.Losses,
            PlayerGold = context.PlayerGold,
            PlayerIncome = context.PlayerIncome,
            PlayerHealth = context.PlayerHealth,
            PlayerMaxHealth = context.PlayerMaxHealth,
            PlayerPrestige = context.PlayerPrestige,
            PlayerLevel = context.PlayerLevel,
            SelectionIsFree = context.SelectionIsFree,
            CanExit = context.CanExit,
            CanReroll = context.CanReroll,
            RerollCost = context.RerollCost,
            RerollsRemaining = context.RerollsRemaining,
            CurrentEncounterId = context.CurrentEncounterId,
            CurrentEncounterType = context.CurrentEncounterType,
            ActionCooldownRemainingSeconds = context.ActionCooldownRemainingSeconds,
            ReplayPhase = context.ReplayPhase,
            ReplayBattleId = context.ReplayBattleId,
            InteractableTemplateIds = context.InteractableTemplateIds,
            BoardItems = context.BoardItems.Select(ProjectCard).ToArray(),
            ChestItems = context.ChestItems.Select(ProjectCard).ToArray(),
            PlayerSkills = context.PlayerSkills.Select(ProjectCard).ToArray(),
            SelectionOptions = context.SelectionOptions.Select(ProjectCard).ToArray(),
            AvailableActions = context
                .AvailableActions.Select(static option => new BazaarAgentCompactDecisionOption
                {
                    ActionKind = option.ActionKind,
                    Group = option.Group,
                    CardInstanceId = option.CardInstanceId,
                    TargetSection = option.TargetSection,
                    TargetSockets = option.TargetSockets,
                })
                .ToArray(),
            CardKnowledge = knowledge,
        };
    }

    private static BazaarAgentCardKnowledge BuildKnowledge(BazaarAgentCardSnapshot card)
    {
        var tags = card.Tags.OrderBy(static value => value, StringComparer.Ordinal).ToArray();
        var hiddenTags = card
            .HiddenTags.OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        // Current cooldown changes every combat frame. CooldownMax and modifiers remain semantic.
        var attributes = card
            .Attributes.Where(static pair =>
                pair.Value != 0 && !string.Equals(pair.Key, "Cooldown", StringComparison.Ordinal)
            )
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal
            );
        var abilities = card
            .ActiveAbilities.OrderBy(AbilitySortKey, StringComparer.Ordinal)
            .ToArray();

        return new BazaarAgentCardKnowledge
        {
            KnowledgeId = ComputeKnowledgeId(card, tags, hiddenTags, attributes, abilities),
            Kind = card.Kind,
            Type = card.Type,
            TemplateId = card.TemplateId,
            DisplayName = card.DisplayName,
            Tier = card.Tier,
            Size = card.Size,
            Enchantment = card.Enchantment,
            Tags = tags,
            HiddenTags = hiddenTags,
            Attributes = attributes,
            ActiveAbilities = abilities,
        };
    }

    private static string ComputeKnowledgeId(
        BazaarAgentCardSnapshot card,
        IReadOnlyList<string> tags,
        IReadOnlyList<string> hiddenTags,
        IReadOnlyDictionary<string, int> attributes,
        IReadOnlyList<BazaarAgentCardAbilitySnapshot> abilities
    )
    {
        var canonical = new StringBuilder();
        Append(canonical, card.Kind.ToString());
        Append(canonical, card.Type);
        Append(canonical, card.TemplateId);
        Append(canonical, card.DisplayName);
        Append(canonical, card.Tier);
        Append(canonical, card.Size);
        Append(canonical, card.Enchantment);
        foreach (var tag in tags)
            Append(canonical, tag);
        Append(canonical, "#hidden");
        foreach (var tag in hiddenTags)
            Append(canonical, tag);
        Append(canonical, "#attributes");
        foreach (
            var attribute in attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
        )
        {
            Append(canonical, attribute.Key);
            Append(
                canonical,
                attribute.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
        }
        Append(canonical, "#abilities");
        foreach (var ability in abilities)
            AppendAbility(canonical, ability);

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
        return "ck_" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private static void Append(StringBuilder builder, string? value)
    {
        value ??= "";
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private static string AbilitySortKey(BazaarAgentCardAbilitySnapshot ability)
    {
        var canonical = new StringBuilder();
        AppendAbility(canonical, ability);
        return canonical.ToString();
    }

    private static void AppendAbility(StringBuilder builder, BazaarAgentCardAbilitySnapshot ability)
    {
        Append(builder, ability.Id);
        Append(builder, ability.InternalName);
        Append(builder, ability.InternalDescription);
        Append(builder, ability.Trigger);
        Append(builder, ability.Action);
        Append(builder, ability.ActiveIn);
        Append(builder, ability.WorksIn);
        Append(builder, ability.Priority);
    }

    private static string NormalizeSessionId(string? requestedSessionId)
    {
        if (!string.IsNullOrWhiteSpace(requestedSessionId))
        {
            var candidate = requestedSessionId.Trim();
            if (candidate.Length <= 128 && candidate.All(IsSessionCharacter))
                return candidate;
        }

        return BazaarAgentUlid.New();
    }

    private static bool IsSessionCharacter(char value) =>
        (value >= 'a' && value <= 'z')
        || (value >= 'A' && value <= 'Z')
        || (value >= '0' && value <= '9')
        || value is '-' or '_' or '.';

    private sealed class SessionKnowledge
    {
        internal string CacheEpoch { get; } = BazaarAgentUlid.New();
        internal HashSet<string> SeenKnowledgeIds { get; } = new(StringComparer.Ordinal);
        internal DateTime LastAccessUtc { get; set; } = DateTime.UtcNow;
    }
}

public readonly record struct BazaarAgentAgentViewProjection(
    BazaarAgentAgentView View,
    string AgentSessionId,
    string CacheEpoch
);
