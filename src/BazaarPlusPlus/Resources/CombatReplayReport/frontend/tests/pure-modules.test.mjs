import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import {
  beginLogicalDraw,
  canvasBackingScale,
} from "../src/timeline/canvas.ts";
import {
  combatMsAtPointer,
  shouldDrawTimelinePreview,
  TIMELINE_LEFT_GUTTER,
  TIMELINE_RIGHT_INTERACTION_GUTTER,
  timelineRatioAtX,
  timelineUsableWidth,
  timelineXAtCombatMs,
} from "../src/timeline/geometry.ts";
import {
  MAX_TIMELINE_WIDTH,
  MIN_TIMELINE_WIDTH,
  TIMELINE_BASE_DENSITY,
  timelineWidthAtZoom,
} from "../src/timeline/constants.ts";
import {
  mapCombatToMedia,
  mapMediaToCombat,
  normalizeAnchors,
  normalizeSyncState,
} from "../src/recording/sync.ts";
import {
  formatCompactNumber,
  formatDuration,
  formatMilliseconds,
  formatTimecode,
  orderAxisLabel,
  signedOrder,
} from "../src/i18n/format.ts";
import { COPY } from "../src/i18n/catalog.ts";
import {
  asArray,
  asFiniteNumber,
  asString,
  isRecord,
  pick,
} from "../src/model/value.ts";
import {
  niceLinearTicks,
  sharedStateDomain,
  sharedStateMappedY,
  stateAxisTicks,
  stateScaleValue,
} from "../src/timeline/state-scale.ts";
import {
  buildClusters,
  buildStatusRanges,
  buildVisualClusters,
  createHitIndex,
  eventLaneEndpoints,
  eventKindToken,
  isVisibleTimelineEvent,
  relatedLaneRoles,
  timelineEventToken,
  timelineClusterEventIds,
} from "../src/timeline/clusters.ts";
import {
  markerImageBounds,
  markerPoint,
} from "../src/timeline/event-drawing.ts";
import { layoutTimelineMarkers } from "../src/timeline/marker-layout.ts";
import { hitTestTimelineClusters } from "../src/timeline/event-interaction.ts";
import {
  safeAssetUrl,
  safeVideoUrl,
} from "../src/model/asset-paths.ts";
import {
  eventTimeMs,
  frameZeroMetricSamples,
  normalizeEntity,
  normalizeEvent,
  normalizeMetric,
  normalizeMetricName,
  normalizeSide,
} from "../src/model/normalize.ts";
import {
  buildViewModel,
  decodeEnvelope,
  ReportError,
} from "../src/model/report.ts";
import {
  entityArtDimensions,
  maximumEntityArtWidth,
  normalizedEntityArtSpan,
} from "../src/components/semantic/entity-art-geometry.ts";
import {
  DEFAULT_LANE_VISIBILITY,
  filterTimelineEntities,
  opponentBoundaryLane,
} from "../src/timeline/lane-filter.ts";
import {
  buildEntityActivity,
  buildStatistics,
} from "../src/statistics/aggregate.ts";
import {
  damageKindFromType,
  eventDamageKind,
} from "../src/model/damage-semantics.ts";
import { eventPresentation } from "../src/model/event-semantics.ts";
import {
  mergeInspectorEvents,
  summarizeDirectDamageGroup,
} from "../src/components/inspector/frame-event-groups.ts";
import { attributeEventDiff } from "../src/components/inspector/event-diff.ts";
import {
  buildCombatLogEntries,
  combatLogEventToken,
  combatLogHealthSettlementEvents,
  combatLogTargetDetails,
  groupCombatLogEntries,
  isCombatLogHealthSettlementEvent,
  isDirectStatusApplicationEvent,
  isNarrativeCombatLogEntry,
  nearestCombatLogEntryIndex,
  selectedCombatLogEntryIndex,
} from "../src/combat-log/entries.ts";

function timelineEvent(overrides = {}) {
  return {
    id: "event",
    frame: 1,
    sequence: 0,
    combatMs: 1_000,
    kind: "status",
    action: "",
    value: null,
    previousValue: null,
    currentValue: null,
    unit: "",
    sourceId: "",
    triggerSourceId: "",
    targetIds: [],
    removedTargetIds: [],
    role: "",
    attributionConfidence: "unknown",
    iconSemanticKey: "",
    icon: "",
    occurrences: 1,
    ...overrides,
  };
}

test("damage semantics distinguish direct, burn, poison, and other outcomes", () => {
  assert.equal(damageKindFromType("Damage"), "direct");
  assert.equal(damageKindFromType("Crit"), "direct");
  assert.equal(damageKindFromType("Burn"), "burn");
  assert.equal(damageKindFromType("Poison"), "poison");
  assert.equal(damageKindFromType("UnknownDamageType"), "other");
  assert.equal(
    eventDamageKind(
      timelineEvent({ kind: "effect-executed", action: "PlayerDamage" }),
    ),
    "direct",
  );
  assert.equal(
    eventDamageKind(
      timelineEvent({ kind: "effect-executed", action: "PlayerBurnApply" }),
    ),
    "burn",
  );
  assert.equal(
    eventDamageKind(
      timelineEvent({ kind: "effect-executed", action: "PlayerPoisonApply" }),
    ),
    "poison",
  );
  assert.equal(
    eventDamageKind(timelineEvent({ kind: "health", action: "Health:Burn" })),
    "burn",
  );
  assert.equal(
    eventDamageKind(timelineEvent({ kind: "status", action: "Damage" })),
    null,
  );
  assert.equal(
    timelineEventToken(
      timelineEvent({ kind: "effect-executed", action: "PlayerDamage" }),
    ),
    "damageDirect",
  );
  assert.equal(
    timelineEventToken(
      timelineEvent({ kind: "effect-executed", action: "PlayerBurnApply" }),
    ),
    "burn",
  );
  assert.equal(
    timelineEventToken(
      timelineEvent({ kind: "effect-executed", action: "PlayerPoisonApply" }),
    ),
    "poison",
  );

  const entity = (id, side) => ({
    id,
    name: id,
    type: "hero",
    side,
    span: 1,
    asset: "",
    hiddenFromTimeline: false,
  });
  const statistics = buildStatistics({
    entities: [
      entity("player", "player"),
      entity("opponent", "opponent"),
    ],
    events: [
      timelineEvent({
        id: "direct",
        kind: "health",
        action: "Health:Damage",
        targetIds: ["opponent"],
        value: -100,
      }),
      timelineEvent({
        id: "burn",
        kind: "health",
        action: "Health:Burn",
        targetIds: ["opponent"],
        value: -30,
      }),
      timelineEvent({
        id: "poison",
        kind: "health",
        action: "Health:Poison",
        targetIds: ["opponent"],
        value: -20,
      }),
      timelineEvent({
        id: "other",
        kind: "health",
        action: "Health:Reflect",
        targetIds: ["opponent"],
        value: -5,
      }),
      timelineEvent({
        id: "opponent-direct",
        kind: "health",
        action: "Health:Damage",
        targetIds: ["player"],
        value: -70,
      }),
    ],
  });
  assert.deepEqual(statistics.damageTypes.opponent, {
    direct: 100,
    burn: 30,
    poison: 20,
    other: 5,
  });
  assert.deepEqual(statistics.damageTypes.player, {
    direct: 70,
    burn: 0,
    poison: 0,
    other: 0,
  });
  assert.equal(statistics.damageDealt.player, 155);
  assert.equal(statistics.damageDealt.opponent, 70);
});

