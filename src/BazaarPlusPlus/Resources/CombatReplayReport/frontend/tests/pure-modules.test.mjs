import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import ts from "typescript";
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
  TIMELINE_MARKER_SIZE,
  timelineWidthAtZoom,
} from "../src/timeline/constants.ts";
import {
  getSettledTerminalAnchor,
  getTerminalCombatAnchor,
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
  criticalIndicatorOffset,
  heroHealthAreaGeometry,
  markerGlyphFontSize,
  markerImageBounds,
  markerPoint,
  markerRenderSize,
} from "../src/timeline/event-drawing.ts";
import { layoutTimelineMarkers } from "../src/timeline/marker-layout.ts";
import {
  hitTestTimelineClusters,
} from "../src/timeline/event-interaction.ts";
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
  intrinsicItemArtGeometry,
  maximumEntityArtWidth,
  normalizedEntityArtSpan,
} from "../src/components/semantic/entity-art-geometry.ts";
import {
  DEFAULT_LANE_VISIBILITY,
  filterTimelineEntities,
  opponentBoundaryLane,
} from "../src/timeline/lane-filter.ts";
import {
  ACTIVITY_COLUMNS,
  buildEntityActivity,
  buildStatistics,
} from "../src/statistics/aggregate.ts";
import {
  damageKindFromType,
  eventDamageKind,
} from "../src/model/damage-semantics.ts";
import { enrichDefeatEvents } from "../src/model/defeat-events.ts";
import {
  CARD_ATTRIBUTE_ACTIVITY_SEMANTICS,
  CARD_ATTRIBUTE_ACTIONS,
  PLAYER_ATTRIBUTE_ACTIONS,
  cardAttributePolicy,
  cardAttributeSemantic,
  eventPresentation,
  playerAttributePolicy,
  playerAttributeSemantic,
} from "../src/model/event-semantics.ts";
import {
  mergeInspectorEvents,
  summarizeDirectDamageGroup,
} from "../src/components/inspector/frame-event-groups.ts";
import { attributeEventDiff } from "../src/model/attribute-event-diff.ts";
import { resolveSourceModeAttributeEvents } from "../src/model/effect-attribute-details.ts";
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
    isCritical: false,
    iconSemanticKey: "",
    icon: "",
    occurrences: 1,
    ...overrides,
  };
}

function runtimeSourceFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const entryPath = join(directory, entry.name);
    if (entry.isDirectory()) return runtimeSourceFiles(entryPath);
    return /\.(?:ts|tsx)$/u.test(entry.name) ? [entryPath] : [];
  });
}

