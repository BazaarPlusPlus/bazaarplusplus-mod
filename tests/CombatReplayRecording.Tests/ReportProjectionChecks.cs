using System.Reflection;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Infra.Messages;
using BazaarGameShared.Infra.Messages.CombatSimEvents;
using BazaarGameShared.Infra.Messages.Shared;
using BazaarPlusPlus.Game.CombatReplay.ReportData;
using BazaarPlusPlus.Game.PvpBattles;

internal static class ReportProjectionChecks
{
    internal static void Run()
    {
        var cardId = new InstanceId("card-a");
        var triggerId = new InstanceId("trigger-a");
        var removedId = new InstanceId("removed-a");
        var firstFrame = new CombatSimFrame
        {
            PlayerUpdates = new CombatSimPlayerUpdate
            {
                HealthAdjustments =
                {
                    new CombatSimPlayerHealthAdjustment
                    {
                        AttributeChanged = EPlayerHealthChangeType.Health,
                        DamageType = EDamageType.Damage,
                        Amount = -25,
                    },
                },
                Attributes =
                {
                    [EPlayerAttributeType.Health] = Attribute(
                        EPlayerAttributeType.Health,
                        1_000,
                        1_000
                    ),
                    [EPlayerAttributeType.HealthRegen] = Attribute(
                        EPlayerAttributeType.HealthRegen,
                        12,
                        12
                    ),
                    [EPlayerAttributeType.Shield] = Attribute(EPlayerAttributeType.Shield, 40, 40),
                },
                Portrait = new CombatSimPlayerPortraitUpdate { Index = 3 },
            },
            OpponentUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [EPlayerAttributeType.Health] = Attribute(
                        EPlayerAttributeType.Health,
                        900,
                        900
                    ),
                    [EPlayerAttributeType.Rage] = Attribute(EPlayerAttributeType.Rage, 0, 0),
                    [EPlayerAttributeType.HealthRegen] = Attribute(
                        EPlayerAttributeType.HealthRegen,
                        0,
                        0
                    ),
                    [EPlayerAttributeType.Shield] = Attribute(EPlayerAttributeType.Shield, 5, 5),
                },
            },
        };
        firstFrame.Events.Add(
            new CombatSimEventEffectAuraExecuted
            {
                ExecutionContextId = "context-a",
                EffectId = "effect-a",
                Source = cardId,
                TriggerSource = triggerId,
                AppliedTo = new HashSet<IEffectTarget>
                {
                    new EffectTargetPlayer { Target = ECombatantId.Opponent },
                },
                RemovedFrom = new HashSet<IEffectTarget>
                {
                    new EffectTargetCard { Target = removedId },
                },
            }
        );
        firstFrame.CardUpdates[cardId] = new CombatSimCardUpdate
        {
            CardInstanceId = cardId,
            Attributes =
            {
                [ECardAttributeType.DamageAmount] = CardAttribute(
                    ECardAttributeType.DamageAmount,
                    10,
                    10
                ),
                [ECardAttributeType.Cooldown] = CardAttribute(
                    ECardAttributeType.Cooldown,
                    5_000,
                    4_500
                ),
            },
            State = new CombatSimCardStateUpdate(),
        };

        var lateRageFrame = new CombatSimFrame
        {
            PlayerUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [EPlayerAttributeType.Rage] = Attribute(EPlayerAttributeType.Rage, 7, 10),
                },
            },
        };
        var combat = new CombatSim
        {
            Frames = new List<CombatSimFrame> { firstFrame, lateRageFrame },
        };
        var document = Project(
            new PvpBattleManifest
            {
                BattleId = "0123456789abcdef0123456789abcdef",
                RecordedAtUtc = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
            },
            new NetMessageCombatSim(combat)
        );

        Require(
            document.FrameZeroState.Player.Health == 1_000
                && document.FrameZeroState.Player.HealthRegen == 12
                && document.FrameZeroState.Player.Shield == 40,
            "Delta-zero player metrics must survive as explicit frame-zero baselines."
        );
        Require(
            document.FrameZeroState.Player.Rage == 7,
            "A late first Rage transition must project its PreviousValue into frame zero."
        );
        Require(
            document.FrameZeroState.Opponent.Health == 900
                && document.FrameZeroState.Opponent.Rage == 0
                && document.FrameZeroState.Opponent.HealthRegen == 0
                && document.FrameZeroState.Opponent.Shield == 5,
            "Opponent frame-zero state must explicitly contain all four metrics."
        );

        var damage = document.Events.Single(entry => entry.Kind == "health");
        Require(
            damage.SourceEntityId == null
                && damage.AttributionConfidence == "target-exact-source-unknown",
            "Unknown-source damage must remain source-unknown instead of being assigned to the opposite side."
        );
        var aura = document.Events.Single(entry => entry.Kind == "aura");
        Require(
            aura.SourceEntityId == cardId.Value
                && aura.TriggerSourceEntityId == triggerId.Value
                && aura.TargetEntityIds.SequenceEqual(new[] { "player:Opponent" })
                && aura.RemovedTargetEntityIds.SequenceEqual(new[] { removedId.Value })
                && aura.AttributionConfidence == "exact",
            "Projected relations must retain source, trigger, targets, removed targets, and confidence."
        );
        Require(
            document.RawRecordCount == 14 && document.RawRecordCount > document.Events.Count,
            $"RawRecordCount must count all raw subrecords including delta-zero updates (raw={document.RawRecordCount}, projected={document.Events.Count})."
        );
    }

    private static CombatReportDocumentV1 Project(
        PvpBattleManifest manifest,
        NetMessageCombatSim message
    )
    {
        var projectorType = typeof(CombatReportDocumentV1).Assembly.GetType(
            "BazaarPlusPlus.Game.CombatReplay.ReportData.CombatReportProjector",
            throwOnError: true
        )!;
        var projector = Activator.CreateInstance(projectorType, nonPublic: true)!;
        var method = projectorType.GetMethod(
            "Project",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        return (CombatReportDocumentV1)
            method.Invoke(projector, new object[] { manifest, message })!;
    }

    private static CombatSimPlayerAttributeUpdate Attribute(
        EPlayerAttributeType type,
        int previous,
        int current
    ) =>
        new()
        {
            AttributeType = type,
            PreviousValue = previous,
            CurrentValue = current,
        };

    private static CombatSimCardAttributeUpdate CardAttribute(
        ECardAttributeType type,
        int previous,
        int current
    ) =>
        new()
        {
            AttributeType = type,
            PreviousValue = previous,
            CurrentValue = current,
        };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