test("combat log merges only identical events from the same frame", () => {
  const base = timelineEvent({
    frame: 20,
    combatMs: 1_000,
    kind: "effect-executed",
    action: "CardHaste",
    sourceId: "source",
    targetIds: ["target"],
    value: 2_000,
    unit: "milliseconds",
    iconSemanticKey: "haste",
    icon: "../report-assets/status-haste.png",
  });
  const entries = buildCombatLogEntries([
    { ...base, id: "same-a", occurrences: 2 },
    { ...base, id: "same-b", occurrences: 3 },
    {
      ...base,
      id: "next-frame",
      frame: 21,
      combatMs: 1_050,
    },
    {
      ...base,
      id: "different-target",
      targetIds: ["other-target"],
    },
  ]);

  assert.equal(entries.length, 3);
  const mergedEntry = entries.find((entry) =>
    entry.eventIds.includes("same-a")
  );
  assert.ok(mergedEntry);
  assert.equal(mergedEntry.count, 5);
  assert.deepEqual(mergedEntry.eventIds, ["same-a", "same-b"]);
  assert.equal(mergedEntry.iconSemanticKey, "haste");
  assert.equal(mergedEntry.icon, "../report-assets/status-haste.png");
  assert.equal(entries.filter((entry) => entry.frame === 20).length, 2);
  assert.equal(entries.filter((entry) => entry.frame === 21).length, 1);
});

test("combat log preserves burn, poison, and regeneration applications alongside their health settlements", () => {
  const events = [
    timelineEvent({
      id: "burn-application",
      frame: 80,
      combatMs: 4_000,
      kind: "effect-executed",
      action: "PlayerBurnApply",
      sourceId: "trail-mix",
      targetIds: ["opponent"],
      value: 16,
      iconSemanticKey: "burn",
      icon: "../report-assets/status-burn.png",
    }),
    timelineEvent({
      id: "poison-application",
      frame: 80,
      combatMs: 4_000,
      kind: "effect-executed",
      action: "PlayerPoisonApply",
      sourceId: "trail-mix",
      targetIds: ["opponent"],
      value: 9,
      iconSemanticKey: "poison",
      icon: "../report-assets/status-poison.png",
    }),
    timelineEvent({
      id: "regen-application",
      frame: 80,
      combatMs: 4_000,
      kind: "effect-executed",
      action: "PlayerRegenApply",
      sourceId: "trail-mix",
      targetIds: ["player"],
      value: 7,
      iconSemanticKey: "regen",
      icon: "../report-assets/status-regen.png",
    }),
    timelineEvent({
      id: "burn-settlement",
      frame: 81,
      combatMs: 4_050,
      kind: "health",
      action: "Health:Burn",
      targetIds: ["opponent"],
      value: -16,
    }),
    timelineEvent({
      id: "regen-settlement",
      frame: 81,
      combatMs: 4_050,
      kind: "health",
      action: "Health:Regen",
      targetIds: ["player"],
      value: 12,
    }),
  ];

  assert.equal(combatLogEventToken(events[0]), "burn");
  assert.equal(combatLogEventToken(events[1]), "poison");
  assert.equal(combatLogEventToken(events[2]), "regen");
  assert.equal(combatLogEventToken(events[3]), "damage");
  assert.equal(combatLogEventToken(events[4]), "heal");
  assert.equal(isCombatLogHealthSettlementEvent(events[0]), false);
  assert.equal(isCombatLogHealthSettlementEvent(events[1]), false);
  assert.equal(isCombatLogHealthSettlementEvent(events[2]), false);
  assert.equal(isCombatLogHealthSettlementEvent(events[3]), true);
  assert.equal(isCombatLogHealthSettlementEvent(events[4]), true);
  assert.equal(
    isCombatLogHealthSettlementEvent(
      timelineEvent({ kind: "health", action: "Health:Damage" }),
    ),
    false,
  );

  const entries = buildCombatLogEntries(events);
  assert.deepEqual(
    Object.fromEntries(entries.map((entry) => [entry.action, entry.token])),
    {
      PlayerBurnApply: "burn",
      PlayerPoisonApply: "poison",
      PlayerRegenApply: "regen",
      "Health:Burn": "damage",
      "Health:Regen": "heal",
    },
  );
  assert.equal(
    entries.find((entry) => entry.action === "PlayerBurnApply")?.icon,
    "../report-assets/status-burn.png",
  );
  assert.equal(
    entries.find((entry) => entry.action === "PlayerPoisonApply")?.icon,
    "../report-assets/status-poison.png",
  );
  assert.equal(
    entries.find((entry) => entry.action === "PlayerRegenApply")?.icon,
    "../report-assets/status-regen.png",
  );
});

test("combat log uses signed Health attributes only when no explicit adjustment exists", () => {
  const attributeOnlyHeal = timelineEvent({
    id: "attribute-only-heal",
    frame: 126,
    combatMs: 6_300,
    kind: "player-attribute",
    action: "Health",
    targetIds: ["player"],
    value: 20,
    previousValue: 2_222,
    currentValue: 2_242,
    iconSemanticKey: "status.heal",
    icon: "../report-assets/objects/11/1111111111111111111111111111111111111111111111111111111111111111.png",
  });
  const attributeOnlyDamage = timelineEvent({
    id: "attribute-only-damage",
    frame: 127,
    combatMs: 6_350,
    kind: "player-attribute",
    action: "Health",
    targetIds: ["opponent"],
    value: -25,
  });
  const explicitRegen = timelineEvent({
    id: "explicit-regen",
    frame: 128,
    combatMs: 6_400,
    kind: "health",
    action: "Health:Regen",
    targetIds: ["player"],
    value: 12,
  });
  const duplicateAggregate = timelineEvent({
    id: "duplicate-aggregate",
    frame: 128,
    combatMs: 6_400,
    kind: "player-attribute",
    action: "Health",
    targetIds: ["player"],
    value: 12,
  });
  const healthMaxIncrease = timelineEvent({
    id: "health-max-increase",
    frame: 126,
    combatMs: 6_300,
    kind: "player-attribute",
    action: "HealthMax",
    targetIds: ["player"],
    value: 20,
  });

  assert.equal(combatLogEventToken(attributeOnlyHeal), "heal");
  assert.equal(combatLogEventToken(attributeOnlyDamage), "damage");
  assert.equal(combatLogEventToken(healthMaxIncrease), "status");
  assert.equal(
    isNarrativeCombatLogEntry(
      buildCombatLogEntries([healthMaxIncrease])[0],
    ),
    false,
  );
  assert.equal(
    isCombatLogHealthSettlementEvent(attributeOnlyHeal),
    true,
  );
  assert.equal(
    isCombatLogHealthSettlementEvent(
      timelineEvent({
        kind: "player-attribute",
        action: "Health",
        value: 0,
      }),
    ),
    false,
  );

  const selected = combatLogHealthSettlementEvents([
    attributeOnlyHeal,
    attributeOnlyDamage,
    explicitRegen,
    duplicateAggregate,
  ]);
  assert.deepEqual(
    selected.map((event) => event.id),
    ["attribute-only-heal", "attribute-only-damage", "explicit-regen"],
  );

  const entries = buildCombatLogEntries(selected);
  assert.equal(
    entries.find((entry) => entry.eventIds.includes("attribute-only-heal"))
      ?.icon,
    attributeOnlyHeal.icon,
  );
});

test("combat log narrative excludes generic setup and attribute noise", () => {
  assert.equal(
    isNarrativeCombatLogEntry({ action: "PlayerDamage", token: "damage" }),
    true,
  );
  assert.equal(
    isNarrativeCombatLogEntry({ action: "Died", token: "status" }),
    true,
  );
  assert.equal(
    isNarrativeCombatLogEntry({
      action: "PlayerModifyAttribute",
      token: "status",
    }),
    false,
  );
  assert.equal(
    isNarrativeCombatLogEntry({ action: "FlyingStart", token: "status" }),
    false,
  );
});