function runtimeCopyKeyCandidates() {
  const sourceDirectory = join(
    dirname(fileURLToPath(import.meta.url)),
    "../src",
  );
  const catalogPath = join(sourceDirectory, "i18n/catalog.ts");
  const candidates = new Set([
    "entityEffect",
    "entityHero",
    "entityItem",
    "entitySkill",
  ]);
  for (const sourcePath of runtimeSourceFiles(sourceDirectory)) {
    if (sourcePath === catalogPath) continue;
    const source = ts.createSourceFile(
      sourcePath,
      readFileSync(sourcePath, "utf8"),
      ts.ScriptTarget.Latest,
      true,
      sourcePath.endsWith(".tsx") ? ts.ScriptKind.TSX : ts.ScriptKind.TS,
    );
    const visit = (node) => {
      if (ts.isStringLiteralLike(node)) candidates.add(node.text);
      ts.forEachChild(node, visit);
    };
    visit(source);
  }
  return candidates;
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

test("defeat events identify the lethal settlement and only exact direct sources", () => {
  const entities = [
    {
      id: "attacker",
      name: "Primal Core",
      type: "item",
      side: "player",
      span: 2,
      asset: "",
      hiddenFromTimeline: false,
    },
    {
      id: "defender",
      name: "Opponent",
      type: "hero",
      side: "opponent",
      span: 1,
      asset: "",
      hiddenFromTimeline: false,
    },
  ];
  const metrics = [
    {
      frame: 9,
      combatMs: 450,
      side: "opponent",
      metric: "health",
      value: 100,
      unit: "points",
    },
  ];
  const damage = timelineEvent({
    id: "damage",
    frame: 10,
    sequence: 0,
    combatMs: 500,
    kind: "effect-executed",
    action: "PlayerDamage",
    sourceId: "attacker",
    triggerSourceId: "attacker",
    targetIds: ["defender"],
    value: 120,
    unit: "points",
    iconSemanticKey: "status.damage",
    icon: "damage.png",
  });
  const repeatedDamage = timelineEvent({
    ...damage,
    id: "damage-repeat",
    sequence: 1,
    value: 0,
  });
  const died = timelineEvent({
    id: "died",
    frame: 10,
    sequence: 2,
    combatMs: 500,
    kind: "combatant-died",
    action: "Died",
    targetIds: ["defender"],
  });
  const settlement = timelineEvent({
    id: "settlement",
    frame: 10,
    sequence: 3,
    combatMs: 500,
    kind: "health",
    action: "Health:Damage",
    targetIds: ["defender"],
    value: -120,
    unit: "points",
    iconSemanticKey: "status.damage",
    icon: "damage.png",
  });

  const enrichedEvents = enrichDefeatEvents(
    [damage, repeatedDamage, died, settlement],
    metrics,
    entities,
  );
  const enriched = enrichedEvents.find((event) => event.id === "died");
  const replacedDamage = enrichedEvents.find(
    (event) => event.id === "damage",
  );
  const replacedRepeatedDamage = enrichedEvents.find(
    (event) => event.id === "damage-repeat",
  );

  assert.equal(enriched?.action, "Died:Direct");
  assert.equal(enriched?.sourceId, "attacker");
  assert.equal(enriched?.triggerSourceId, "attacker");
  assert.equal(enriched?.value, 120);
  assert.equal(enriched?.icon, "damage.png");
  assert.deepEqual(eventPresentation(enriched), {
    groupKey: "defeat",
    labelKey: "defeatDirect",
    token: "defeat",
  });
  assert.equal(replacedDamage?.timelineReplacedByDefeat, true);
  assert.equal(replacedRepeatedDamage?.timelineReplacedByDefeat, true);
  assert.equal(
    isVisibleTimelineEvent(
      replacedDamage,
      new Map(entities.map((entity) => [entity.id, entity])),
    ),
    false,
  );
});

test("defeat inference finds the health crossing without borrowing DOT sources", () => {
  const entities = [
    {
      id: "attacker",
      name: "Poison source",
      type: "item",
      side: "player",
      span: 1,
      asset: "",
      hiddenFromTimeline: false,
    },
    {
      id: "defender",
      name: "Defender",
      type: "hero",
      side: "opponent",
      span: 1,
      asset: "",
      hiddenFromTimeline: false,
    },
  ];
  const events = [
    timelineEvent({
      id: "poison-application",
      frame: 10,
      sequence: 0,
      combatMs: 500,
      kind: "effect-executed",
      action: "PlayerPoisonApply",
      sourceId: "attacker",
      targetIds: ["defender"],
      value: 10,
    }),
    timelineEvent({
      id: "died",
      frame: 10,
      sequence: 1,
      combatMs: 500,
      kind: "combatant-died",
      action: "Died",
      targetIds: ["defender"],
    }),
    timelineEvent({
      id: "poison-settlement",
      frame: 10,
      sequence: 2,
      combatMs: 500,
      kind: "health",
      action: "Health:Poison",
      targetIds: ["defender"],
      value: -120,
      unit: "points",
      iconSemanticKey: "status.poison",
      icon: "poison.png",
    }),
    timelineEvent({
      id: "burn-settlement",
      frame: 10,
      sequence: 3,
      combatMs: 500,
      kind: "health",
      action: "Health:Burn",
      targetIds: ["defender"],
      value: -300,
      unit: "points",
    }),
  ];
  const enriched = enrichDefeatEvents(
    events,
    [
      {
        frame: 9,
        combatMs: 450,
        side: "opponent",
        metric: "health",
        value: 100,
        unit: "points",
      },
    ],
    entities,
  ).find((event) => event.id === "died");

  assert.equal(enriched?.action, "Died:Poison");
  assert.equal(enriched?.sourceId, "");
  assert.equal(enriched?.value, 120);
  assert.equal(enriched?.icon, "poison.png");
  assert.deepEqual(eventPresentation(enriched), {
    groupKey: "defeat",
    labelKey: "defeatPoison",
    token: "defeat",
  });
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

test("entity activity uses native recap totals without discarding event target detail", () => {
  const source = normalizeEntity(
    {
      entityId: "native-stat-item",
      owner: "player",
      type: "item",
      name: "Native Stat Item",
    },
    0,
  );
  const target = normalizeEntity(
    {
      entityId: "native-stat-target",
      owner: "opponent",
      type: "hero",
      name: "Target",
    },
    1,
  );
  const cardStats = new Map([
    [
      source.id,
      {
        entityId: source.id,
        damageDone: 525,
        shieldAdded: 0,
        healAdded: 0,
        joyAdded: 0,
        poisonAdded: 0,
        burnAdded: 756,
        hastedCardsCount: 65,
        slowedCardsCount: 0,
        frozenCardsCount: 0,
        useCount: 8,
        regenAdded: 0,
        rageAdded: 0,
      },
    ],
  ]);
  const rows = buildEntityActivity({
    entities: [source, target],
    cardStats,
    events: [
      timelineEvent({
        id: "partial-damage-detail",
        kind: "effect-executed",
        action: "PlayerDamage",
        sourceId: source.id,
        targetIds: [target.id],
        attributionConfidence: "exact",
        value: 367,
      }),
    ],
  });
  const row = rows.find((candidate) => candidate.entity.id === source.id);

  assert.equal(row?.triggers, 8);
  assert.equal(row?.authoritativeValues.damage, 525);
  assert.equal(row?.authoritativeValues.burn, 756);
  assert.equal(row?.authoritativeValues.haste, 65);
  assert.equal(row?.targetDetails.damage[0]?.amount, 367);
});

test("statistics omits the unsupported joy activity column", () => {
  assert.equal(
    ACTIVITY_COLUMNS.some((column) => column.key === "joy"),
    false,
  );
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

test("recording sync keeps short terminal plateaus at their first sample", () => {
  const anchors = [
    { combatMs: 0, mediaPtsMs: 500 },
    { combatMs: 50, mediaPtsMs: 550 },
    { combatMs: 100, mediaPtsMs: 600 },
    { combatMs: 100, mediaPtsMs: 650 },
    { combatMs: 100, mediaPtsMs: 700 },
  ];

  assert.deepEqual(getTerminalCombatAnchor(anchors), anchors[2]);
  assert.equal(mapCombatToMedia(100, anchors), 600);
  assert.equal(mapCombatToMedia(101, anchors), 600);
  assert.equal(mapCombatToMedia(Number.POSITIVE_INFINITY, anchors), 600);
});

test("recording sync uses a settled frame beyond the final combat time", () => {
  const anchors = [
    { combatMs: 0, mediaPtsMs: 500 },
    { combatMs: 50, mediaPtsMs: 550 },
    { combatMs: 100, mediaPtsMs: 600 },
    { combatMs: 100, mediaPtsMs: 850 },
    { combatMs: 100, mediaPtsMs: 1_100 },
    { combatMs: 100, mediaPtsMs: 1_350 },
    { combatMs: 100, mediaPtsMs: 1_600 },
  ];

  assert.deepEqual(getTerminalCombatAnchor(anchors), anchors[2]);
  assert.deepEqual(getSettledTerminalAnchor(anchors), anchors[5]);
  assert.equal(mapCombatToMedia(100, anchors), 600);
  assert.equal(mapCombatToMedia(101, anchors), 1_350);
  assert.equal(mapCombatToMedia(Number.POSITIVE_INFINITY, anchors), 1_350);
});

test("recording sync does not infer a settled frame from legacy slow motion", () => {
  const anchors = [
    { combatMs: 0, mediaPtsMs: 500 },
    { combatMs: 100, mediaPtsMs: 600 },
    { combatMs: 100, mediaPtsMs: 1_350 },
    { combatMs: 100, mediaPtsMs: 2_100 },
    { combatMs: 100, mediaPtsMs: 2_850 },
    { combatMs: 100, mediaPtsMs: 3_600 },
  ];

  assert.deepEqual(getTerminalCombatAnchor(anchors), anchors[1]);
  assert.deepEqual(getSettledTerminalAnchor(anchors), anchors[1]);
  assert.equal(mapCombatToMedia(101, anchors), 600);
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
    [
      laneCenter - 18,
      laneCenter - 9,
      laneCenter,
      laneCenter + 9,
      laneCenter + 18,
    ],
  );
  assert.deepEqual(
    markers.map(({ markerSizeCap }) => markerSizeCap),
    [8, 8, 8, 8, 8],
  );
  assert.deepEqual(
    points.map(({ x }) => x),
    visual.map(({ x }) => x),
  );
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

test("timeline marker stacks enumerate density without changing event time", () => {
  const entities = [
    { id: "source", type: "item" },
    { id: "target", type: "hero" },
  ];
  const expected = [
    { count: 1, offsets: [0], sizeCaps: [14] },
    { count: 2, offsets: [-15, 15], sizeCaps: [14, 14] },
    { count: 3, offsets: [-15, 0, 15], sizeCaps: [14, 14, 14] },
    {
      count: 4,
      offsets: [-19, -6.333, 6.333, 19],
      sizeCaps: [14, 14, 14, 14],
    },
    { count: 5, offsets: [-18, -9, 0, 9, 18], sizeCaps: [8, 8, 8, 8, 8] },
  ];
  const actions = [
    "PlayerDamage",
    "PlayerBurnApply",
    "PlayerPoisonApply",
    "PlayerHeal",
    "PlayerShieldApply",
  ];

  for (const { count, offsets, sizeCaps } of expected) {
    const events = Array.from({ length: count }, (_, sequence) =>
      timelineEvent({
        id: `event-${count}-${sequence}`,
        action: actions[sequence],
        combatMs: 1_000 + sequence,
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
    const laneCenter = visual[0].y;

    assert.deepEqual(
      points.map(({ x }) => x),
      visual.map(({ x }) => x),
    );
    assert.deepEqual(
      points.map(({ y }) =>
        Math.round((y - laneCenter) * 1_000) / 1_000
      ),
      offsets,
    );
    assert.deepEqual(
      markers.map(({ markerSizeCap }) => markerSizeCap),
      sizeCaps,
    );
  }
});

test("timeline marker rows reuse space across transitive collision chains", () => {
  const clusters = [
    {
      lane: 0,
      statusRange: {},
      token: "haste",
      x: 100,
      y: 52,
    },
    { lane: 0, token: "burn", x: 112, y: 52 },
    {
      lane: 0,
      statusRange: {},
      token: "slow",
      x: 124,
      y: 52,
    },
  ];
  const markers = layoutTimelineMarkers(clusters);

  assert.deepEqual(
    markers.map(({ markerX }) => markerX),
    [100, 112, 124],
  );
  assert.deepEqual(
    markers.map(({ markerY }) => markerY),
    [37, 67, 37],
  );
  assert.deepEqual(
    markers.map(({ markerSizeCap }) => markerSizeCap),
    [14, 14, 14],
  );
});

test("timeline marker rows include trailing critical and defeat decorations", () => {
  const criticalEvent = {
    kind: "effect-executed",
    isCritical: true,
  };
  const criticalMarkers = layoutTimelineMarkers([
    {
      lane: 0,
      token: "damageDirect",
      events: [criticalEvent],
      x: 100,
      y: 52,
    },
    {
      lane: 0,
      token: "shield",
      events: [],
      x: 114,
      y: 52,
    },
  ]);
  const defeatMarkers = layoutTimelineMarkers([
    {
      lane: 0,
      token: "defeat",
      events: [],
      x: 100,
      y: 52,
    },
    {
      lane: 0,
      token: "shield",
      events: [],
      x: 114,
      y: 52,
    },
  ]);

  assert.notEqual(
    criticalMarkers[0].markerY,
    criticalMarkers[1].markerY,
  );
  assert.notEqual(
    defeatMarkers[0].markerY,
    defeatMarkers[1].markerY,
  );
  assert.deepEqual(
    criticalMarkers.map(({ markerX }) => markerX),
    [100, 114],
  );
  assert.deepEqual(
    defeatMarkers.map(({ markerX }) => markerX),
    [100, 114],
  );
});

test("timeline marker hit testing keeps a trailing decoration with its event", () => {
  const decorated = {
    lane: 0,
    token: "damageDirect",
    events: [
      {
        kind: "effect-executed",
        isCritical: true,
      },
    ],
    x: 100,
    y: 52,
  };
  const adjacent = {
    lane: 0,
    token: "shield",
    events: [],
    x: 123,
    y: 52,
  };
  const markers = layoutTimelineMarkers([decorated, adjacent]);

  assert.equal(markers[0].markerY, markers[1].markerY);
  assert.equal(
    hitTestTimelineClusters({
      x: 115.5,
      y: 48,
      hitIndex: createHitIndex(markers),
      visualClusters: markers,
      laneHeight: 52,
    }),
    decorated,
  );
});

test("timeline marker rows bound six-way density without overlap", () => {
  const clusters = Array.from({ length: 6 }, (_, index) => ({
    lane: 0,
    token: `token-${index}`,
    x: 100 + index,
    y: 52,
  }));
  const markers = layoutTimelineMarkers(clusters);
  const offsets = markers.map(({ markerY }) => markerY - 52);

  assert.equal(offsets[0], -18.5);
  assert.equal(offsets.at(-1), 18.5);
  assert.equal(new Set(offsets).size, 6);
  assert.deepEqual(
    markers.map(({ markerSizeCap }) => markerSizeCap),
    [7, 7, 7, 7, 7, 7],
  );
  for (let index = 1; index < offsets.length; index += 1) {
    assert.ok(offsets[index] - offsets[index - 1] >= 7);
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

test("timeline marker renderers share one normal size and honor dense caps", () => {
  for (const tier of [1, 2, 3]) {
    assert.equal(markerRenderSize({ tier }), TIMELINE_MARKER_SIZE);
  }
  assert.equal(markerRenderSize({ markerSizeCap: 8 }), 8);
  assert.equal(markerGlyphFontSize(8, 14), 8);
  assert.equal(markerGlyphFontSize(14, 12), 12);
});

test("hero health areas use side-local peaks and step geometry", () => {
  const areas = heroHealthAreaGeometry(
    {
      durationMs: 4_000,
      metrics: [
        {
          frame: 0,
          combatMs: 0,
          side: "player",
          metric: "health",
          value: 1_000,
          unit: "points",
        },
        {
          frame: 40,
          combatMs: 2_000,
          side: "player",
          metric: "health",
          value: 500,
          unit: "points",
        },
        {
          frame: 60,
          combatMs: 3_000,
          side: "player",
          metric: "health",
          value: 0,
          unit: "points",
        },
        {
          frame: 0,
          combatMs: 0,
          side: "opponent",
          metric: "health",
          value: 1_500,
          unit: "points",
        },
      ],
    },
    [
      { side: "player", type: "hero" },
      { side: "player", type: "item" },
      { side: "opponent", type: "hero" },
    ],
    478,
    52,
  );

  assert.deepEqual(areas, [
    {
      lane: 0,
      side: "player",
      peak: 1_000,
      baselineY: 48,
      points: [
        { x: 14, y: 4 },
        { x: 214, y: 4 },
        { x: 214, y: 26 },
        { x: 314, y: 26 },
        { x: 314, y: 48 },
        { x: 414, y: 48 },
      ],
    },
    {
      lane: 2,
      side: "opponent",
      peak: 1_500,
      baselineY: 152,
      points: [
        { x: 14, y: 108 },
        { x: 414, y: 108 },
      ],
    },
  ]);
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
    assert.ok(COPY[locale].statisticsTab);
    assert.ok(COPY[locale].recordingTitle);
  }
});

test("viewer base copy keys have a runtime producer", () => {
  const candidates = runtimeCopyKeyCandidates();
  const unreachable = Object.keys(COPY.en)
    .filter((key) => !key.startsWith("attribute."))
    .filter((key) => !candidates.has(key))
    .sort();

  assert.deepEqual(unreachable, []);
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
  assert.deepEqual(entityArtDimensions("item", 1, "inspector"), {
    width: 48,
    height: 64,
    span: 1,
  });
  assert.deepEqual(entityArtDimensions("skill", 1, "inspector"), {
    width: 64,
    height: 64,
    span: 1,
  });
  assert.equal(
    entityArtDimensions("skill", 1, "default").width <
      entityArtDimensions("item", 1, "default").width,
    true,
  );
});

test("inspector item art follows the exported asset aspect without cropping", () => {
  const sourceGeometryBySpan = {
    1: { width: 194, height: 378 },
    2: { width: 362, height: 378 },
    3: { width: 504, height: 366 },
  };

  for (const span of [1, 2, 3]) {
    const source = sourceGeometryBySpan[span];
    const geometry = intrinsicItemArtGeometry(
      span,
      "inspector",
      source,
    );

    assert.equal(geometry.height, 64);
    assert.equal(geometry.span, span);
    assert.ok(
      Math.abs(geometry.width - (source.width / source.height) * 64)
        < 1e-9,
    );
  }

  assert.deepEqual(
    intrinsicItemArtGeometry(1, "inspector", { width: 1, height: 1 }),
    {
      width: 64,
      height: 64,
      span: 1,
    },
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
  const unnamedEffect = normalizeEntity(
    {
      entityId: "effect-2",
      owner: "opponent",
      name: "",
      type: "effect",
      order: 2,
    },
    2,
  );
  assert.equal(unnamedEffect.name, "");
  assert.equal(unnamedEffect.hiddenFromTimeline, true);

  const event = normalizeEvent(
    {
      eventId: "event-1",
      frame: 7,
      frameSequence: 2,
      combatTimeMs: 1_250,
      kind: "effect-executed",
      action: "Damage",
      isCritical: true,
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
  assert.equal(event.isCritical, true);
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
  assert.equal(report.scrubVideoUrl, "");
  assert.equal(report.sync.status, "ReadyExact");
  assert.deepEqual(report.sync.anchors, [
    { combatMs: 0, mediaPtsMs: 0 },
    { combatMs: 150, mediaPtsMs: 150 },
  ]);
});

test("schema-v1 accepts an optional safe scrub proxy URL", () => {
  const payload = structuredClone(schemaV1GoldenPayload);
  payload.recordingManifest.scrubVideoRelativeUrl =
    "../CombatReplayVideos/cccccccccccccccccccccccccccccccc/battle.scrub.mp4";
  const report = buildViewModel(decodeEnvelope(payload), COPY.en);

  assert.equal(
    report.scrubVideoUrl,
    "../CombatReplayVideos/cccccccccccccccccccccccccccccccc/battle.scrub.mp4",
  );
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
  assert.deepEqual(
    sharedStateDomain(grouped, "linear", ["burn", "poison"]),
    { minimum: 0, maximum: 864 },
  );
  assert.equal(sharedStateDomain(grouped, "linear", []), null);
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
        kind: "effect-executed",
        action: "CardHaste",
        sourceId: "skill",
        targetIds: ["target"],
      }),
      entityById,
    ),
    false,
    "target mode uses the status range instead of a duplicate application",
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "effect-executed",
        action: "CardHaste",
        sourceId: "skill",
        targetIds: ["target"],
      }),
      entityById,
      "source",
    ),
    true,
    "source mode keeps the attributable application event",
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "aura",
        action: "Aura",
        sourceId: "target",
        targetIds: ["target"],
      }),
      entityById,
    ),
    false,
    "raw aura bookkeeping must not obscure the concrete attribute diff",
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "effect-executed",
        action: "PlayerModifyAttribute",
        sourceId: "skill",
        targetIds: ["hero"],
      }),
      entityById,
    ),
    false,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "player-attribute",
        action: "HealthMax",
        targetIds: ["hero"],
      }),
      entityById,
    ),
    true,
  );
  assert.equal(
    isVisibleTimelineEvent(
      timelineEvent({
        kind: "player-attribute",
        action: "EnragedDuration",
        targetIds: ["hero"],
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
    true,
  );
  for (const action of [
    "Cooldown",
    "Haste",
    "Slow",
    "Freeze",
    "Chilled",
  ]) {
    assert.equal(
      isVisibleTimelineEvent(
        timelineEvent({
          kind: "card-attribute",
          action,
          targetIds: ["target"],
        }),
        entityById,
      ),
      false,
      `${action} live state must not become an individual marker`,
    );
  }
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
    true,
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
        targetIds: ["target"],
      }),
      new Map(entities.map((entity, index) => [entity.id, index])),
      "source",
    ),
    [{ lane: 0, role: "source" }],
  );
  assert.deepEqual(
    eventLaneEndpoints(
      timelineEvent({
        triggerSourceId: "skill",
        targetIds: ["target"],
      }),
      new Map(entities.map((entity, index) => [entity.id, index])),
      "source",
    ),
    [{ lane: 0, role: "trigger" }],
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

  const criticalItem = { id: "critical-item", type: "item" };
  const criticalEntities = [criticalItem, ...entities];
  const criticalEvent = timelineEvent({
    id: "critical-damage",
    kind: "effect-executed",
    action: "PlayerDamage",
    sourceId: "critical-item",
    targetIds: ["hero"],
    isCritical: true,
    iconSemanticKey: "status.damage",
    icon: "../report-assets/objects/aa/damage.png",
  });
  assert.deepEqual(
    eventLaneEndpoints(
      criticalEvent,
      new Map(
        criticalEntities.map((entity, index) => [entity.id, index]),
      ),
    ),
    [{ lane: 3, role: "target" }],
  );
  assert.deepEqual(
    eventLaneEndpoints(
      criticalEvent,
      new Map(
        criticalEntities.map((entity, index) => [entity.id, index]),
      ),
      "source",
    ),
    [{ lane: 0, role: "source" }],
  );
  assert.deepEqual(
    buildClusters(
      { durationMs: 2_000 },
      [criticalEvent],
      criticalEntities,
      1_000,
      54,
    ).map((cluster) => ({
      groupKey: cluster.groupKey,
      labelKey: cluster.labelKey,
      lane: cluster.lane,
      role: cluster.role,
      tier: cluster.tier,
    })),
    [
      {
        groupKey: "damage-direct",
        labelKey: "damageDirect",
        lane: 3,
        role: "target",
        tier: 2,
      },
    ],
  );
  assert.deepEqual(
    buildClusters(
      { durationMs: 2_000 },
      [criticalEvent],
      criticalEntities,
      1_000,
      54,
      "source",
    ).map((cluster) => ({
      groupKey: cluster.groupKey,
      labelKey: cluster.labelKey,
      lane: cluster.lane,
      role: cluster.role,
      tier: cluster.tier,
      critical: cluster.events.some((event) => event.isCritical),
    })),
    [
      {
        groupKey: "damage-direct",
        labelKey: "damageDirect",
        lane: 0,
        role: "source",
        tier: 2,
        critical: true,
      },
    ],
  );
  const criticalOffset = criticalIndicatorOffset(14);
  assert.equal(criticalOffset.x, 10);
  assert.ok(Math.abs(criticalOffset.y + 3.92) < 0.001);

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
  assert.equal(visual.length, 2);
  assert.deepEqual(
    visual.map((cluster) => cluster.events.map((event) => event.id)),
    [["damage-1"], ["damage-2"]],
  );
  assert.deepEqual(visual.map((cluster) => cluster.x), [100, 120]);
});

