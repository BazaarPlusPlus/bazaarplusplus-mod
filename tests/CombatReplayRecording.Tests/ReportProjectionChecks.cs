using System.Reflection;
using BazaarGameShared.Domain.Core;
using BazaarGameShared.Domain.Core.Types;
using BazaarGameShared.Domain.Effect;
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
                        975
                    ),
                    [EPlayerAttributeType.HealthRegen] = Attribute(
                        EPlayerAttributeType.HealthRegen,
                        12,
                        12
                    ),
                    [EPlayerAttributeType.Shield] = Attribute(EPlayerAttributeType.Shield, 40, 40),
                    [EPlayerAttributeType.Burn] = Attribute(EPlayerAttributeType.Burn, 3, 3),
                    [EPlayerAttributeType.Poison] = Attribute(EPlayerAttributeType.Poison, 4, 4),
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
                    [EPlayerAttributeType.Burn] = Attribute(EPlayerAttributeType.Burn, 0, 0),
                    [EPlayerAttributeType.Poison] = Attribute(EPlayerAttributeType.Poison, 0, 0),
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
                    [EPlayerAttributeType.Health] = Attribute(
                        EPlayerAttributeType.Health,
                        975,
                        995
                    ),
                },
            },
        };
        var combat = new CombatSim
        {
            Frames = new List<CombatSimFrame> { firstFrame, lateRageFrame },
            CardStats =
            {
                [cardId.Value] = new Dictionary<ECardStats, int>
                {
                    [ECardStats.DamageDone] = 525,
                    [ECardStats.BurnAdded] = 756,
                    [ECardStats.HastedCardsCount] = 65,
                    [ECardStats.UseCount] = 8,
                },
            },
        };
        var manifest = new PvpBattleManifest
        {
            BattleId = "0123456789abcdef0123456789abcdef",
            RecordedAtUtc = DateTimeOffset.Parse("2026-07-22T00:00:00Z"),
        };
        manifest.Snapshots.PlayerHand.Items =
        [
            new PvpBattleCardSnapshot
            {
                InstanceId = "socket-effect-a",
                TemplateId = "c017c0dd-af3c-47e0-b510-3661d9dcd7f2",
                Type = ECardType.SocketEffect,
                Size = ECardSize.Small,
                Name = "[Stove] Socket Effect",
            },
            new PvpBattleCardSnapshot
            {
                InstanceId = cardId.Value,
                TemplateId = "618271c5-5721-40ac-b1c7-43aa06a02d07",
                Type = ECardType.Item,
                Size = ECardSize.Small,
                Name = "Honeycomb",
            },
        ];
        var document = Project(manifest, new NetMessageCombatSim(combat));

        var cardStats = document.CardStats.Single(entry => entry.EntityId == cardId.Value);
        Require(
            cardStats.DamageDone == 525
                && cardStats.BurnAdded == 756
                && cardStats.HastedCardsCount == 65
                && cardStats.UseCount == 8
                && cardStats.HealAdded == 0,
            "The report must project the authoritative CombatSim card totals used by native recap."
        );

        Require(
            document.FrameZeroState.Player.Health == 1_000
                && document.FrameZeroState.Player.HealthRegen == 12
                && document.FrameZeroState.Player.Shield == 40
                && document.FrameZeroState.Player.Burn == 3
                && document.FrameZeroState.Player.Poison == 4,
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
                && document.FrameZeroState.Opponent.Shield == 5
                && document.FrameZeroState.Opponent.Burn == 0
                && document.FrameZeroState.Opponent.Poison == 0,
            "Opponent frame-zero state must explicitly contain all six combat metrics."
        );
        var reportJsonType = typeof(CombatReportDocumentV1).Assembly.GetType(
            "BazaarPlusPlus.Game.CombatReplay.ReportData.CombatReportJson",
            throwOnError: true
        )!;
        var serializeMethod = reportJsonType.GetMethod(
            "Serialize",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(CombatReportDocumentV1)],
            modifiers: null
        )!;
        var serialized = (string)serializeMethod.Invoke(null, [document])!;
        Require(
            serialized.Contains("\"burn\":3", StringComparison.Ordinal)
                && serialized.Contains("\"poison\":4", StringComparison.Ordinal),
            "Burn and Poison frame-zero values must survive the immutable report JSON contract."
        );

        var damage = document.Events.Single(entry => entry.Kind == "health");
        Require(
            damage.SourceEntityId == null
                && damage.AttributionConfidence == "target-exact-source-unknown",
            "Unknown-source damage must remain source-unknown instead of being assigned to the opposite side."
        );
        var healing = document.Events.Single(entry =>
            entry.Kind == "player-attribute" && entry.Action == "Health"
        );
        Require(
            healing.Value == 20 && healing.IconSemanticKey == "status.heal",
            "A positive Health delta must bind the game's native healing icon semantic."
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
            document.RawRecordCount == 19 && document.RawRecordCount > document.Events.Count,
            $"RawRecordCount must count all raw subrecords including six delta-zero state updates per combatant (raw={document.RawRecordCount}, projected={document.Events.Count})."
        );
        Require(
            document.Entities.Single(entity => entity.EntityId == "socket-effect-a").Type
                == "effect"
                && document.Entities.Single(entity => entity.EntityId == cardId.Value).Type
                    == "item",
            "Socket-effect snapshots in a hand capture must not masquerade as item entities."
        );

        RunEffectValueAttributionChecks(manifest, cardId);
        RunStructuralStatusIconChecks(manifest, cardId);
        RunAttributeProjectionPolicyChecks(manifest);
    }

    private static void RunAttributeProjectionPolicyChecks(PvpBattleManifest sourceManifest)
    {
        var policyType = typeof(CombatReportDocumentV1).Assembly.GetType(
            "BazaarPlusPlus.Game.CombatReplay.ReportData.CombatReportAttributePolicy",
            throwOnError: true
        )!;
        var classifyPlayer = policyType.GetMethod(
            "Classify",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(EPlayerAttributeType)],
            modifiers: null
        )!;
        var classifyCard = policyType.GetMethod(
            "Classify",
            BindingFlags.Static | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(ECardAttributeType)],
            modifiers: null
        )!;
        foreach (var attribute in Enum.GetValues<EPlayerAttributeType>())
        {
            Require(
                classifyPlayer.Invoke(null, [attribute])?.ToString() != "Unknown",
                $"Player attribute {attribute} must have an explicit report classification."
            );
        }
        foreach (var attribute in Enum.GetValues<ECardAttributeType>())
        {
            Require(
                classifyCard.Invoke(null, [attribute])?.ToString() != "Unknown",
                $"Card attribute {attribute} must have an explicit report classification."
            );
        }

        var cardId = new InstanceId("policy-target");
        var extensionTargetId = new InstanceId("policy-extension-target");
        var battleEndTargetId = new InstanceId("policy-battle-end-target");
        CombatSimFrame FrameWith(
            InstanceId targetId,
            params CombatSimCardAttributeUpdate[] attributes
        )
        {
            var frame = new CombatSimFrame();
            frame.CardUpdates[targetId] = new CombatSimCardUpdate
            {
                CardInstanceId = targetId,
                Attributes = attributes.ToDictionary(
                    attribute => attribute.AttributeType,
                    attribute => attribute
                ),
            };
            return frame;
        }

        var document = Project(
            new PvpBattleManifest
            {
                BattleId = "1234567890abcdef1234567890abcdef",
                RecordedAtUtc = sourceManifest.RecordedAtUtc,
            },
            new NetMessageCombatSim(
                new CombatSim
                {
                    Frames =
                    [
                        MergeFrames(
                            FrameWith(
                                cardId,
                                CardAttribute(ECardAttributeType.Cooldown, 5_000, 4_950),
                                CardAttribute(ECardAttributeType.Haste, 0, 2_000),
                                CardAttribute(ECardAttributeType.Slow, 500, 450),
                                CardAttribute(ECardAttributeType.Freeze, 0, 1_000),
                                CardAttribute(ECardAttributeType.DamageAmount, 10, 20),
                                CardAttribute(ECardAttributeType.Custom_0, 0, 1)
                            ),
                            FrameWith(
                                extensionTargetId,
                                CardAttribute(ECardAttributeType.Slow, 0, 100)
                            )
                        ),
                        MergeFrames(
                            FrameWith(
                                cardId,
                                CardAttribute(ECardAttributeType.Haste, 1_950, 3_000),
                                CardAttribute(ECardAttributeType.Slow, 450, 400),
                                CardAttribute(ECardAttributeType.Freeze, 950, 200)
                            ),
                            FrameWith(
                                extensionTargetId,
                                CardAttribute(ECardAttributeType.Slow, 50, 400)
                            )
                        ),
                        MergeFrames(
                            FrameWith(
                                cardId,
                                CardAttribute(ECardAttributeType.Haste, 2_950, 1_200),
                                CardAttribute(ECardAttributeType.Slow, 400, 350)
                            ),
                            FrameWith(
                                battleEndTargetId,
                                CardAttribute(ECardAttributeType.Freeze, 0, 1_000)
                            )
                        ),
                        FrameWith(
                            cardId,
                            CardAttribute(ECardAttributeType.Haste, 1_150, 1_100),
                            CardAttribute(ECardAttributeType.Slow, 350, 300)
                        ),
                        FrameWith(
                            cardId,
                            CardAttribute(ECardAttributeType.Haste, 50, 0),
                            CardAttribute(ECardAttributeType.Slow, 50, 0)
                        ),
                        FrameWith(cardId, CardAttribute(ECardAttributeType.Haste, 0, 500)),
                        MergeFrames(
                            new CombatSimFrame
                            {
                                PlayerUpdates = new CombatSimPlayerUpdate
                                {
                                    Attributes =
                                    {
                                        [EPlayerAttributeType.EnragedDuration] = Attribute(
                                            EPlayerAttributeType.EnragedDuration,
                                            1_000,
                                            950
                                        ),
                                        [EPlayerAttributeType.Tempo] = Attribute(
                                            EPlayerAttributeType.Tempo,
                                            5,
                                            6
                                        ),
                                        [EPlayerAttributeType.CritChance] = Attribute(
                                            EPlayerAttributeType.CritChance,
                                            10,
                                            20
                                        ),
                                        [EPlayerAttributeType.Custom_3] = Attribute(
                                            EPlayerAttributeType.Custom_3,
                                            0,
                                            1
                                        ),
                                    },
                                },
                            },
                            FrameWith(cardId, CardAttribute(ECardAttributeType.Haste, 50, 0))
                        ),
                    ],
                }
            )
        );
        Require(
            document.Events.All(reportEvent => reportEvent.Action != "Cooldown"),
            "Per-frame Cooldown clock ticks must not become report events."
        );
        Require(
            document.Events.All(reportEvent =>
                reportEvent.Kind != "card-attribute"
                || reportEvent.Action is not ("Haste" or "Slow" or "Freeze")
            ),
            "Raw status clock samples must not become discrete card-attribute events."
        );
        var statusRanges = document
            .Events.Where(reportEvent => reportEvent.Kind == "card-status-range")
            .OrderBy(reportEvent => reportEvent.TargetEntityIds.Single(), StringComparer.Ordinal)
            .ThenBy(reportEvent => reportEvent.Action, StringComparer.Ordinal)
            .ThenBy(reportEvent => reportEvent.CombatTimeMs)
            .Select(reportEvent => new
            {
                Target = reportEvent.TargetEntityIds.Single(),
                reportEvent.Action,
                Start = reportEvent.CombatTimeMs,
                Duration = reportEvent.Value,
            })
            .ToList();
        Require(
            statusRanges.Count == 6,
            $"The complete raw status stream must collapse to exactly six active spans instead of per-frame changes (actual={statusRanges.Count})."
        );
        Require(
            statusRanges.Any(range =>
                range.Target == cardId.Value
                && range.Action == "Haste"
                && range.Start == 0
                && range.Duration == 200
            ),
            "A status application followed by extension, reduction, normal ticking, and terminal zero must produce one exact interval."
        );
        Require(
            statusRanges.Any(range =>
                range.Target == cardId.Value
                && range.Action == "Haste"
                && range.Start == 250
                && range.Duration == 50
            ),
            "A second active span must remain a separate compact interval."
        );
        Require(
            statusRanges.Any(range =>
                range.Target == cardId.Value
                && range.Action == "Slow"
                && range.Start == 0
                && range.Duration == 200
            ),
            "A first observed positive-to-positive status must be treated as active from frame zero."
        );
        Require(
            statusRanges.Any(range =>
                range.Target == cardId.Value
                && range.Action == "Freeze"
                && range.Start == 0
                && range.Duration == 250
            ),
            "A positive partial reduction without terminal zero must shorten the inferred interval."
        );
        Require(
            statusRanges.Any(range =>
                range.Target == extensionTargetId.Value
                && range.Action == "Slow"
                && range.Start == 0
                && range.Duration == 300
            ),
            "A positive extension must lengthen the interval and clamp it to the report boundary."
        );
        Require(
            statusRanges.Any(range =>
                range.Target == battleEndTargetId.Value
                && range.Action == "Freeze"
                && range.Start == 100
                && range.Duration == 200
            ),
            "An active status at battle end must clamp to the report boundary."
        );
        Require(
            document.Events.Count(reportEvent => reportEvent.Action == "DamageAmount") == 1,
            "Persistent modifier transitions must remain in the report."
        );
        var playerCritChance = document.Events.Single(reportEvent =>
            reportEvent.Kind == "player-attribute" && reportEvent.Action == "CritChance"
        );
        Require(
            playerCritChance.Unit == "percent"
                && playerCritChance.IconSemanticKey == "status.critChance",
            "Player percentage modifiers must retain their display unit."
        );
        Require(
            document.Events.All(reportEvent =>
                reportEvent.Action is not ("EnragedDuration" or "Tempo")
            ),
            "Unsupported player live-state clocks must fail closed instead of becoming generic status changes."
        );
        Require(
            document.Events.All(reportEvent =>
                !reportEvent.Action.StartsWith("Custom_", StringComparison.Ordinal)
            ),
            "Opaque diagnostic slots must fail closed until the report has a dedicated diagnostic disclosure."
        );
    }

    private static CombatSimFrame MergeFrames(params CombatSimFrame[] frames)
    {
        var merged = new CombatSimFrame();
        foreach (var frame in frames)
        {
            if (frame.PlayerUpdates != null)
                merged.PlayerUpdates = frame.PlayerUpdates;
            if (frame.OpponentUpdates != null)
                merged.OpponentUpdates = frame.OpponentUpdates;
            foreach (var pair in frame.CardUpdates)
                merged.CardUpdates[pair.Key] = pair.Value;
            merged.Events.AddRange(frame.Events);
        }
        return merged;
    }

    private static void RunStructuralStatusIconChecks(
        PvpBattleManifest sourceManifest,
        InstanceId sourceCardId
    )
    {
        var targetCardId = new InstanceId("structural-target");
        var frame = new CombatSimFrame();
        frame.Events.Add(
            Executed(
                EActionCommandType.CardDisable,
                sourceCardId,
                new EffectTargetCard { Target = targetCardId },
                "destroy"
            )
        );
        frame.CardUpdates[targetCardId] = new CombatSimCardUpdate
        {
            CardInstanceId = targetCardId,
            Attributes =
            {
                [ECardAttributeType.Ammo] = CardAttribute(ECardAttributeType.Ammo, 2, 1),
                [ECardAttributeType.CritChance] = CardAttribute(
                    ECardAttributeType.CritChance,
                    0,
                    5
                ),
                [ECardAttributeType.DamageAmount] = CardAttribute(
                    ECardAttributeType.DamageAmount,
                    10,
                    30
                ),
                [ECardAttributeType.Multicast] = CardAttribute(ECardAttributeType.Multicast, 2, 1),
                [ECardAttributeType.Lifesteal] = CardAttribute(
                    ECardAttributeType.Lifesteal,
                    0,
                    100
                ),
                [ECardAttributeType.PercentCooldownReduction] = CardAttribute(
                    ECardAttributeType.PercentCooldownReduction,
                    0,
                    10
                ),
            },
        };

        var document = Project(
            new PvpBattleManifest
            {
                BattleId = "00112233445566778899aabbccddeeff",
                RecordedAtUtc = sourceManifest.RecordedAtUtc,
            },
            new NetMessageCombatSim(new CombatSim { Frames = new List<CombatSimFrame> { frame } })
        );
        Require(
            document
                .Events.Single(reportEvent => reportEvent.Action == "CardDisable")
                .IconSemanticKey == "status.destroy",
            "CardDisable must retain the game's native destroy/disable icon semantic."
        );
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Ammo"] = "status.ammo",
            ["CritChance"] = "status.critChance",
            ["DamageAmount"] = "status.damage",
            ["Multicast"] = "status.multicast",
            ["PercentCooldownReduction"] = "status.cooldownReduction",
        };
        foreach (var pair in expected)
        {
            Require(
                document
                    .Events.Single(reportEvent =>
                        reportEvent.Kind == "card-attribute" && reportEvent.Action == pair.Key
                    )
                    .IconSemanticKey == pair.Value,
                $"{pair.Key} must retain its native tooltip icon semantic."
            );
        }
        Require(
            document
                .Events.Single(reportEvent =>
                    reportEvent.Kind == "card-attribute" && reportEvent.Action == "Lifesteal"
                )
                .Unit == "percent"
                && document
                    .Events.Single(reportEvent =>
                        reportEvent.Kind == "card-attribute"
                        && reportEvent.Action == "PercentCooldownReduction"
                    )
                    .Unit == "percent",
            "Card percentage modifiers must retain their display unit."
        );
    }

    private static void RunEffectValueAttributionChecks(
        PvpBattleManifest sourceManifest,
        InstanceId sourceCardId
    )
    {
        var targetCardId = new InstanceId("target-card");
        var quantified = new CombatSimFrame
        {
            PlayerUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [EPlayerAttributeType.HealthRegen] = Attribute(
                        EPlayerAttributeType.HealthRegen,
                        0,
                        7
                    ),
                },
            },
            OpponentUpdates = new CombatSimPlayerUpdate
            {
                HealthAdjustments =
                {
                    new CombatSimPlayerHealthAdjustment
                    {
                        AttributeChanged = EPlayerHealthChangeType.Shield,
                        DamageType = EDamageType.Damage,
                        Amount = -10,
                        IsCrit = true,
                    },
                    new CombatSimPlayerHealthAdjustment
                    {
                        AttributeChanged = EPlayerHealthChangeType.Health,
                        DamageType = EDamageType.Damage,
                        Amount = -25,
                        IsCrit = true,
                    },
                },
                Attributes =
                {
                    [EPlayerAttributeType.Burn] = Attribute(EPlayerAttributeType.Burn, 0, 12),
                },
            },
        };
        quantified.Events.Add(
            Executed(
                EActionCommandType.PlayerDamage,
                sourceCardId,
                new EffectTargetPlayer { Target = ECombatantId.Opponent },
                "damage"
            )
        );
        quantified.Events.Add(
            Executed(
                EActionCommandType.PlayerBurnApply,
                sourceCardId,
                new EffectTargetPlayer { Target = ECombatantId.Opponent },
                "burn"
            )
        );
        quantified.Events.Add(
            Executed(
                EActionCommandType.PlayerRegenApply,
                sourceCardId,
                new EffectTargetPlayer { Target = ECombatantId.Player },
                "regen"
            )
        );
        quantified.Events.Add(
            Executed(
                EActionCommandType.CardSlow,
                sourceCardId,
                new EffectTargetCard { Target = targetCardId },
                "slow"
            )
        );
        quantified.Events.Add(
            Executed(
                EActionCommandType.CardCharge,
                sourceCardId,
                new EffectTargetCard { Target = targetCardId },
                "charge"
            )
        );
        quantified.CardUpdates[targetCardId] = new CombatSimCardUpdate
        {
            CardInstanceId = targetCardId,
            Attributes =
            {
                [ECardAttributeType.Slow] = CardAttribute(ECardAttributeType.Slow, 0, 1_950),
            },
        };

        var ambiguous = new CombatSimFrame
        {
            PlayerUpdates = new CombatSimPlayerUpdate
            {
                HealthAdjustments =
                {
                    new CombatSimPlayerHealthAdjustment
                    {
                        AttributeChanged = EPlayerHealthChangeType.Shield,
                        DamageType = EDamageType.Shield,
                        Amount = 10,
                        IsCrit = true,
                    },
                    new CombatSimPlayerHealthAdjustment
                    {
                        AttributeChanged = EPlayerHealthChangeType.Shield,
                        DamageType = EDamageType.Shield,
                        Amount = 20,
                    },
                },
            },
            OpponentUpdates = new CombatSimPlayerUpdate
            {
                Attributes =
                {
                    [EPlayerAttributeType.Burn] = Attribute(EPlayerAttributeType.Burn, 12, 32),
                },
            },
        };
        ambiguous.Events.Add(
            Executed(
                EActionCommandType.PlayerBurnApply,
                sourceCardId,
                new EffectTargetPlayer { Target = ECombatantId.Opponent },
                "ambiguous-a"
            )
        );
        ambiguous.Events.Add(
            Executed(
                EActionCommandType.PlayerBurnApply,
                new InstanceId("other-source"),
                new EffectTargetPlayer { Target = ECombatantId.Opponent },
                "ambiguous-b"
            )
        );
        ambiguous.Events.Add(
            Executed(
                EActionCommandType.PlayerShieldApply,
                sourceCardId,
                new EffectTargetPlayer { Target = ECombatantId.Player },
                "ambiguous-shield-a"
            )
        );
        ambiguous.Events.Add(
            Executed(
                EActionCommandType.PlayerShieldApply,
                new InstanceId("other-source"),
                new EffectTargetPlayer { Target = ECombatantId.Player },
                "ambiguous-shield-b"
            )
        );

        var ambientDecay = new CombatSimFrame();
        ambientDecay.Events.Add(
            Executed(
                EActionCommandType.CardSlow,
                sourceCardId,
                new EffectTargetCard { Target = targetCardId },
                "negative-status"
            )
        );
        ambientDecay.CardUpdates[targetCardId] = new CombatSimCardUpdate
        {
            CardInstanceId = targetCardId,
            Attributes =
            {
                [ECardAttributeType.Slow] = CardAttribute(ECardAttributeType.Slow, 1_950, 1_900),
            },
        };

        var manifest = new PvpBattleManifest
        {
            BattleId = "fedcba9876543210fedcba9876543210",
            RecordedAtUtc = sourceManifest.RecordedAtUtc,
        };
        var document = Project(
            manifest,
            new NetMessageCombatSim(
                new CombatSim
                {
                    Frames = new List<CombatSimFrame> { quantified, ambiguous, ambientDecay },
                }
            )
        );
        var executedEvents = document
            .Events.Where(reportEvent => reportEvent.Kind == "effect-executed")
            .ToList();

        var damage = executedEvents.Single(reportEvent => reportEvent.EffectId == "damage");
        Require(
            damage.Value == 35 && damage.IsCritical == true,
            "A unique direct-damage execution must receive the summed critical shield and health damage observed on its target."
        );
        Require(
            executedEvents.Single(reportEvent => reportEvent.EffectId == "burn").Value == 12,
            "A unique burn application must receive the target's positive Burn transition."
        );
        Require(
            executedEvents.Single(reportEvent => reportEvent.EffectId == "regen").Value == 7,
            "A unique regen application must receive the target's positive HealthRegen transition."
        );
        var slow = executedEvents.Single(reportEvent => reportEvent.EffectId == "slow");
        Require(
            slow.Value == 1_950 && slow.Unit == "ms",
            "A unique card status application must report the observable positive net duration in milliseconds."
        );
        Require(
            executedEvents.Single(reportEvent => reportEvent.EffectId == "charge").Value == null,
            "Charge must remain count-only while the raw frame does not expose an unambiguous applied amount."
        );
        Require(
            executedEvents
                .Where(reportEvent =>
                    reportEvent.EffectId?.StartsWith("ambiguous-", StringComparison.Ordinal) == true
                )
                .All(reportEvent => reportEvent.Value == null),
            "A shared aggregate transition must not be split or copied across same-target executions."
        );
        Require(
            executedEvents
                .Where(reportEvent =>
                    reportEvent.EffectId?.StartsWith("ambiguous-shield-", StringComparison.Ordinal)
                    == true
                )
                .All(reportEvent => reportEvent.Value == null && reportEvent.IsCritical == null),
            "A critical outcome must not be copied across ambiguous same-target executions."
        );
        Require(
            executedEvents.Single(reportEvent => reportEvent.EffectId == "negative-status").Value
                == null,
            "Ambient negative status decay must never be reported as an applied amount."
        );
    }

    private static CombatSimEventEffectExecuted Executed(
        EActionCommandType action,
        InstanceId source,
        IEffectTarget target,
        string effectId
    ) =>
        new()
        {
            ExecutionContextId = "context-" + effectId,
            EffectId = effectId,
            ActionType = action,
            Source = source,
            TriggerSource = source,
            Target = target,
        };

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