test("combat log keeps direct freeze applications without exposing freeze countdown ticks", () => {
  const directApplications = [
    timelineEvent({
      id: "freeze-a",
      frame: 169,
      combatMs: 8_450,
      kind: "effect-executed",
      action: "CardFreeze",
      sourceId: "petrifying-gaze",
      triggerSourceId: "regal-blade",
      targetIds: ["dog"],
      value: 1_000,
      unit: "ms",
    }),
    timelineEvent({
      id: "freeze-b",
      frame: 169,
      combatMs: 8_450,
      kind: "effect-executed",
      action: "CardFreeze",
      sourceId: "petrifying-gaze",
      triggerSourceId: "regal-blade",
      targetIds: ["cash-cannon"],
      value: 1_000,
      unit: "ms",
    }),
  ];
  const countdownTick = timelineEvent({
    id: "freeze-tick",
    frame: 170,
    combatMs: 8_500,
    kind: "card-attribute",
    action: "Freeze",
    targetIds: ["dog"],
    value: -50,
    previousValue: 1_000,
    currentValue: 950,
    unit: "ms",
  });

  assert.equal(
    directApplications.every(isDirectStatusApplicationEvent),
    true,
  );
  assert.equal(isDirectStatusApplicationEvent(countdownTick), false);

  const entries = groupCombatLogEntries(
    buildCombatLogEntries(
      [...directApplications, countdownTick].filter(
        isDirectStatusApplicationEvent,
      ),
    ).filter(isNarrativeCombatLogEntry),
  );
  assert.equal(entries.length, 1);
  assert.equal(entries[0].action, "CardFreeze");
  assert.equal(entries[0].token, "freeze");
  assert.equal(entries[0].count, 2);
  assert.deepEqual(entries[0].targetIds, ["dog", "cash-cannon"]);
  assert.deepEqual(entries[0].eventIds, ["freeze-a", "freeze-b"]);
});

test("combat log grouping never merges distinct actions", () => {
  const entries = buildCombatLogEntries([
    timelineEvent({
      id: "first",
      frame: 20,
      kind: "effect-executed",
      action: "PlayerDamage",
      sourceId: "source",
      targetIds: ["target"],
      value: 10,
      unit: "points",
    }),
    timelineEvent({
      id: "second",
      frame: 20,
      kind: "effect-executed",
      action: "PlayerBurnApply",
      sourceId: "source",
      targetIds: ["target"],
      value: 10,
      unit: "points",
    }),
  ]);

  assert.equal(groupCombatLogEntries(entries).length, 2);
});

test("combat log target details aggregate overlapping targets without duplicates", () => {
  const base = {
    frame: 80,
    combatMs: 4_000,
    action: "CardSlow",
    token: "slow",
    iconSemanticKey: "",
    icon: "",
    sourceId: "boar-roast",
    triggerSourceId: "",
    value: 2_000,
    unit: "ms",
  };
  const details = combatLogTargetDetails([
    {
      ...base,
      id: "first",
      targetIds: ["boar-roast", "cash-cannon"],
      eventIds: ["first-event"],
      count: 1,
    },
    {
      ...base,
      id: "second",
      targetIds: ["boar-roast", "regal-blade", "regal-blade"],
      eventIds: ["second-event"],
      count: 2,
    },
  ]);

  assert.deepEqual(
    details.map((detail) => detail.targetIds[0]),
    ["boar-roast", "cash-cannon", "regal-blade"],
  );
  assert.deepEqual(
    details.map((detail) => detail.count),
    [3, 1, 2],
  );
  assert.deepEqual(details[0].eventIds, ["first-event", "second-event"]);
});

test("combat log nearest entry lookup is stable at bounds and ties", () => {
  const entries = [0, 1_000, 2_000].map((combatMs, index) => ({
    id: String(index),
    frame: index,
    combatMs,
    action: "",
    token: "status",
    sourceId: "",
    triggerSourceId: "",
    targetIds: [],
    eventIds: [],
    value: null,
    unit: "",
    count: 1,
  }));

  assert.equal(nearestCombatLogEntryIndex([], 1_000), -1);
  assert.equal(nearestCombatLogEntryIndex(entries, -100), 0);
  assert.equal(nearestCombatLogEntryIndex(entries, 500), 0);
  assert.equal(nearestCombatLogEntryIndex(entries, 1_600), 2);
  assert.equal(nearestCombatLogEntryIndex(entries, 3_000), 2);
});

test("combat log selection prefers the clicked event within a shared frame", () => {
  const entries = [
    {
      id: "first",
      frame: 20,
      combatMs: 1_000,
      action: "PlayerDamage",
      token: "damage",
      sourceId: "source-a",
      triggerSourceId: "",
      targetIds: ["target"],
      eventIds: ["event-a"],
      value: 10,
      unit: "points",
      count: 1,
    },
    {
      id: "second",
      frame: 20,
      combatMs: 1_000,
      action: "PlayerHeal",
      token: "healing",
      sourceId: "source-b",
      triggerSourceId: "",
      targetIds: ["target"],
      eventIds: ["event-b"],
      value: 5,
      unit: "points",
      count: 1,
    },
  ];

  assert.equal(selectedCombatLogEntryIndex(entries, ["event-b"], 1_000), 1);
  assert.equal(selectedCombatLogEntryIndex(entries, [], 1_000), 0);
});

test("inspector merge identity keeps unit, semantic icon, and attribution distinct", () => {
  const base = timelineEvent({
    frame: 12,
    combatMs: 600,
    kind: "effect-executed",
    action: "CardHaste",
    value: 2,
    unit: "seconds",
    sourceId: "source",
    triggerSourceId: "trigger",
    targetIds: ["target-a"],
    iconSemanticKey: "haste",
    attributionConfidence: "exact",
  });
  const events = [
    { ...base, id: "same-a" },
    { ...base, id: "same-b", targetIds: ["target-b"] },
    { ...base, id: "different-unit", unit: "milliseconds" },
    { ...base, id: "different-icon", iconSemanticKey: "slow" },
    {
      ...base,
      id: "different-attribution",
      attributionConfidence: "inferred",
    },
  ];

  const merged = mergeInspectorEvents(events);

  assert.equal(merged.length, 4);
  assert.equal(merged[0].count, 2);
  assert.deepEqual(merged[0].targetIds, ["target-a", "target-b"]);
  assert.deepEqual(
    merged.slice(1).map((group) => group.event.id),
    ["different-unit", "different-icon", "different-attribution"],
  );
});