test("source mode resolves only unique same-target attribute executions", () => {
  const events = [
    timelineEvent({
      id: "reload",
      frame: 221,
      combatMs: 11_050,
      kind: "effect-executed",
      action: "CardReload",
      sourceId: "miss-isles",
      triggerSourceId: "primal-core",
      targetIds: ["miss-isles"],
    }),
    timelineEvent({
      id: "modify",
      frame: 221,
      sequence: 1,
      combatMs: 11_050,
      kind: "effect-executed",
      action: "CardModifyAttribute",
      sourceId: "miss-isles",
      triggerSourceId: "primal-core",
      targetIds: ["miss-isles"],
    }),
    timelineEvent({
      id: "ammo",
      frame: 221,
      sequence: 2,
      combatMs: 11_050,
      kind: "card-attribute",
      action: "Ammo",
      value: 1,
      previousValue: 0,
      currentValue: 1,
      unit: "points",
      targetIds: ["miss-isles"],
      iconSemanticKey: "status.ammo",
      icon: "ammo.png",
    }),
    timelineEvent({
      id: "damage",
      frame: 221,
      sequence: 3,
      combatMs: 11_050,
      kind: "card-attribute",
      action: "DamageAmount",
      value: 5,
      previousValue: 170,
      currentValue: 175,
      unit: "points",
      targetIds: ["miss-isles"],
      iconSemanticKey: "status.damage",
      icon: "damage.png",
    }),
  ];

  const resolved = resolveSourceModeAttributeEvents(events);
  assert.deepEqual(
    resolved.map((event) => ({
      action: event.action,
      currentValue: event.currentValue,
      id: event.id,
      previousValue: event.previousValue,
      resolvedAttributeAction: event.resolvedAttributeAction,
      value: event.value,
    })),
    [
      {
        action: "CardReload",
        currentValue: 1,
        id: "reload",
        previousValue: 0,
        resolvedAttributeAction: "Ammo",
        value: 1,
      },
      {
        action: "CardModifyAttribute",
        currentValue: 175,
        id: "modify",
        previousValue: 170,
        resolvedAttributeAction: "DamageAmount",
        value: 5,
      },
      {
        action: "Ammo",
        currentValue: 1,
        id: "ammo",
        previousValue: 0,
        resolvedAttributeAction: undefined,
        value: 1,
      },
      {
        action: "DamageAmount",
        currentValue: 175,
        id: "damage",
        previousValue: 170,
        resolvedAttributeAction: undefined,
        value: 5,
      },
    ],
  );
  assert.equal(eventPresentation(resolved[0]).labelKey, "reload");
  assert.equal(eventPresentation(resolved[1]).labelKey, "attributeDamage");
  assert.equal(timelineEventToken(resolved[0]), "attribute");
  assert.equal(
    isVisibleTimelineEvent(
      resolved[0],
      new Map([["miss-isles", { id: "miss-isles", type: "item" }]]),
      "source",
    ),
    true,
  );
  assert.equal(
    isVisibleTimelineEvent(
      events[0],
      new Map([["miss-isles", { id: "miss-isles", type: "item" }]]),
      "source",
    ),
    false,
  );
  const clusters = buildClusters(
    { durationMs: 20_000 },
    resolved.slice(0, 2),
    [{ id: "miss-isles", type: "item" }],
    1_000,
    52,
    "source",
  );
  assert.equal(clusters.length, 1);
  assert.equal(clusters[0].labelKey, "attribute");
  assert.equal(clusters[0].icon, "");
  assert.deepEqual(
    clusters[0].events.map((event) => event.id),
    ["reload", "modify"],
  );

  const ambiguous = resolveSourceModeAttributeEvents([
    events[1],
    timelineEvent({
      ...events[1],
      id: "modify-2",
      sequence: 4,
    }),
    events[3],
  ]);
  assert.equal(ambiguous[0].resolvedAttributeAction, undefined);
  assert.equal(ambiguous[1].resolvedAttributeAction, undefined);
});

