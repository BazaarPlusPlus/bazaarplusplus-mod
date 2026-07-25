import assert from "node:assert/strict";
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
  timelineClusterEventIds,
} from "../src/timeline/clusters.ts";
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
  entityArtDimensions,
  maximumEntityArtWidth,
  normalizedEntityArtSpan,
} from "../src/components/semantic/entity-art-geometry.ts";
import {
  DEFAULT_LANE_VISIBILITY,
  filterTimelineEntities,
  opponentBoundaryLane,
} from "../src/timeline/lane-filter.ts";
import { buildEntityActivity } from "../src/statistics/aggregate.ts";

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
    raw: {},
    ...overrides,
  };
}

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

test("report entities and events normalize legacy field aliases deterministically", () => {
  const rawEntity = {
    instanceId: "item-1",
    displayName: "Large Item",
    semanticType: "item",
    team: "friendly",
    size: 8,
    visual: { relativeUrl: "../asset.png" },
  };
  assert.deepEqual(normalizeEntity(rawEntity, 0), {
    id: "item-1",
    name: "Large Item",
    type: "item",
    side: "friendly",
    span: 3,
    asset: "../asset.png",
    hiddenFromTimeline: false,
    raw: rawEntity,
  });
  const hidden = normalizeEntity(
    { id: "effect-1", name: "[Stove] Socket Effect", type: "effect" },
    1,
  );
  assert.equal(hidden.name, "");
  assert.equal(hidden.hiddenFromTimeline, true);

  const event = normalizeEvent(
    {
      id: "event-1",
      frame: 7,
      sequence: 2,
      combatTimeSeconds: 1.25,
      source: { instanceId: "source-1" },
      targets: [{ entityId: "target-1" }],
      targetEntityId: "target-2",
      count: 3,
    },
    0,
  );
  assert.equal(event.combatMs, 1_250);
  assert.equal(event.sourceId, "source-1");
  assert.deepEqual(event.targetIds, ["target-1", "target-2"]);
  assert.equal(event.occurrences, 3);
  assert.equal(eventTimeMs({ combatFrame: 4 }), 200);
});

test("combatant metrics normalize aliases and preserve frame-zero state", () => {
  assert.equal(normalizeSide("friendly"), "player");
  assert.equal(normalizeSide("opponent-hero"), "opponent");
  assert.equal(normalizeSide("spectator"), "neutral");
  assert.equal(normalizeMetricName("Health_Regen"), "healthRegen");
  assert.equal(normalizeMetricName("armor"), "shield");
  assert.equal(normalizeMetricName("unknown"), "");
  assert.deepEqual(
    normalizeMetric({
      combatant: "enemy",
      name: "fire",
      currentValue: "42",
      combatMs: 500,
    }),
    {
      frame: 0,
      combatMs: 500,
      side: "opponent",
      metric: "burn",
      value: 42,
      unit: "points",
    },
  );
  assert.deepEqual(
    frameZeroMetricSamples({
      frameZeroState: {
        player: { health: 1_000, shield: 20 },
        opponent: { health: 900 },
      },
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