test("inspector summarizes provable direct damage without guessing per-source values", () => {
  const silverStake = timelineEvent({
    id: "silver-stake",
    frame: 364,
    sequence: 1,
    kind: "effect-executed",
    action: "PlayerDamage",
    sourceId: "silver-stake-item",
    triggerSourceId: "silver-stake-item",
    targetIds: ["player-hero"],
  });
  const wolf = timelineEvent({
    id: "wolf",
    frame: 364,
    sequence: 3,
    kind: "effect-executed",
    action: "PlayerDamage",
    sourceId: "wolf-item",
    triggerSourceId: "wolf-item",
    targetIds: ["player-hero"],
  });
  const frameEvents = [
    silverStake,
    wolf,
    timelineEvent({
      id: "shield-damage-a",
      frame: 364,
      sequence: 8,
      kind: "health",
      action: "Shield:Damage",
      targetIds: ["player-hero"],
      value: -740,
    }),
    timelineEvent({
      id: "shield-damage-b",
      frame: 364,
      sequence: 9,
      kind: "health",
      action: "Shield:Damage",
      targetIds: ["player-hero"],
      value: -690,
    }),
    timelineEvent({
      id: "aggregate-shield-metric",
      frame: 364,
      sequence: 10,
      kind: "player-attribute",
      action: "Shield",
      targetIds: ["player-hero"],
      value: -1_430,
    }),
    timelineEvent({
      id: "other-target-damage",
      frame: 364,
      sequence: 11,
      kind: "health",
      action: "Health:Damage",
      targetIds: ["opponent-hero"],
      value: -999,
    }),
  ];

  const summary = summarizeDirectDamageGroup(
    [silverStake, wolf],
    frameEvents,
    "player-hero",
  );
  assert.deepEqual(summary, {
    amount: 1_430,
    settlementEventIds: ["shield-damage-a", "shield-damage-b"],
  });
  assert.equal(
    summarizeDirectDamageGroup(
      [silverStake],
      frameEvents,
      "player-hero",
    ),
    null,
  );
  assert.equal(
    summarizeDirectDamageGroup(
      [
        silverStake,
        {
          ...wolf,
          sourceId: silverStake.sourceId,
          triggerSourceId: silverStake.triggerSourceId,
        },
      ],
      frameEvents,
      "player-hero",
    ),
    null,
  );
});

test("timeline geometry reserves both interaction gutters reversibly", () => {
  const width = 1_000;
  assert.equal(
    timelineUsableWidth(width),
    width - TIMELINE_LEFT_GUTTER - TIMELINE_RIGHT_INTERACTION_GUTTER,
  );
  assert.equal(timelineXAtCombatMs(0, 10_000, width), TIMELINE_LEFT_GUTTER);
  assert.equal(
    timelineXAtCombatMs(10_000, 10_000, width),
    width - TIMELINE_RIGHT_INTERACTION_GUTTER,
  );
  const midpoint = timelineXAtCombatMs(5_000, 10_000, width);
  assert.equal(timelineRatioAtX(midpoint, width), 0.5);

  const canvas = {
    width: 2_000,
    style: { width: "1000px" },
    getBoundingClientRect: () => ({ left: 100, width: 500 }),
  };
  const clientX = 100 + midpoint / 2;
  assert.equal(combatMsAtPointer(canvas, clientX, 10_000), 5_000);
});

test("timeline 100% uses the doubled review-density baseline", () => {
  assert.equal(TIMELINE_BASE_DENSITY, 2);
  assert.equal(
    timelineWidthAtZoom(MIN_TIMELINE_WIDTH, 1),
    MIN_TIMELINE_WIDTH * 2,
  );
  assert.equal(
    timelineWidthAtZoom(MIN_TIMELINE_WIDTH, 0.5),
    MIN_TIMELINE_WIDTH,
  );
  assert.equal(
    timelineWidthAtZoom(MAX_TIMELINE_WIDTH, 4),
    MAX_TIMELINE_WIDTH,
  );
});

test("timeline preview stays virtual and yields when it overlaps the pinned frame", () => {
  const width = 1_000;
  assert.equal(
    shouldDrawTimelinePreview(2_000, null, 10_000, width),
    false,
  );
  assert.equal(
    shouldDrawTimelinePreview(2_000, 2_000, 10_000, width),
    false,
  );
  assert.equal(
    shouldDrawTimelinePreview(2_000, 2_010, 10_000, width),
    false,
  );
  assert.equal(
    shouldDrawTimelinePreview(2_000, 7_000, 10_000, width),
    true,
  );
});

test("lane filters preserve order and move the opponent boundary with visible lanes", () => {
  const entities = [
    normalizeEntity(
      { entityId: "player-hero", owner: "player", type: "hero" },
      0,
    ),
    normalizeEntity(
      { entityId: "player-item", owner: "player", type: "item" },
      1,
    ),
    normalizeEntity(
      { entityId: "player-skill", owner: "player", type: "skill" },
      2,
    ),
    normalizeEntity(
      { entityId: "opponent-hero", owner: "opponent", type: "hero" },
      3,
    ),
    normalizeEntity(
      { entityId: "opponent-item", owner: "opponent", type: "item" },
      4,
    ),
  ];
  assert.equal(opponentBoundaryLane(entities), 3);

  const withoutSkills = filterTimelineEntities(entities, {
    ...DEFAULT_LANE_VISIBILITY,
    skill: false,
  });
  assert.deepEqual(
    withoutSkills.map((entity) => entity.id),
    [
      "player-hero",
      "player-item",
      "opponent-hero",
      "opponent-item",
    ],
  );
  assert.equal(opponentBoundaryLane(withoutSkills), 2);

  const opponentOnly = filterTimelineEntities(entities, {
    ...DEFAULT_LANE_VISIBILITY,
    player: false,
  });
  assert.deepEqual(
    opponentOnly.map((entity) => entity.id),
    ["opponent-hero", "opponent-item"],
  );
  assert.equal(opponentBoundaryLane(opponentOnly), null);
});

test("entity activity falls back to an exact trigger source without overriding a valid direct source", () => {
  const directItem = normalizeEntity(
    {
      entityId: "direct-item",
      owner: "player",
      type: "item",
      name: "Direct Item",
    },
    0,
  );
  const triggerSkill = normalizeEntity(
    {
      entityId: "trigger-skill",
      owner: "player",
      type: "skill",
      name: "Trigger Skill",
    },
    1,
  );
  const rows = buildEntityActivity({
    entities: [directItem, triggerSkill],
    events: [
      timelineEvent({
        id: "trigger-fallback",
        kind: "effect-executed",
        action: "CardHaste",
        sourceId: "missing-source",
        triggerSourceId: triggerSkill.id,
        attributionConfidence: "exact",
        occurrences: 3,
        value: 2_000,
      }),
      timelineEvent({
        id: "direct-source-wins",
        kind: "effect-executed",
        action: "CardHaste",
        sourceId: directItem.id,
        triggerSourceId: triggerSkill.id,
        attributionConfidence: "exact",
        value: 500,
      }),
    ],
  });
  const rowById = new Map(rows.map((row) => [row.entity.id, row]));

  assert.equal(rowById.get(triggerSkill.id)?.triggers, 3);
  assert.equal(rowById.get(triggerSkill.id)?.counts.haste, 3);
  assert.equal(rowById.get(triggerSkill.id)?.amounts.haste, 2_000);
  assert.equal(rowById.get(directItem.id)?.triggers, 1);
  assert.equal(rowById.get(directItem.id)?.counts.haste, 1);
  assert.equal(rowById.get(directItem.id)?.amounts.haste, 500);
});

test("entity activity preserves every affected target without dividing a multi-target effect amount", () => {
  const source = normalizeEntity(
    {
      entityId: "multi-target-source",
      owner: "player",
      type: "item",
      name: "Coffee",
    },
    0,
  );
  const firstTarget = normalizeEntity(
    {
      entityId: "multi-target-first",
      owner: "player",
      type: "item",
      name: "Microwave",
    },
    1,
  );
  const secondTarget = normalizeEntity(
    {
      entityId: "multi-target-second",
      owner: "player",
      type: "item",
      name: "Trail Mix",
    },
    2,
  );
  const rows = buildEntityActivity({
    entities: [source, firstTarget, secondTarget],
    events: [
      timelineEvent({
        id: "multi-target-haste",
        kind: "effect-executed",
        action: "CardHaste",
        sourceId: source.id,
        targetIds: [firstTarget.id, secondTarget.id],
        attributionConfidence: "exact",
        value: 2_000,
      }),
    ],
  });
  const sourceRow = rows.find((row) => row.entity.id === source.id);

  assert.equal(sourceRow?.counts.haste, 1);
  assert.equal(sourceRow?.amounts.haste, 2_000);
  assert.deepEqual(
    sourceRow?.targetDetails.haste.map((detail) => ({
      targetId: detail.targetId,
      targetName: detail.target?.name,
      count: detail.count,
      amount: detail.amount,
      quantifiedCount: detail.quantifiedCount,
    })),
    [
      {
        targetId: firstTarget.id,
        targetName: "Microwave",
        count: 1,
        amount: 2_000,
        quantifiedCount: 1,
      },
      {
        targetId: secondTarget.id,
        targetName: "Trail Mix",
        count: 1,
        amount: 2_000,
        quantifiedCount: 1,
      },
    ],
  );
});