test("timeline attribute markers stay generic while a same-frame member keeps every concrete event", () => {
  const entities = [
    { id: "item", type: "item" },
    { id: "hero", type: "hero" },
  ];
  const critIcon = "../report-assets/objects/aa/crit.png";
  const regenIcon = "../report-assets/objects/bb/regen.png";
  const critFirst = timelineEvent({
    id: "crit-1",
    frame: 260,
    combatMs: 1_000,
    kind: "card-attribute",
    action: "CritChance",
    value: 2,
    previousValue: 78,
    currentValue: 80,
    targetIds: ["item"],
    iconSemanticKey: "status.critChance",
    icon: critIcon,
  });
  const critSecond = timelineEvent({
    id: "crit-2",
    frame: 261,
    combatMs: 1_040,
    kind: "card-attribute",
    action: "CritChance",
    value: 2,
    previousValue: 80,
    currentValue: 82,
    targetIds: ["item"],
  });
  const regenApplication = timelineEvent({
    id: "regen-application",
    frame: 261,
    sequence: 1,
    combatMs: 1_040,
    kind: "effect-executed",
    action: "PlayerRegenApply",
    targetIds: ["hero"],
    iconSemanticKey: "status.regen",
    icon: regenIcon,
  });
  const regenAmount = timelineEvent({
    id: "regen-amount",
    frame: 261,
    sequence: 2,
    combatMs: 1_040,
    kind: "card-attribute",
    action: "RegenApplyAmount",
    value: 4,
    previousValue: 109,
    currentValue: 113,
    targetIds: ["item"],
  });
  const clusters = buildClusters(
    { durationMs: 2_000 },
    [critFirst, critSecond, regenApplication, regenAmount],
    entities,
    1_000,
    52,
  );
  const attributeClusters = clusters.filter(
    (cluster) =>
      cluster.lane === 0 && cluster.groupKey === "attribute",
  );
  assert.equal(attributeClusters.length, 2);
  assert.deepEqual(
    attributeClusters.map((cluster) => ({
      events: cluster.events.map((event) => event.id),
      icon: cluster.icon,
      iconSemanticKey: cluster.iconSemanticKey,
      labelKey: cluster.labelKey,
      token: cluster.token,
    })),
    [
      {
        events: ["crit-1"],
        icon: "",
        iconSemanticKey: "",
        labelKey: "attribute",
        token: "attribute",
      },
      {
        events: ["crit-2", "regen-amount"],
        icon: "",
        iconSemanticKey: "",
        labelKey: "attribute",
        token: "attribute",
      },
    ],
  );

  const visual = buildVisualClusters(clusters);
  const attributeVisual = visual.filter(
    (cluster) =>
      cluster.lane === 0 && cluster.groupKey === "attribute",
  );
  assert.equal(attributeVisual.length, 2);
  assert.deepEqual(
    attributeVisual.map((cluster) => cluster.events.map((event) => event.id)),
    [["crit-1"], ["crit-2", "regen-amount"]],
  );
  assert.deepEqual(
    attributeVisual.map((cluster) => cluster.icon),
    ["", ""],
  );
  assert.deepEqual(
    attributeVisual.map((cluster) => cluster.labelKey),
    ["attribute", "attribute"],
  );
  assert.deepEqual(
    attributeVisual[1].events.map((event) => event.id),
    ["crit-2", "regen-amount"],
  );
});

test("adjacent-frame markers keep one hit identity across their painted width", () => {
  const entities = [
    { id: "source-a", type: "item" },
    { id: "source-b", type: "item" },
    { id: "target", type: "hero" },
  ];
  const events = [
    timelineEvent({
      id: "shield-frame-61",
      frame: 61,
      combatMs: 3_050,
      kind: "effect-executed",
      action: "PlayerShieldApply",
      sourceId: "source-a",
      targetIds: ["target"],
      value: 61,
    }),
    timelineEvent({
      id: "shield-frame-62",
      frame: 62,
      combatMs: 3_100,
      kind: "effect-executed",
      action: "PlayerShieldApply",
      sourceId: "source-b",
      targetIds: ["target"],
      value: 466,
    }),
  ];
  const markers = layoutTimelineMarkers(
    buildVisualClusters(
      buildClusters(
        { durationMs: 8_000 },
        events,
        entities,
        1_000,
        52,
      ),
    ),
  );

  assert.equal(markers.length, 2);
  assert.deepEqual(
    markers.map((cluster) => cluster.events.map((event) => event.id)),
    [["shield-frame-61"], ["shield-frame-62"]],
  );
  assert.deepEqual(
    markers.map((cluster) => markerPoint(cluster).y - cluster.y),
    [-15, 15],
  );

  const hitIndex = createHitIndex(markers);
  for (const marker of markers) {
    const point = markerPoint(marker);
    for (let dx = -9; dx <= 9; dx += 1) {
      assert.equal(
        hitTestTimelineClusters({
          x: point.x + dx,
          y: point.y,
          hitIndex,
          visualClusters: markers,
          laneHeight: 52,
        }),
        marker,
      );
    }
  }
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

  const burnAmount = timelineEvent({
    kind: "card-attribute",
    action: "BurnApplyAmount",
    value: 20,
    previousValue: 10,
    currentValue: 30,
  });
  assert.deepEqual(eventPresentation(burnAmount), {
    groupKey: "attribute-BurnApplyAmount",
    labelKey: "attribute.BurnApplyAmount",
    token: "attribute",
  });
  assert.deepEqual(attributeEventDiff(burnAmount), {
    deltaText: "+20",
    polarity: "increase",
    transitionText: "10 → 30",
  });
  assert.equal(COPY.en["attribute.BurnApplyAmount"], "Burn applied");
  assert.equal(COPY["zh-CN"]["attribute.BurnApplyAmount"], "施加燃烧");
  assert.equal(
    cardAttributeSemantic("BurnApplyAmount")?.nativeSemanticKey,
    "status.burn",
  );
  assert.equal(
    cardAttributeSemantic("ShieldApplyAmount")?.nativeSemanticKey,
    "status.shield",
  );
});