test("entity activity separates structural attribute deltas and destroy actions", () => {
  const source = normalizeEntity(
    {
      entityId: "destroy-source",
      owner: "opponent",
      type: "item",
      name: "Disintegration Ray",
    },
    0,
  );
  const target = normalizeEntity(
    {
      entityId: "changed-target",
      owner: "player",
      type: "item",
      name: "Ice Swan",
    },
    1,
  );
  const rows = buildEntityActivity({
    entities: [source, target],
    events: [
      timelineEvent({
        id: "destroy",
        kind: "effect-executed",
        action: "CardDisable",
        sourceId: source.id,
        removedTargetIds: [target.id],
        attributionConfidence: "exact",
      }),
      timelineEvent({
        id: "damage-stat",
        kind: "card-attribute",
        action: "DamageAmount",
        targetIds: [target.id],
        previousValue: 10,
        currentValue: 30,
        value: 20,
      }),
      timelineEvent({
        id: "cooldown-reduction",
        kind: "card-attribute",
        action: "PercentCooldownReduction",
        targetIds: [target.id],
        previousValue: 0,
        currentValue: 10,
        value: 10,
      }),
      timelineEvent({
        id: "multicast",
        kind: "card-attribute",
        action: "Multicast",
        targetIds: [target.id],
        previousValue: 2,
        currentValue: 1,
        value: -1,
      }),
    ],
  });
  const rowById = new Map(rows.map((row) => [row.entity.id, row]));

  assert.equal(rowById.get(source.id)?.triggers, 1);
  assert.equal(rowById.get(source.id)?.counts.destroy, 1);
  assert.equal(rowById.get(target.id)?.triggers, 0);
  assert.equal(rowById.get(target.id)?.amounts.damageModifier, 20);
  assert.equal(rowById.get(target.id)?.amounts.cooldownReduction, 10);
  assert.equal(rowById.get(target.id)?.amounts.multicast, -1);
  assert.equal(rowById.get(target.id)?.counts.multicast, 1);
  assert.deepEqual(
    rowById.get(source.id)?.targetDetails.destroy.map((detail) => ({
      targetId: detail.targetId,
      targetName: detail.target?.name,
      count: detail.count,
    })),
    [{ targetId: target.id, targetName: "Ice Swan", count: 1 }],
  );
  assert.deepEqual(
    rowById.get(target.id)?.targetDetails.damageModifier.map((detail) => ({
      targetId: detail.targetId,
      amount: detail.amount,
      firstPreviousValue: detail.firstPreviousValue,
      lastCurrentValue: detail.lastCurrentValue,
    })),
    [{
      targetId: target.id,
      amount: 20,
      firstPreviousValue: 10,
      lastCurrentValue: 30,
    }],
  );
  assert.deepEqual(
    rowById.get(target.id)?.targetDetails.multicast.map((detail) => ({
      targetId: detail.targetId,
      amount: detail.amount,
      firstPreviousValue: detail.firstPreviousValue,
      lastCurrentValue: detail.lastCurrentValue,
    })),
    [{
      targetId: target.id,
      amount: -1,
      firstPreviousValue: 2,
      lastCurrentValue: 1,
    }],
  );
});

test("recording sync interpolates and clamps in both directions", () => {
  const anchors = [
    { combatMs: 1_000, mediaPtsMs: 2_000 },
    { combatMs: 3_000, mediaPtsMs: 5_000 },
    { combatMs: 5_000, mediaPtsMs: 9_000 },
  ];
  assert.equal(mapCombatToMedia(0, anchors), 2_000);
  assert.equal(mapCombatToMedia(2_000, anchors), 3_500);
  assert.equal(mapCombatToMedia(9_000, anchors), 9_000);
  assert.equal(mapMediaToCombat(0, anchors), 1_000);
  assert.equal(mapMediaToCombat(3_500, anchors), 2_000);
  assert.equal(mapMediaToCombat(12_000, anchors), 5_000);
  assert.equal(mapCombatToMedia(100, anchors.slice(0, 1)), null);
});

test("recording sync rejects non-monotonic or mismatched exact metadata", () => {
  const exact = {
    battleId: "battle-1",
    recordingId: "recording-1",
    syncMetadataStatus: "ReadyExact",
    syncAnchors: [
      { combatMs: 0, mediaPtsMs: 500 },
      { combatMs: 1_000, mediaPtsMs: 1_500 },
    ],
  };
  assert.deepEqual(normalizeSyncState(exact, "battle-1"), {
    status: "ReadyExact",
    anchors: exact.syncAnchors,
    identityMatches: true,
    issues: [],
  });
  assert.deepEqual(
    normalizeAnchors({
      anchors: [
        { combatMs: 1_000, mediaPtsMs: 1_000 },
        { combatMs: 500, mediaPtsMs: 2_000 },
      ],
    }),
    [],
  );
  assert.deepEqual(normalizeSyncState(exact, "battle-2"), {
    status: "ReadyUnsynced",
    anchors: [],
    identityMatches: false,
    issues: ["identityMismatch", "invalidExactSync"],
  });
});

test("timeline keeps direct, burn, and poison markers semantically distinct", () => {
  const entities = [
    { id: "source", type: "item" },
    { id: "target", type: "hero" },
  ];
  const events = [
    timelineEvent({
      id: "direct",
      kind: "effect-executed",
      action: "PlayerDamage",
      icon: "../report-assets/objects/00/direct.png",
      sourceId: "source",
      targetIds: ["target"],
    }),
    timelineEvent({
      id: "burn",
      sequence: 1,
      kind: "effect-executed",
      action: "PlayerBurnApply",
      icon: "../report-assets/objects/11/burn.png",
      sourceId: "source",
      targetIds: ["target"],
    }),
    timelineEvent({
      id: "poison",
      sequence: 2,
      kind: "effect-executed",
      action: "PlayerPoisonApply",
      icon: "../report-assets/objects/22/poison.png",
      sourceId: "source",
      targetIds: ["target"],
    }),
  ];

  const clusters = buildClusters(
    { durationMs: 2_000 },
    events,
    entities,
    1_000,
    52,
  );
  assert.deepEqual(
    clusters.map((cluster) => cluster.token),
    ["damageDirect", "burn", "poison"],
  );
  assert.deepEqual(
    clusters.map((cluster) => cluster.icon),
    events.map((event) => event.icon),
  );
  assert.deepEqual(
    clusters.map((cluster) => markerPoint(cluster).y),
    clusters.map((cluster) => cluster.y),
  );
  assert.equal(buildVisualClusters(clusters).length, 3);
});