test("attribute policies classify the complete current enum inventory", () => {
  assert.equal(PLAYER_ATTRIBUTE_ACTIONS.length, 40);
  assert.equal(new Set(PLAYER_ATTRIBUTE_ACTIONS).size, 40);
  assert.equal(CARD_ATTRIBUTE_ACTIONS.length, 89);
  assert.equal(new Set(CARD_ATTRIBUTE_ACTIONS).size, 89);
  assert.equal(
    PLAYER_ATTRIBUTE_ACTIONS.every(
      (action) => playerAttributePolicy(action).attributeClass !== "unknown",
    ),
    true,
  );
  assert.equal(
    CARD_ATTRIBUTE_ACTIONS.every(
      (action) => cardAttributePolicy(action).attributeClass !== "unknown",
    ),
    true,
  );

  assert.deepEqual(playerAttributePolicy("EnragedDuration"), {
    attributeClass: "state",
    timeline: "hidden",
    inspectorVisible: false,
    statisticsVisible: false,
  });
  assert.deepEqual(playerAttributePolicy("HealthMax"), {
    attributeClass: "modifier",
    timeline: "marker",
    inspectorVisible: true,
    statisticsVisible: false,
  });
  assert.deepEqual(cardAttributePolicy("Cooldown"), {
    attributeClass: "state",
    timeline: "hidden",
    inspectorVisible: false,
    statisticsVisible: false,
  });
  assert.deepEqual(cardAttributePolicy("Haste"), {
    attributeClass: "state",
    timeline: "hidden",
    inspectorVisible: false,
    statisticsVisible: false,
  });
  assert.deepEqual(cardAttributePolicy("DamageAmount"), {
    attributeClass: "modifier",
    timeline: "marker",
    inspectorVisible: true,
    statisticsVisible: true,
  });
  assert.deepEqual(cardAttributePolicy("CritChance"), {
    attributeClass: "modifier",
    timeline: "marker",
    inspectorVisible: true,
    statisticsVisible: true,
  });
  assert.equal(
    cardAttributePolicy("BuyPrice").attributeClass,
    "economy",
  );
  assert.equal(
    cardAttributePolicy("Custom_4").attributeClass,
    "diagnostic",
  );
  assert.equal(
    cardAttributePolicy("FutureGameAttribute").attributeClass,
    "unknown",
  );

  for (const action of PLAYER_ATTRIBUTE_ACTIONS) {
    const descriptor = playerAttributePolicy(action);
    if (!descriptor.inspectorVisible) continue;
    const semantic = playerAttributeSemantic(action);
    assert.notEqual(semantic, null, `missing player semantic for ${action}`);
    for (const locale of Object.keys(COPY)) {
      assert.ok(
        COPY[locale][semantic.labelKey],
        `missing ${locale} player label for ${action}`,
      );
    }
  }
  for (const action of CARD_ATTRIBUTE_ACTIONS) {
    const descriptor = cardAttributePolicy(action);
    if (!descriptor.inspectorVisible) continue;
    const semantic = cardAttributeSemantic(action);
    assert.notEqual(semantic, null, `missing card semantic for ${action}`);
    for (const locale of Object.keys(COPY)) {
      assert.ok(
        COPY[locale][semantic.labelKey],
        `missing ${locale} card label for ${action}`,
      );
    }
  }
});