test("dense timeline markers use distinct laid-out hit targets", () => {
  const entities = [
    { id: "source", type: "item" },
    { id: "target", type: "hero" },
  ];
  const events = [
    ["direct", "PlayerDamage"],
    ["burn", "PlayerBurnApply"],
    ["poison", "PlayerPoisonApply"],
    ["heal", "PlayerHeal"],
    ["shield", "PlayerShieldApply"],
  ].map(([id, action], sequence) =>
    timelineEvent({
      id,
      action,
      combatMs: 1_000,
      frame: 20,
      kind: "effect-executed",
      sequence,
      sourceId: "source",
      targetIds: ["target"],
    })
  );
  const visual = buildVisualClusters(
    buildClusters(
      { durationMs: 2_000 },
      events,
      entities,
      1_000,
      52,
    ),
  );
  const markers = layoutTimelineMarkers(visual);
  const points = markers.map(markerPoint);

  assert.equal(new Set(points.map(({ x, y }) => `${x}:${y}`)).size, 5);
  const laneCenter = visual[0].y;
  assert.deepEqual(
    Array.from(new Set(points.map(({ y }) => y))).sort((a, b) => a - b),
    [laneCenter - 11, laneCenter + 11],
  );
  for (let left = 0; left < points.length; left += 1) {
    for (let right = left + 1; right < points.length; right += 1) {
      assert.ok(
        Math.hypot(
          points[left].x - points[right].x,
          points[left].y - points[right].y,
        ) >= 19,
      );
    }
  }
  const hitIndex = createHitIndex(markers);
  for (let index = 0; index < markers.length; index += 1) {
    assert.equal(
      hitTestTimelineClusters({
        ...points[index],
        hitIndex,
        visualClusters: markers,
        laneHeight: 52,
      }),
      markers[index],
    );
  }
});

test("native timeline markers preserve their aspect ratio around the center", () => {
  assert.deepEqual(markerImageBounds(32, 16, 14), {
    x: -7,
    y: -3.5,
    width: 14,
    height: 7,
  });
  const portrait = markerImageBounds(56, 58, 14);
  assert.equal(portrait.height, 14);
  assert.ok(Math.abs(portrait.width - 13.517241379310345) < 0.000001);
  assert.equal(portrait.x, -portrait.width / 2);
  assert.equal(portrait.y, -7);
});

test("canvas backing scale honors DPR and logical coordinates", () => {
  globalThis.window = { devicePixelRatio: 2 };
  assert.equal(canvasBackingScale(1_000, 500), 2);
  assert.ok(canvasBackingScale(32_000, 20_000) < 1.1);

  let transform;
  const context = {
    setTransform: (...values) => {
      transform = values;
    },
  };
  const canvas = {
    width: 2_000,
    height: 1_000,
    style: { width: "1000px" },
  };
  assert.deepEqual(beginLogicalDraw(canvas, context), {
    width: 1_000,
    height: 500,
  });
  assert.deepEqual(transform, [2, 0, 0, 2, 0, 0]);
});

test("formatting keeps time and extreme-value output bounded", () => {
  assert.equal(formatDuration(2_345), "2.35s");
  assert.equal(formatDuration(12_345), "12.3s");
  assert.equal(formatMilliseconds(999), "999ms");
  assert.equal(formatMilliseconds(1_000), "1s");
  assert.equal(formatMilliseconds(7_850), "7.85s");
  assert.equal(formatMilliseconds(47_400), "47.4s");
  assert.equal(formatMilliseconds(-1_950), "-1.95s");
  assert.equal(formatTimecode(62_345), "01:02.345");
  assert.match(formatCompactNumber(1e18), /^1\.00e18$/u);
  assert.equal(signedOrder(-999), -3);
  assert.equal(orderAxisLabel(-3), "−1e3");
});

test("all supported locales expose the complete viewer copy contract", () => {
  const locales = ["zh-CN", "zh-Hant", "en"];
  const expectedKeys = Object.keys(COPY.en).sort();
  for (const locale of locales) {
    assert.deepEqual(Object.keys(COPY[locale]).sort(), expectedKeys);
    assert.ok(COPY[locale].timelineTitle);
    assert.ok(COPY[locale].statisticsTitle);
    assert.ok(COPY[locale].recordingTitle);
  }
});

test("untrusted report values normalize without widening the input surface", () => {
  assert.equal(isRecord({ value: 1 }), true);
  assert.equal(isRecord([]), false);
  assert.deepEqual(asArray("not-an-array"), []);
  assert.equal(asString("  item  ", "fallback"), "item");
  assert.equal(asString(" ", "fallback"), "fallback");
  assert.equal(asFiniteNumber("42", 0), 42);
  assert.equal(asFiniteNumber("bad", 7), 7);
  assert.equal(pick({ stable: 0 }, ["stable"], 9), 0);
  assert.equal(pick({ stable: null }, ["stable"], 9), 9);
});

test("file report URLs accept only canonical shared assets and recordings", () => {
  const hash = "ab".repeat(32);
  assert.equal(
    safeAssetUrl(`../report-assets/objects/ab/${hash}.png`),
    `../report-assets/objects/ab/${hash}.png`,
  );
  assert.equal(
    safeAssetUrl(`../report-assets/objects/cd/${hash}.png`),
    "",
  );
  assert.equal(
    safeVideoUrl("../CombatReplayVideos/recording%20one/battle.mp4"),
    "../CombatReplayVideos/recording%20one/battle.mp4",
  );
  assert.equal(
    safeVideoUrl("../CombatReplayVideos/%252e%252e/battle.mp4"),
    "",
  );
  assert.equal(
    safeVideoUrl("../CombatReplayVideos/%2e%2e/battle.mp4"),
    "",
  );
});

test("item art uses exact one, two, and three-slot geometry", () => {
  const timelineWidths = [1, 2, 3].map(
    (span) => entityArtDimensions("item", span, "default").width,
  );
  const activityWidths = [1, 2, 3].map(
    (span) => entityArtDimensions("item", span, "activity").width,
  );

  assert.deepEqual(timelineWidths, [44, 88, 132]);
  assert.deepEqual(activityWidths, [44, 88, 132]);
  assert.equal(maximumEntityArtWidth("default"), 132);
  assert.equal(normalizedEntityArtSpan(0), 1);
  assert.equal(normalizedEntityArtSpan(8), 3);
  assert.deepEqual(entityArtDimensions("skill", 3, "default"), {
    width: 36,
    height: 36,
    span: 3,
  });
  assert.equal(
    entityArtDimensions("skill", 1, "default").width <
      entityArtDimensions("item", 1, "default").width,
    true,
  );
});

test("report entities and events normalize exact schema-v1 fields", () => {
  assert.deepEqual(normalizeEntity({
    entityId: "item-1",
    owner: "player",
    type: "item",
    name: "Large Item",
    span: 3,
    assetRelativeUrl:
      "../report-assets/objects/aa/"
      + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
      + ".png",
    order: 0,
  }, 0), {
    id: "item-1",
    name: "Large Item",
    type: "item",
    side: "player",
    span: 3,
    asset:
      "../report-assets/objects/aa/"
      + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
      + ".png",
    hiddenFromTimeline: false,
  });
  const hidden = normalizeEntity(
    {
      entityId: "effect-1",
      owner: "player",
      name: "[Stove] Socket Effect",
      type: "effect",
      order: 1,
    },
    1,
  );
  assert.equal(hidden.name, "");
  assert.equal(hidden.hiddenFromTimeline, true);

  const event = normalizeEvent(
    {
      eventId: "event-1",
      frame: 7,
      frameSequence: 2,
      combatTimeMs: 1_250,
      kind: "effect-executed",
      action: "Damage",
      sourceEntityId: "source-1",
      targetEntityIds: ["target-1", "target-2"],
      removedTargetEntityIds: [],
      role: "applied",
      attributionConfidence: "exact",
      rawReference: {
        category: "effect",
        type: "Damage",
        index: 0,
      },
    },
    0,
  );
  assert.equal(event.combatMs, 1_250);
  assert.equal(event.sourceId, "source-1");
  assert.deepEqual(event.targetIds, ["target-1", "target-2"]);
  assert.equal(event.occurrences, 1);
  assert.equal(eventTimeMs({
    eventId: "event-2",
    frame: 4,
    frameSequence: 0,
    combatTimeMs: 200,
    kind: "status",
    action: "",
    targetEntityIds: [],
    removedTargetEntityIds: [],
    role: "received",
    attributionConfidence: "unknown",
    rawReference: {
      category: "status",
      type: "Status",
      index: 0,
    },
  }), 200);
});