test("card attribute activity columns come from the shared semantic registry", () => {
  assert.deepEqual(
    CARD_ATTRIBUTE_ACTIVITY_SEMANTICS.map((semantic) => ({
      action: semantic.action,
      aggregation: semantic.activity.aggregation,
      key: semantic.activity.key,
      label: semantic.labelKey,
      semanticKey: semantic.nativeSemanticKey,
      token: semantic.token,
      unit: semantic.activity.unit,
    })),
    ACTIVITY_COLUMNS
      .filter((column) => column.eventKind === "card-attribute")
      .map((column) => ({
        action: column.actions[0],
        aggregation: column.aggregation,
        key: column.key,
        label: column.label,
        semanticKey: column.semanticKey,
        token: column.token,
        unit: column.unit,
      })),
  );
  assert.equal(
    CARD_ATTRIBUTE_ACTIVITY_SEMANTICS.every(
      (semantic) =>
        cardAttributePolicy(semantic.action).statisticsVisible,
    ),
    true,
  );
});

test("player max-health changes keep their diff without guessing an application source", () => {
  const application = timelineEvent({
    id: "pasta-application",
    frame: 159,
    kind: "effect-executed",
    action: "PlayerModifyAttribute",
    sourceId: "pasta",
    triggerSourceId: "pasta",
    targetIds: ["player-hero"],
  });
  const healthMax = timelineEvent({
    id: "health-max",
    frame: 159,
    kind: "player-attribute",
    action: "HealthMax",
    targetIds: ["player-hero"],
    value: 25,
    previousValue: 4_095,
    currentValue: 4_120,
    unit: "points",
  });

  assert.deepEqual(eventPresentation(healthMax), {
    groupKey: "player-attribute-HealthMax",
    labelKey: "attributeHealthMax",
    token: "attributeHealthMax",
  });
  assert.deepEqual(attributeEventDiff(healthMax), {
    deltaText: "+25",
    polarity: "increase",
    transitionText: "4,095 → 4,120",
  });
  assert.equal(healthMax.sourceId, "");
  assert.equal(healthMax.triggerSourceId, "");
  assert.equal(application.sourceId, "pasta");
});