test("combatant metrics normalize exact schema-v1 fields and preserve frame zero", () => {
  assert.equal(normalizeSide("player"), "player");
  assert.equal(normalizeSide("opponent"), "opponent");
  assert.equal(normalizeSide("friendly"), "neutral");
  assert.equal(normalizeSide("enemy"), "neutral");
  assert.equal(normalizeSide("spectator"), "neutral");
  assert.equal(normalizeMetricName("Health_Regen"), "healthRegen");
  assert.equal(normalizeMetricName("shield"), "shield");
  assert.equal(normalizeMetricName("armor"), "");
  assert.equal(normalizeMetricName("unknown"), "");
  assert.deepEqual(
    normalizeMetric({
      frame: 10,
      combatTimeMs: 500,
      combatant: "opponent",
      metric: "Burn",
      value: 42,
      unit: "points",
    }),
    {
      frame: 10,
      combatMs: 500,
      side: "opponent",
      metric: "burn",
      value: 42,
      unit: "points",
    },
  );
  assert.deepEqual(
    frameZeroMetricSamples({
      player: { health: 1_000, shield: 20 },
      opponent: { health: 900 },
    }),
    [
      {
        frame: 0,
        combatMs: 0,
        side: "player",
        metric: "health",
        value: 1_000,
        unit: "points",
      },
      {
        frame: 0,
        combatMs: 0,
        side: "player",
        metric: "shield",
        value: 20,
        unit: "points",
      },
      {
        frame: 0,
        combatMs: 0,
        side: "opponent",
        metric: "health",
        value: 900,
        unit: "points",
      },
    ],
  );
});

const schemaV1GoldenPayload = JSON.parse(
  readFileSync(
    new URL(
      "./fixtures/combat-report-envelope-v1.golden.json",
      import.meta.url,
    ),
    "utf8",
  ),
);

function assertInvalidSchemaV1(mutator) {
  const payload = structuredClone(schemaV1GoldenPayload);
  mutator(payload);
  assert.throws(
    () => decodeEnvelope(payload),
    (error) =>
      error instanceof ReportError
      && error.code === "invalidData",
  );
}

test("schema-v1 golden payload decodes without compatibility aliases", () => {
  const envelope = decodeEnvelope(structuredClone(schemaV1GoldenPayload));
  const report = buildViewModel(envelope, COPY.en);

  assert.equal(report.battleId, "battle-1");
  assert.equal(report.playerName, "pengx17");
  assert.equal(report.opponentName, "Anaui");
  assert.equal(report.outcome, "victory");
  assert.equal(report.entities.length, 2);
  assert.equal(
    report.entities[1].asset,
    "../report-assets/objects/aa/"
      + "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
      + ".png",
  );
  assert.equal(report.events.length, 2);
  assert.equal(report.events[0].occurrences, 1);
  assert.equal(report.metrics.length, 8);
  assert.equal(
    report.videoUrl,
    "../CombatReplayVideos/cccccccccccccccccccccccccccccccc/battle.mp4",
  );
  assert.equal(report.sync.status, "ReadyExact");
  assert.deepEqual(report.sync.anchors, [
    { combatMs: 0, mediaPtsMs: 0 },
    { combatMs: 150, mediaPtsMs: 150 },
  ]);
});

test("schema-v1 decoder rejects removed aliases and malformed array entries", () => {
  assertInvalidSchemaV1((payload) => {
    payload.battleDocument.entityTable = payload.battleDocument.entities;
    delete payload.battleDocument.entities;
  });
  assertInvalidSchemaV1((payload) => {
    payload.battleDocument.combatEvents = payload.battleDocument.events;
    delete payload.battleDocument.events;
  });
  assertInvalidSchemaV1((payload) => {
    payload.battleDocument.events[0].occurrences = 3;
  });
  assertInvalidSchemaV1((payload) => {
    payload.recordingManifest.video = {
      relativeUrl: payload.recordingManifest.videoRelativeUrl,
    };
    delete payload.recordingManifest.videoRelativeUrl;
  });
  assertInvalidSchemaV1((payload) => {
    payload.battleDocument.events[0].targetEntityIds.push({
      entityId: "player-hero",
    });
  });
});

test("combatant state shares one reversible y domain", () => {
  const grouped = new Map([
    ["player:health", [{ value: 3_000 }, { value: 1_500 }]],
    ["opponent:health", [{ value: 2_500 }, { value: 0 }]],
    ["player:burn", [{ value: 800 }]],
    ["opponent:poison", [{ value: 200 }]],
  ]);
  const linearDomain = sharedStateDomain(grouped, "linear");
  assert.deepEqual(linearDomain, { minimum: 0, maximum: 3_240 });
  assert.equal(sharedStateMappedY(0, linearDomain, 120), 112);
  assert.equal(sharedStateMappedY(3_240, linearDomain, 120), 8);
  assert.equal(stateScaleValue(999, "magnitude"), 3);
  assert.ok(niceLinearTicks(0, 3_240, 4).length >= 3);
  assert.deepEqual(
    stateAxisTicks({ minimum: 0, maximum: 3 }, "magnitude"),
    [
      { mapped: 0, label: "0" },
      { mapped: 1, label: "9" },
      { mapped: 2, label: "99" },
      { mapped: 3, label: "1e3" },
    ],
  );
});

test("timeline visibility and clustering preserve every underlying event", () => {
  const entities = [
    { id: "skill", type: "skill" },
    { id: "target", type: "item" },
    { id: "hero", type: "hero" },
  ];
  const entityById = new Map(entities.map((entity) => [entity.id, entity]));
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "player-attribute",
        action: "Health",
      }),
      entityById,
    ),
    false,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "health",
        action: "Damage",
        targetIds: ["target"],
      }),
      entityById,
    ),
    false,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "card-attribute",
        action: "BurnApplyAmount",
        targetIds: ["target"],
      }),
      entityById,
    ),
    false,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "card-attribute",
        action: "Multicast",
        targetIds: ["target"],
      }),
      entityById,
    ),
    true,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "card-attribute",
        action: "PercentCooldownReduction",
        targetIds: ["target"],
      }),
      entityById,
    ),
    true,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "card-attribute",
        action: "CritChance",
        targetIds: ["target"],
      }),
      entityById,
    ),
    false,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "effect-executed",
        action: "PlayerRageApply",
        sourceId: "skill",
      }),
      entityById,
    ),
    false,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "effect-executed",
        action: "PlayerShieldApply",
        sourceId: "skill",
        targetIds: ["hero"],
      }),
      entityById,
    ),
    true,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "effect-executed",
        action: "PlayerDamage",
        sourceId: "skill",
        targetIds: ["target"],
      }),
      entityById,
    ),
    false,
  );
  assert.deepEqual(
    eventLaneEndpoints(
      timelineEvent({
        sourceId: "skill",
        triggerSourceId: "skill",
        targetIds: ["target"],
      }),
      new Map(entities.map((entity, index) => [entity.id, index])),
    ),
    [{ lane: 1, role: "target" }],
  );
  assert.deepEqual(
    eventLaneEndpoints(
      timelineEvent({
        sourceId: "skill",
        triggerSourceId: "skill",
      }),
      new Map(entities.map((entity, index) => [entity.id, index])),
    ),
    [],
  );
  assert.deepEqual(
    buildClusters(
      { durationMs: 2_000 },
      [
        timelineEvent({
          id: "hero-damage",
          kind: "effect-executed",
          action: "PlayerDamage",
          sourceId: "skill",
          targetIds: ["hero"],
        }),
      ],
      entities,
      1_000,
      54,
    ).map((cluster) => ({
      lane: cluster.lane,
      role: cluster.role,
      ids: timelineClusterEventIds(cluster),
    })),
    [{ lane: 2, role: "target", ids: ["hero-damage"] }],
  );

  const events = [
    timelineEvent({
      id: "damage-1",
      kind: "damage",
      sourceId: "skill",
      targetIds: ["target"],
    }),
    timelineEvent({
      id: "damage-2",
      sequence: 1,
      kind: "damage",
      sourceId: "skill",
      targetIds: ["target"],
    }),
  ];
  const clusters = buildClusters(
    { durationMs: 2_000 },
    events,
    entities,
    1_000,
    54,
  );
  assert.equal(clusters.length, 1);
  assert.deepEqual(
    clusters.map((cluster) => cluster.events.length),
    [2],
  );
  assert.equal(eventKindToken(events[0]), "damage");
  assert.deepEqual(
    Array.from(relatedLaneRoles(clusters[0], entities).entries()).map(
      ([lane, roles]) => [lane, Array.from(roles)],
    ),
    [
      [0, ["source"]],
      [1, ["target"]],
    ],
  );
  assert.equal(
    Array.from(createHitIndex(clusters).values()).flat().length,
    clusters.length,
  );

  const visual = buildVisualClusters([
    { ...clusters[0], x: 100, events: [events[0]] },
    { ...clusters[0], x: 120, events: [events[1]] },
  ]);
  assert.equal(visual.length, 1);
  assert.deepEqual(
    visual[0].events.map((event) => event.id),
    ["damage-1", "damage-2"],
  );
  assert.equal(visual[0].members.length, 2);
});

test("structural event semantics render destroy and attribute diffs explicitly", () => {
  const destroy = timelineEvent({
    kind: "effect-executed",
    action: "CardDisable",
  });
  const multicast = timelineEvent({
    kind: "card-attribute",
    action: "Multicast",
    value: -1,
    previousValue: 2,
    currentValue: 1,
  });
  const damage = timelineEvent({
    kind: "card-attribute",
    action: "DamageAmount",
    value: 20,
    previousValue: 10,
    currentValue: 30,
  });

  assert.deepEqual(eventPresentation(destroy), {
    groupKey: "destroy",
    labelKey: "destroy",
    token: "destroy",
  });
  assert.equal(eventKindToken(destroy), "destroy");
  assert.equal(timelineEventToken(destroy), "destroy");
  assert.equal(timelineEventToken(multicast), "attribute");
  assert.deepEqual(attributeEventDiff(multicast), {
    deltaText: "−1",
    polarity: "decrease",
    transitionText: "2 → 1",
  });
  assert.deepEqual(attributeEventDiff(damage), {
    deltaText: "+20",
    polarity: "increase",
    transitionText: "10 → 30",
  });
});

test("status ranges pair applications with their exact frame effects", () => {
  const hasteStart = timelineEvent({
    id: "haste-start",
    frame: 10,
    combatMs: 1_000,
    kind: "card-attribute",
    action: "Haste",
    previousValue: 0,
    currentValue: 2_000,
    targetIds: ["target"],
  });
  const apply = timelineEvent({
    id: "haste-source",
    frame: 10,
    combatMs: 1_000,
    kind: "effect-executed",
    action: "CardHaste",
    sourceId: "skill",
    targetIds: ["target"],
  });
  const hasteEnd = timelineEvent({
    id: "haste-end",
    frame: 20,
    combatMs: 3_000,
    kind: "card-attribute",
    action: "Haste",
    previousValue: 2_000,
    currentValue: 0,
    targetIds: ["target"],
  });
  const ranges = buildStatusRanges(
    {
      durationMs: 5_000,
      events: [hasteStart, apply, hasteEnd],
    },
    [
      { id: "skill", type: "skill" },
      { id: "target", type: "item" },
    ],
    1_000,
    54,
  );
  assert.equal(ranges.length, 1);
  assert.equal(ranges[0].startMs, 1_000);
  assert.equal(ranges[0].endMs, 3_000);
  assert.deepEqual(
    ranges[0].cluster.events.map((event) => event.id),
    ["haste-source"],
  );
  assert.deepEqual(
    ranges[0].cluster.relatedEvents.map((event) => event.id),
    ["haste-start"],
  );
  assert.deepEqual(timelineClusterEventIds(ranges[0].cluster), [
    "haste-source",
  ]);
});

test("status range indexing preserves overlapping targets and removed-target attribution", () => {
  const startA = timelineEvent({
    id: "start-a",
    frame: 10,
    combatMs: 1_000,
    kind: "card-attribute",
    action: "Haste",
    previousValue: 0,
    currentValue: 2_000,
    targetIds: ["target-a"],
  });
  const startB = timelineEvent({
    id: "start-b",
    frame: 10,
    combatMs: 1_000,
    kind: "card-attribute",
    action: "Haste",
    previousValue: 0,
    currentValue: 3_000,
    targetIds: ["target-b"],
  });
  const multiTargetApply = timelineEvent({
    id: "apply-multi",
    frame: 10,
    combatMs: 1_000,
    kind: "effect-executed",
    action: "CardHaste",
    sourceId: "skill",
    targetIds: ["target-a", "target-b"],
    removedTargetIds: ["target-a"],
  });
  const removedTargetApply = timelineEvent({
    id: "apply-removed",
    frame: 10,
    combatMs: 1_000,
    kind: "effect-executed",
    action: "CardHaste",
    sourceId: "skill",
    removedTargetIds: ["target-b"],
  });
  const endA = timelineEvent({
    id: "end-a",
    frame: 20,
    combatMs: 3_000,
    kind: "card-attribute",
    action: "Haste",
    previousValue: 2_000,
    currentValue: 0,
    targetIds: ["target-a"],
  });
  const endB = timelineEvent({
    id: "end-b",
    frame: 30,
    combatMs: 4_000,
    kind: "card-attribute",
    action: "Haste",
    previousValue: 3_000,
    currentValue: 0,
    targetIds: ["target-b"],
  });

  const ranges = buildStatusRanges(
    {
      durationMs: 5_000,
      events: [
        startA,
        startB,
        multiTargetApply,
        removedTargetApply,
        endA,
        endB,
      ],
    },
    [
      { id: "skill", type: "skill" },
      { id: "target-a", type: "item" },
      { id: "target-b", type: "item" },
    ],
    1_000,
    54,
  );

  assert.equal(ranges.length, 2);
  assert.deepEqual(
    ranges.map((range) => [range.targetId, range.startMs, range.endMs]),
    [
      ["target-a", 1_000, 3_000],
      ["target-b", 1_000, 4_000],
    ],
  );
  assert.deepEqual(timelineClusterEventIds(ranges[0].cluster), [
    "apply-multi",
  ]);
  assert.deepEqual(timelineClusterEventIds(ranges[1].cluster), [
    "apply-multi",
    "apply-removed",
  ]);
  assert.deepEqual(
    ranges.map((range) =>
      range.cluster.relatedEvents.map((event) => event.id)
    ),
    [["start-a"], ["start-b"]],
  );
});