test("compact status ranges pair applications with their exact frame effects", () => {
  const hasteRange = timelineEvent({
    id: "haste-range",
    frame: 10,
    combatMs: 1_000,
    kind: "card-status-range",
    action: "Haste",
    value: 2_000,
    unit: "ms",
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
  const entities = [
    { id: "skill", type: "skill" },
    { id: "target", type: "item" },
  ];
  const ranges = buildStatusRanges(
    {
      durationMs: 5_000,
      events: [hasteRange, apply],
    },
    entities,
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
    ["haste-range"],
  );
  assert.deepEqual(timelineClusterEventIds(ranges[0].cluster), [
    "haste-source",
  ]);
  assert.equal(
    isVisibleTimelineEvent(hasteRange, new Map(
      entities.map((entity) => [entity.id, entity]),
    )),
    false,
  );
  assert.equal(combatLogEventToken(hasteRange), "status");
  assert.equal(
    isNarrativeCombatLogEntry(buildCombatLogEntries([hasteRange])[0]),
    false,
  );
});

test("status range indexing preserves overlapping targets and removed-target attribution", () => {
  const rangeA = timelineEvent({
    id: "range-a",
    frame: 10,
    combatMs: 1_000,
    kind: "card-status-range",
    action: "Haste",
    value: 2_000,
    unit: "ms",
    targetIds: ["target-a"],
  });
  const rangeB = timelineEvent({
    id: "range-b",
    frame: 10,
    combatMs: 1_000,
    kind: "card-status-range",
    action: "Haste",
    value: 3_000,
    unit: "ms",
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
  const ranges = buildStatusRanges(
    {
      durationMs: 5_000,
      events: [
        rangeA,
        rangeB,
        multiTargetApply,
        removedTargetApply,
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
    [["range-a"], ["range-b"]],
  );
});
