import { expect, test } from "@playwright/test";
import { copyFile, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const testDirectory = dirname(fileURLToPath(import.meta.url));
const viewerArtifactDirectory = process.env.BPP_VIEWER_ARTIFACT_DIR
  ? resolve(process.env.BPP_VIEWER_ARTIFACT_DIR)
  : resolve(testDirectory, "../dist");

function schemaEvent(event) {
  return {
    ...event,
    removedTargetEntityIds: event.removedTargetEntityIds ?? [],
    rawReference: event.rawReference ?? {
      category: "behavior-test",
      type: event.kind,
      index: event.frameSequence,
    },
  };
}

async function visibleGuideRatios(page, bounds) {
  const viewportWidth = await page.evaluate(
    () => document.documentElement.clientWidth,
  );
  const visibleRightRatio = Math.max(
    0.2,
    Math.min(0.95, (viewportWidth - bounds.x - 24) / bounds.width),
  );
  return {
    pinned: visibleRightRatio * 0.48,
    preview: visibleRightRatio * 0.82,
  };
}

async function timelineMarkerPoint(
  page,
  { combatMs, durationMs, entityId, dx = 0, dy = 0 },
) {
  return page.evaluate(
    ({ combatMs, durationMs, entityId, dx, dy }) => {
      const canvas = document.querySelector(
        '[data-bpp-test-id="timeline-canvas"]',
      );
      const lane = document.querySelector(
        `[data-bpp-lane-index][data-bpp-entity-id="${entityId}"]`,
      );
      if (!(canvas instanceof HTMLCanvasElement) || !lane) return null;
      const bounds = canvas.getBoundingClientRect();
      const logicalWidth =
        Number.parseFloat(canvas.style.width) || canvas.width;
      const logicalHeight =
        Number.parseFloat(canvas.style.height) || canvas.height;
      const laneIndex = Number(lane.getAttribute("data-bpp-lane-index"));
      const timelineX =
        14 + (combatMs / durationMs) * Math.max(1, logicalWidth - 78);
      return {
        x:
          bounds.left
          + (timelineX + dx) * (bounds.width / logicalWidth),
        y:
          bounds.top
          + (laneIndex * 52 + 26 + dy)
            * (bounds.height / logicalHeight),
      };
    },
    { combatMs, durationMs, entityId, dx, dy },
  );
}

const fixtureEnvelope = {
  schemaVersion: 1,
  locale: "en",
  battleDocument: {
    schemaVersion: 1,
    documentId: "behavior-fixture",
    battleId: "behavior-battle",
    recordedAtUtc: "2026-07-25T00:00:00Z",
    durationMs: 8000,
    frameDurationMs: 50,
    frameCount: 160,
    rawRecordCount: 12,
    summary: {
      playerName: "Fixture Player",
      opponentName: "Fixture Opponent",
      outcome: "win",
    },
    player: {
      name: "Fixture Player",
      hero: "Jules",
    },
    opponent: {
      name: "Fixture Opponent",
      hero: "Mak",
    },
    winner: "player",
    loser: "opponent",
    frameZeroState: {
      player: {
        health: 1000,
        rage: 0,
        healthRegen: 10,
        shield: 20,
        burn: 0,
        poison: 0,
      },
      opponent: {
        health: 1200,
        rage: 0,
        healthRegen: 5,
        shield: 0,
        burn: 0,
        poison: 0,
      },
    },
    metrics: [
      {
        frame: 40,
        combatTimeMs: 2000,
        combatant: "player",
        metric: "health",
        value: 900,
        unit: "points",
      },
      {
        frame: 40,
        combatTimeMs: 2000,
        combatant: "opponent",
        metric: "health",
        value: 1080,
        unit: "points",
      },
      {
        frame: 80,
        combatTimeMs: 4000,
        combatant: "opponent",
        metric: "burn",
        value: 12,
        unit: "points",
      },
    ],
    entities: [
      {
        entityId: "player-hero",
        owner: "player",
        type: "hero",
        name: "Fixture Player",
        span: 1,
        order: 0,
      },
      {
        entityId: "player-item",
        owner: "player",
        type: "item",
        name: "Training Blade",
        assetRelativeUrl:
          "../report-assets/0000000000000000000000000000000000000000000000000000000000000000.png",
        span: 2,
        order: 1,
      },
      {
        entityId: "player-skill",
        owner: "player",
        type: "skill",
        name: "Quick Thinking",
        span: 1,
        order: 2,
      },
      {
        entityId: "opponent-hero",
        owner: "opponent",
        type: "hero",
        name: "Fixture Opponent",
        span: 1,
        order: 0,
      },
      {
        entityId: "opponent-item",
        owner: "opponent",
        type: "item",
        name: "Practice Shield",
        span: 1,
        order: 1,
      },
    ],
    cardStats: [
      {
        entityId: "player-item",
        damageDone: 525,
        shieldAdded: 0,
        healAdded: 0,
        joyAdded: 0,
        poisonAdded: 0,
        burnAdded: 367,
        hastedCardsCount: 0,
        slowedCardsCount: 0,
        frozenCardsCount: 0,
        useCount: 8,
        regenAdded: 0,
        rageAdded: 0,
      },
    ],
    events: [
      schemaEvent({
        eventId: "damage-1",
        frame: 40,
        frameSequence: 0,
        combatTimeMs: 2000,
        kind: "effect-executed",
        action: "PlayerDamage",
        sourceEntityId: "player-item",
        triggerSourceEntityId: "player-item",
        targetEntityIds: ["opponent-hero"],
        value: 120,
        unit: "points",
        role: "applied",
        attributionConfidence: "exact",
        iconSemanticKey: "damage",
        iconAssetRelativeUrl:
          "../report-assets/objects/00/0000000000000000000000000000000000000000000000000000000000000000.png",
      }),
      schemaEvent({
        eventId: "charge-1",
        frame: 60,
        frameSequence: 0,
        combatTimeMs: 3000,
        kind: "effect-executed",
        action: "CardCharge",
        sourceEntityId: "player-skill",
        triggerSourceEntityId: "player-skill",
        targetEntityIds: ["player-item"],
        value: 7_850,
        unit: "milliseconds",
        role: "applied",
        attributionConfidence: "exact",
      }),
      schemaEvent({
        eventId: "burn-1",
        frame: 80,
        frameSequence: 0,
        combatTimeMs: 4000,
        kind: "effect-executed",
        action: "PlayerBurnApply",
        sourceEntityId: "opponent-item",
        triggerSourceEntityId: "opponent-item",
        targetEntityIds: ["player-hero"],
        value: 12,
        unit: "points",
        role: "applied",
        attributionConfidence: "exact",
      }),
      schemaEvent({
        eventId: "heal-1",
        frame: 100,
        frameSequence: 0,
        combatTimeMs: 5000,
        kind: "effect-executed",
        action: "PlayerHeal",
        sourceEntityId: "player-item",
        triggerSourceEntityId: "player-item",
        targetEntityIds: ["player-hero"],
        value: 40,
        unit: "points",
        role: "applied",
        attributionConfidence: "exact",
      }),
    ],
  },
  recordingManifest: {
    schemaVersion: 1,
    artifactId: "behavior-artifact",
    recordingId: "behavior-recording",
    battleId: "behavior-battle",
    videoRelativeUrl: "",
    syncMetadataStatus: "NotRequested",
    syncAnchors: [],
    assets: [],
  },
};

let fixtureDirectory;
let reportUrl;
let chartReportUrl;
let damageKindsReportUrl;
let directDamageSummaryReportUrl;
let denseReportUrl;
let markerLayoutReportUrl;
let statusApplicationReportUrl;
let structuralReportUrl;
let recordingReportUrl;
let scrubRecordingReportUrl;
let navigationReportUrl;
let scrollRecordingReportUrl;

function serializedEnvelope(value) {
  return JSON.stringify(value).replaceAll("<", "\\u003c");
}

function reportHtml(envelope) {
  return `<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8">
    <meta
      http-equiv="Content-Security-Policy"
      content="default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; media-src 'self'"
    >
    <link rel="stylesheet" href="./viewer.css">
    <script type="application/json" id="bpp-report-data">${serializedEnvelope(envelope)}</script>
    <script defer src="./viewer.js"></script>
  </head>
  <body>
    <main data-bpp-test-id="report-root"></main>
  </body>
</html>`;
}

test.beforeAll(async ({ browserName }) => {
  fixtureDirectory = await mkdtemp(
    join(tmpdir(), `bpp-viewer-${process.pid}-${browserName}-`),
  );
  await copyFile(
    join(viewerArtifactDirectory, "viewer.js"),
    join(fixtureDirectory, "viewer.js"),
  );
  await copyFile(
    join(viewerArtifactDirectory, "viewer.css"),
    join(fixtureDirectory, "viewer.css"),
  );
  await writeFile(
    join(fixtureDirectory, "report.html"),
    reportHtml(fixtureEnvelope),
    "utf8",
  );
  const chartEnvelope = structuredClone(fixtureEnvelope);
  chartEnvelope.battleDocument.events.push(
    schemaEvent({
      eventId: "chart-player-damage",
      frame: 110,
      frameSequence: 0,
      combatTimeMs: 5500,
      kind: "health",
      action: "Health:Damage",
      sourceEntityId: "player-item",
      triggerSourceEntityId: "player-item",
      targetEntityIds: ["opponent-hero"],
      value: -200,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "chart-player-burn",
      frame: 111,
      frameSequence: 0,
      combatTimeMs: 5550,
      kind: "health",
      action: "Health:Burn",
      sourceEntityId: "player-item",
      triggerSourceEntityId: "player-item",
      targetEntityIds: ["opponent-hero"],
      value: -70,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "chart-player-poison",
      frame: 112,
      frameSequence: 0,
      combatTimeMs: 5600,
      kind: "health",
      action: "Health:Poison",
      sourceEntityId: "player-item",
      triggerSourceEntityId: "player-item",
      targetEntityIds: ["opponent-hero"],
      value: -50,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "chart-opponent-damage",
      frame: 120,
      frameSequence: 0,
      combatTimeMs: 6000,
      kind: "health",
      action: "Health:Damage",
      sourceEntityId: "opponent-item",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["player-hero"],
      value: -100,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "chart-opponent-burn",
      frame: 121,
      frameSequence: 0,
      combatTimeMs: 6050,
      kind: "health",
      action: "Health:Burn",
      sourceEntityId: "opponent-item",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["player-hero"],
      value: -50,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "chart-opponent-poison",
      frame: 122,
      frameSequence: 0,
      combatTimeMs: 6100,
      kind: "health",
      action: "Health:Poison",
      sourceEntityId: "opponent-item",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["player-hero"],
      value: -30,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
  );
  chartEnvelope.battleDocument.rawRecordCount += 6;
  await writeFile(
    join(fixtureDirectory, "chart-report.html"),
    reportHtml(chartEnvelope),
    "utf8",
  );
  const damageKindsEnvelope = structuredClone(fixtureEnvelope);
  damageKindsEnvelope.battleDocument.events = [
    schemaEvent({
      ...damageKindsEnvelope.battleDocument.events[0],
      eventId: "inspector-direct-damage",
      value: 40,
    }),
    schemaEvent({
      eventId: "inspector-burn",
      frame: 40,
      frameSequence: 1,
      combatTimeMs: 2000,
      kind: "effect-executed",
      action: "PlayerBurnApply",
      sourceEntityId: "player-skill",
      triggerSourceEntityId: "player-skill",
      targetEntityIds: ["opponent-hero"],
      value: 12,
      unit: "points",
      role: "applied",
      attributionConfidence: "exact",
      iconSemanticKey: "status.burn",
      iconAssetRelativeUrl:
        "../report-assets/objects/11/1111111111111111111111111111111111111111111111111111111111111111.png",
    }),
    schemaEvent({
      eventId: "inspector-poison",
      frame: 40,
      frameSequence: 2,
      combatTimeMs: 2000,
      kind: "effect-executed",
      action: "PlayerPoisonApply",
      sourceEntityId: "player-item",
      triggerSourceEntityId: "player-item",
      targetEntityIds: ["opponent-hero"],
      value: 6,
      unit: "points",
      role: "applied",
      attributionConfidence: "exact",
      iconSemanticKey: "status.poison",
      iconAssetRelativeUrl:
        "../report-assets/objects/22/2222222222222222222222222222222222222222222222222222222222222222.png",
    }),
  ];
  damageKindsEnvelope.battleDocument.rawRecordCount = 3;
  await writeFile(
    join(fixtureDirectory, "damage-kinds-report.html"),
    reportHtml(damageKindsEnvelope),
    "utf8",
  );
  const directDamageSummaryEnvelope = structuredClone(fixtureEnvelope);
  directDamageSummaryEnvelope.battleDocument.entities.find(
    (entity) => entity.entityId === "player-item",
  ).name = "Silver Stake";
  directDamageSummaryEnvelope.battleDocument.entities.find(
    (entity) => entity.entityId === "player-skill",
  ).name = "Wolf";
  directDamageSummaryEnvelope.battleDocument.events = [
    schemaEvent({
      eventId: "direct-silver-stake",
      frame: 40,
      frameSequence: 0,
      combatTimeMs: 2000,
      kind: "effect-executed",
      action: "PlayerDamage",
      sourceEntityId: "player-item",
      triggerSourceEntityId: "player-item",
      targetEntityIds: ["opponent-hero"],
      unit: "points",
      role: "applied",
      attributionConfidence: "exact",
      iconSemanticKey: "damage",
      iconAssetRelativeUrl:
        "../report-assets/objects/33/3333333333333333333333333333333333333333333333333333333333333333.png",
    }),
    schemaEvent({
      eventId: "direct-wolf",
      frame: 40,
      frameSequence: 1,
      combatTimeMs: 2000,
      kind: "effect-executed",
      action: "PlayerDamage",
      sourceEntityId: "player-skill",
      triggerSourceEntityId: "player-skill",
      targetEntityIds: ["opponent-hero"],
      unit: "points",
      role: "applied",
      attributionConfidence: "exact",
      iconSemanticKey: "damage",
      iconAssetRelativeUrl:
        "../report-assets/objects/33/3333333333333333333333333333333333333333333333333333333333333333.png",
    }),
    schemaEvent({
      eventId: "direct-shield-settlement-a",
      frame: 40,
      frameSequence: 8,
      combatTimeMs: 2000,
      kind: "health",
      action: "Shield:Damage",
      targetEntityIds: ["opponent-hero"],
      value: -740,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
    }),
    schemaEvent({
      eventId: "direct-shield-settlement-b",
      frame: 40,
      frameSequence: 9,
      combatTimeMs: 2000,
      kind: "health",
      action: "Shield:Damage",
      targetEntityIds: ["opponent-hero"],
      value: -690,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
    }),
    schemaEvent({
      eventId: "direct-shield-aggregate",
      frame: 40,
      frameSequence: 10,
      combatTimeMs: 2000,
      kind: "player-attribute",
      action: "Shield",
      targetEntityIds: ["opponent-hero"],
      value: -1430,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
    }),
  ];
  directDamageSummaryEnvelope.battleDocument.rawRecordCount = 5;
  await writeFile(
    join(fixtureDirectory, "direct-damage-summary-report.html"),
    reportHtml(directDamageSummaryEnvelope),
    "utf8",
  );
  const denseEnvelope = structuredClone(fixtureEnvelope);
  const repeatedDamage = Array.from({ length: 80 }, (_, index) => ({
    ...denseEnvelope.battleDocument.events[0],
    eventId: `dense-damage-${index}`,
    frameSequence: index,
    targetEntityIds: ["opponent-hero"],
    value: index + 1,
  }));
  const sameFrameHeal = {
    ...denseEnvelope.battleDocument.events[0],
    eventId: "dense-heal",
    frameSequence: 80,
    action: "PlayerHeal",
    sourceEntityId: "opponent-item",
    triggerSourceEntityId: "opponent-item",
    targetEntityIds: ["opponent-hero"],
    value: 50,
  };
  denseEnvelope.battleDocument.events = [
    ...repeatedDamage,
    sameFrameHeal,
    ...denseEnvelope.battleDocument.events.slice(1),
  ];
  denseEnvelope.battleDocument.rawRecordCount = 93;
  await writeFile(
    join(fixtureDirectory, "dense-report.html"),
    reportHtml(denseEnvelope),
    "utf8",
  );
  const markerLayoutEnvelope = structuredClone(fixtureEnvelope);
  markerLayoutEnvelope.battleDocument.events = [
    ["marker-direct", "PlayerDamage"],
    ["marker-burn", "PlayerBurnApply"],
    ["marker-poison", "PlayerPoisonApply"],
    ["marker-heal", "PlayerHeal"],
    ["marker-shield", "PlayerShieldApply"],
  ].map(([eventId, action], frameSequence) =>
    schemaEvent({
      eventId,
      frame: 80,
      frameSequence,
      combatTimeMs: 4000,
      kind: "effect-executed",
      action,
      sourceEntityId: "opponent-item",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["player-hero"],
      value: 10 + frameSequence,
      unit: "points",
      role: "applied",
      attributionConfidence: "exact",
    })
  );
  markerLayoutEnvelope.battleDocument.rawRecordCount = 5;
  await writeFile(
    join(fixtureDirectory, "marker-layout-report.html"),
    reportHtml(markerLayoutEnvelope),
    "utf8",
  );
  const structuralEnvelope = structuredClone(fixtureEnvelope);
  structuralEnvelope.battleDocument.entities.find(
    (entity) => entity.entityId === "player-item",
  ).name = "Ice Swan";
  structuralEnvelope.battleDocument.entities.find(
    (entity) => entity.entityId === "opponent-item",
  ).name = "Disintegration Ray";
  structuralEnvelope.battleDocument.entities.push(
    {
      entityId: "player-item-multicast",
      owner: "player",
      type: "item",
      name: "Zarlic",
      span: 1,
      order: 2,
    },
    {
      entityId: "player-item-cooldown",
      owner: "player",
      type: "item",
      name: "Sorbet",
      span: 1,
      order: 3,
    },
  );
  structuralEnvelope.battleDocument.events = [
    schemaEvent({
      eventId: "structural-destroy",
      frame: 80,
      frameSequence: 0,
      combatTimeMs: 4000,
      kind: "effect-executed",
      action: "CardDisable",
      sourceEntityId: "opponent-item",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["player-item"],
      role: "applied",
      attributionConfidence: "exact",
      iconSemanticKey: "status.destroy",
    }),
    schemaEvent({
      eventId: "structural-damage",
      frame: 80,
      frameSequence: 1,
      combatTimeMs: 4000,
      kind: "card-attribute",
      action: "DamageAmount",
      targetEntityIds: ["player-item-multicast"],
      value: 20,
      previousValue: 10,
      currentValue: 30,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
      iconSemanticKey: "status.damage",
    }),
    schemaEvent({
      eventId: "structural-multicast",
      frame: 80,
      frameSequence: 2,
      combatTimeMs: 4000,
      kind: "card-attribute",
      action: "Multicast",
      targetEntityIds: ["player-item-multicast"],
      value: -1,
      previousValue: 2,
      currentValue: 1,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
      iconSemanticKey: "status.multicast",
    }),
    schemaEvent({
      eventId: "structural-cooldown",
      frame: 80,
      frameSequence: 3,
      combatTimeMs: 4000,
      kind: "card-attribute",
      action: "PercentCooldownReduction",
      targetEntityIds: ["player-item-cooldown"],
      value: 10,
      previousValue: 10,
      currentValue: 20,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
      iconSemanticKey: "status.cooldownReduction",
    }),
    schemaEvent({
      eventId: "structural-crit",
      frame: 80,
      frameSequence: 4,
      combatTimeMs: 4000,
      kind: "card-attribute",
      action: "CritChance",
      targetEntityIds: ["player-item-cooldown"],
      value: 5,
      previousValue: 0,
      currentValue: 5,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
      iconSemanticKey: "status.critChance",
    }),
    schemaEvent({
      eventId: "structural-multi-target-haste",
      frame: 100,
      frameSequence: 0,
      combatTimeMs: 5000,
      kind: "effect-executed",
      action: "CardHaste",
      sourceEntityId: "opponent-item",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: [
        "player-item-multicast",
        "player-item-cooldown",
      ],
      value: 2000,
      unit: "ms",
      role: "applied",
      attributionConfidence: "exact",
      iconSemanticKey: "status.haste",
    }),
  ];
  structuralEnvelope.battleDocument.rawRecordCount = 6;
  await writeFile(
    join(fixtureDirectory, "structural-report.html"),
    reportHtml(structuralEnvelope),
    "utf8",
  );
  const statusApplicationEnvelope = structuredClone(fixtureEnvelope);
  const sourceSkill = statusApplicationEnvelope.battleDocument.entities.find(
    (entity) => entity.entityId === "player-skill",
  );
  sourceSkill.name = "Petrifying Gaze";
  statusApplicationEnvelope.battleDocument.entities.push({
    entityId: "opponent-item-2",
    owner: "opponent",
    type: "item",
    name: "Cash Cannon",
    span: 2,
    order: 2,
  });
  statusApplicationEnvelope.battleDocument.events.push(
    schemaEvent({
      eventId: "freeze-application-1",
      frame: 169,
      frameSequence: 0,
      combatTimeMs: 8450,
      kind: "effect-executed",
      action: "CardFreeze",
      sourceEntityId: "player-skill",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["opponent-item"],
      value: 1000,
      unit: "milliseconds",
      role: "applied",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "freeze-application-2",
      frame: 169,
      frameSequence: 1,
      combatTimeMs: 8450,
      kind: "effect-executed",
      action: "CardFreeze",
      sourceEntityId: "player-skill",
      triggerSourceEntityId: "opponent-item",
      targetEntityIds: ["opponent-item-2"],
      value: 1000,
      unit: "milliseconds",
      role: "applied",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "freeze-countdown-tick",
      frame: 170,
      frameSequence: 0,
      combatTimeMs: 8500,
      kind: "card-attribute",
      action: "Freeze",
      targetEntityIds: ["opponent-item"],
      previousValue: 1000,
      currentValue: 950,
      value: -50,
      unit: "milliseconds",
      role: "received",
      attributionConfidence: "unavailable",
    }),
    schemaEvent({
      eventId: "native-heal-settlement",
      frame: 171,
      frameSequence: 0,
      combatTimeMs: 8550,
      kind: "player-attribute",
      action: "Health",
      targetEntityIds: ["player-hero"],
      value: 20,
      previousValue: 2_222,
      currentValue: 2_242,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
      iconSemanticKey: "status.heal",
      iconContentKey:
        "1111111111111111111111111111111111111111111111111111111111111111",
      iconAssetRelativeUrl:
        "../report-assets/objects/11/1111111111111111111111111111111111111111111111111111111111111111.png",
    }),
    schemaEvent({
      eventId: "health-max-increase",
      frame: 171,
      frameSequence: 1,
      combatTimeMs: 8550,
      kind: "player-attribute",
      action: "HealthMax",
      targetEntityIds: ["player-hero"],
      value: 20,
      previousValue: 2_175,
      currentValue: 2_195,
      unit: "points",
      role: "received",
      attributionConfidence: "target-exact-source-unknown",
    }),
  );
  statusApplicationEnvelope.battleDocument.durationMs = 9000;
  statusApplicationEnvelope.battleDocument.frameCount = 180;
  statusApplicationEnvelope.battleDocument.rawRecordCount += 5;
  await writeFile(
    join(fixtureDirectory, "status-application-report.html"),
    reportHtml(statusApplicationEnvelope),
    "utf8",
  );
  const recordingEnvelope = structuredClone(fixtureEnvelope);
  recordingEnvelope.recordingManifest = {
    schemaVersion: 1,
    artifactId: "behavior-recording-artifact",
    battleId: "behavior-battle",
    recordingId: "behavior-recording",
    syncMetadataStatus: "ReadyExact",
    syncAnchors: [
      { combatFrame: 0, combatMs: 0, mediaPtsMs: 0, outputOrdinal: 0 },
      {
        combatFrame: 160,
        combatMs: 8000,
        mediaPtsMs: 8000,
        outputOrdinal: 160,
      },
    ],
    videoRelativeUrl:
      "../CombatReplayVideos/behavior-recording/recording.mp4",
    assets: [],
  };
  await writeFile(
    join(fixtureDirectory, "recording-report.html"),
    reportHtml(recordingEnvelope),
    "utf8",
  );
  const scrubRecordingEnvelope = structuredClone(recordingEnvelope);
  scrubRecordingEnvelope.recordingManifest.scrubVideoRelativeUrl =
    "../CombatReplayVideos/behavior-recording/recording.scrub.mp4";
  await writeFile(
    join(fixtureDirectory, "scrub-recording-report.html"),
    reportHtml(scrubRecordingEnvelope),
    "utf8",
  );
  const navigationEnvelope = structuredClone(fixtureEnvelope);
  navigationEnvelope.battleDocument.events = [
    schemaEvent({
      eventId: "hidden-metric",
      frame: 10,
      frameSequence: 0,
      combatTimeMs: 500,
      kind: "player-attribute",
      action: "Health",
      targetEntityIds: ["player-hero"],
      value: 980,
      unit: "points",
      role: "received",
      attributionConfidence: "unavailable",
    }),
    schemaEvent({
      eventId: "hidden-health",
      frame: 20,
      frameSequence: 0,
      combatTimeMs: 1000,
      kind: "health",
      action: "Damage",
      sourceEntityId: "opponent-item",
      targetEntityIds: ["player-hero"],
      value: -20,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
    schemaEvent({
      eventId: "hidden-rage",
      frame: 30,
      frameSequence: 0,
      combatTimeMs: 1500,
      kind: "effect-executed",
      action: "PlayerRageApply",
      sourceEntityId: "player-skill",
      targetEntityIds: ["player-hero"],
      value: 1,
      unit: "points",
      role: "applied",
      attributionConfidence: "exact",
    }),
    ...navigationEnvelope.battleDocument.events,
  ];
  navigationEnvelope.battleDocument.rawRecordCount += 3;
  await writeFile(
    join(fixtureDirectory, "navigation-report.html"),
    reportHtml(navigationEnvelope),
    "utf8",
  );
  const scrollRecordingEnvelope = structuredClone(recordingEnvelope);
  scrollRecordingEnvelope.battleDocument.entities.push(
    ...Array.from({ length: 28 }, (_, index) => ({
      entityId: `extra-skill-${index}`,
      owner: index < 14 ? "player" : "opponent",
      type: "skill",
      name: `Extra Skill ${index + 1}`,
      span: 1,
      order: index + 10,
    })),
  );
  await writeFile(
    join(fixtureDirectory, "scroll-recording-report.html"),
    reportHtml(scrollRecordingEnvelope),
    "utf8",
  );
  reportUrl = pathToFileURL(join(fixtureDirectory, "report.html")).href;
  chartReportUrl = pathToFileURL(
    join(fixtureDirectory, "chart-report.html"),
  ).href;
  damageKindsReportUrl = pathToFileURL(
    join(fixtureDirectory, "damage-kinds-report.html"),
  ).href;
  directDamageSummaryReportUrl = pathToFileURL(
    join(fixtureDirectory, "direct-damage-summary-report.html"),
  ).href;
  denseReportUrl = pathToFileURL(
    join(fixtureDirectory, "dense-report.html"),
  ).href;
  markerLayoutReportUrl = pathToFileURL(
    join(fixtureDirectory, "marker-layout-report.html"),
  ).href;
  statusApplicationReportUrl = pathToFileURL(
    join(fixtureDirectory, "status-application-report.html"),
  ).href;
  structuralReportUrl = pathToFileURL(
    join(fixtureDirectory, "structural-report.html"),
  ).href;
  recordingReportUrl = pathToFileURL(
    join(fixtureDirectory, "recording-report.html"),
  ).href;
  scrubRecordingReportUrl = pathToFileURL(
    join(fixtureDirectory, "scrub-recording-report.html"),
  ).href;
  navigationReportUrl = pathToFileURL(
    join(fixtureDirectory, "navigation-report.html"),
  ).href;
  scrollRecordingReportUrl = pathToFileURL(
    join(fixtureDirectory, "scroll-recording-report.html"),
  ).href;
});

test.afterAll(async () => {
  if (fixtureDirectory) {
    await rm(fixtureDirectory, { recursive: true, force: true });
  }
});

test("boots the file report with one assembled script and one stylesheet", async ({
  page,
}) => {
  const externalRequests = [];
  const pageErrors = [];
  page.on("request", (request) => {
    if (/^https?:/iu.test(request.url())) externalRequests.push(request.url());
  });
  page.on("pageerror", (error) => pageErrors.push(error.message));

  await page.goto(`${reportUrl}?lang=en`);

  await expect(page.locator("html")).toHaveClass(/bpp-report-ready/u);
  await expect(page.getByTestId("report-error-state")).toHaveCount(0);
  await expect(page.getByTestId("match-title")).toHaveText(
    "Fixture Player vs Fixture Opponent",
  );
  await expect(page.getByTestId("match-outcome")).toHaveText("Victory");
  await expect(page.getByTestId("timeline-canvas")).toBeVisible();
  await expect(page.getByTestId("timeline-lane-labels")).toContainText(
    "Training Blade",
  );
  await expect(page.getByTestId("timeline-lane-labels")).toContainText(
    "Quick Thinking",
  );
  await expect(
    page
      .getByTestId("timeline-lane-1")
      .locator('[data-asset-status="missing"]'),
  ).toHaveCount(1);
  await expect(
    page.getByTestId("recording-visibility-toggle"),
  ).toHaveCount(0);
  expect(externalRequests).toEqual([]);
  expect(pageErrors).toEqual([]);
});

test("uses the shadcn primitive layer and keeps every lane label aligned", async ({
  page,
}) => {
  await page.setViewportSize({ width: 808, height: 857 });
  await page.goto(`${reportUrl}?lang=en`);

  await expect(page.locator('[data-slot="tabs"]')).toHaveCount(1);
  await expect(page.locator('[data-slot="button"]')).not.toHaveCount(0);
  await expect(page.locator('[data-slot="toggle-group"]')).toHaveCount(1);
  await expect(
    page
      .getByTestId("timeline-scroll")
      .locator("xpath=ancestor::*[@data-slot='scroll-area']"),
  ).toHaveCount(0);

  const laneGeometry = await page
    .getByTestId("timeline-lane-labels")
    .evaluate((labels) => {
      const rows = Array.from(
        labels.querySelectorAll("[data-bpp-lane-index]"),
      );
      return rows.map((row) => {
        const slot = row.querySelector(".bpp-lane-art-slot");
        const copy = row.querySelector(".bpp-lane-copy");
        const art = slot?.querySelector("[data-bpp-test-id]");
        const slotBounds = slot?.getBoundingClientRect();
        const copyBounds = copy?.getBoundingClientRect();
        const artBounds = art?.getBoundingClientRect();
        return {
          artCenter:
            artBounds && slotBounds
              ? artBounds.left + artBounds.width / 2 - slotBounds.left
              : 0,
          artHeight: artBounds?.height ?? 0,
          artSpan: Number(art?.getAttribute("data-entity-span") ?? 0),
          artType: art?.getAttribute("data-entity-type") ?? "",
          artWidth: artBounds?.width ?? 0,
          slotWidth: slotBounds?.width ?? 0,
          copyLeft: copyBounds?.left ?? 0,
        };
      });
    });
  expect(laneGeometry.length).toBeGreaterThanOrEqual(5);
  expect(laneGeometry.every(({ slotWidth }) => slotWidth === 132)).toBe(true);
  const firstCopyLeft = laneGeometry[0].copyLeft;
  expect(
    laneGeometry.every(
      ({ copyLeft }) => Math.abs(copyLeft - firstCopyLeft) < 0.5,
    ),
  ).toBe(true);
  expect(
    laneGeometry.every(
      ({ artCenter, slotWidth }) =>
        Math.abs(artCenter - slotWidth / 2) < 0.5,
    ),
  ).toBe(true);
  expect(
    laneGeometry.every(({ artHeight }) => artHeight > 0 && artHeight <= 44),
  ).toBe(true);
  expect(
    laneGeometry
      .filter(({ artType }) => artType === "item")
      .every(({ artSpan, artWidth }) => artWidth === 44 * artSpan),
  ).toBe(true);
  expect(
    laneGeometry
      .filter(({ artType }) => artType === "skill")
      .every(({ artHeight, artWidth }) => artHeight === 36 && artWidth === 36),
  ).toBe(true);

  const activeTabStyle = await page
    .getByTestId("report-tab-timeline")
    .evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        background: style.backgroundColor,
        borderBottomColor: style.borderBottomColor,
        borderBottomWidth: style.borderBottomWidth,
        radius: style.borderRadius,
        font: style.fontFamily,
      };
    });
  expect(activeTabStyle.background).toBe("rgba(0, 0, 0, 0)");
  expect(activeTabStyle.borderBottomColor).not.toBe(
    "rgba(0, 0, 0, 0)",
  );
  expect(activeTabStyle.borderBottomWidth).toBe("2px");
  expect(activeTabStyle.radius).toBe("0px");
  expect(activeTabStyle.font).toContain("system-ui");

  const headerControlMetrics = await page.evaluate(() =>
    [
      document.querySelector('[data-slot="tabs-list"]'),
      document.querySelector('[data-bpp-test-id="locale-switch"]'),
    ].map((element) => ({
      height: element?.getBoundingClientRect().height ?? 0,
      radius: element ? getComputedStyle(element).borderRadius : "",
    })),
  );
  expect(headerControlMetrics.map(({ height }) => height)).toEqual([32, 32]);
  expect(headerControlMetrics[0].radius).toBe("0px");
  expect(headerControlMetrics[1].radius).not.toBe("0px");
  await expect(page.getByTestId("report-root")).toHaveCSS(
    "font-size",
    "13px",
  );
  await expect(
    page
      .getByTestId("state-band-labels")
      .locator('[data-slot="toggle-group-item"]')
      .first(),
  ).toHaveCSS("height", "28px");
  await expect(page.getByTestId("time-zoom-out")).toHaveCSS(
    "height",
    "28px",
  );
  await expect(page.getByTestId("timeline-zoom-toolbar")).toBeAttached();
  await expect(page.getByTestId("timeline-legend")).toHaveCount(0);
  expect(
    await page
      .getByTestId("timeline-zoom-toolbar")
      .evaluate(
        (element) =>
          element.closest('[data-bpp-test-id="workbench-footer"]') !== null,
      ),
  ).toBe(true);

  const timelineTabBefore = await page
    .getByTestId("report-tab-timeline")
    .evaluate((element) => {
      const bounds = element.getBoundingClientRect();
      return {
        left: bounds.left,
        top: bounds.top,
        width: bounds.width,
      };
    });
  await page.getByTestId("report-tab-statistics").click();
  await expect(page.locator('[data-slot="table"]')).toHaveCount(1);
  await expect(
    page.getByTestId("statistics-activity-sort-damage"),
  ).toHaveCSS("height", "36px");
  const timelineTabAfter = await page
    .getByTestId("report-tab-timeline")
    .evaluate((element) => {
      const bounds = element.getBoundingClientRect();
      return {
        left: bounds.left,
        top: bounds.top,
        width: bounds.width,
      };
    });
  expect(Math.abs(timelineTabAfter.left - timelineTabBefore.left)).toBeLessThan(
    0.5,
  );
  expect(Math.abs(timelineTabAfter.top - timelineTabBefore.top)).toBeLessThan(
    0.5,
  );
  expect(
    Math.abs(timelineTabAfter.width - timelineTabBefore.width),
  ).toBeLessThan(0.5);
  await expect(page.getByTestId("timeline-zoom-toolbar")).toBeHidden();
});

test("separates both sides and filters lanes reversibly", async ({ page }) => {
  await page.setViewportSize({ width: 999, height: 857 });
  await page.goto(`${reportUrl}?lang=en`);

  const stickyHeroPaintedPixels = () =>
    page.getByTestId("timeline-sticky-hero-events").evaluate((canvas) => {
      const context = canvas.getContext("2d");
      const pixels = context.getImageData(
        0,
        0,
        canvas.width,
        canvas.height,
      ).data;
      let painted = 0;
      for (let index = 3; index < pixels.length; index += 16) {
        if (pixels[index] !== 0) painted += 1;
      }
      return painted;
    });
  await expect(page.getByTestId("timeline-sticky-hero-label")).toBeHidden();
  await expect.poll(stickyHeroPaintedPixels).toBe(0);
  const opponentBoundary = page.getByTestId("timeline-lane-3");
  await expect(opponentBoundary).toHaveAttribute(
    "data-bpp-entity-id",
    "opponent-hero",
  );
  await expect(opponentBoundary).toHaveAttribute(
    "data-bpp-side-boundary",
    "opponent",
  );
  const readBoundaryGeometry = () => page.evaluate(() => {
    const row = document.querySelector(
      '[data-bpp-side-boundary="opponent"]',
    );
    const canvas = document.querySelector(
      '[data-bpp-test-id="timeline-scene-canvas"]',
    );
    if (!(row instanceof HTMLElement) || !(canvas instanceof HTMLCanvasElement)) {
      return null;
    }
    const rowBounds = row.getBoundingClientRect();
    const canvasBounds = canvas.getBoundingClientRect();
    const scale = canvas.width / Number.parseFloat(canvas.style.width);
    const context = canvas.getContext("2d");
    const pixelAt = (logicalY) =>
      Array.from(
        context.getImageData(
          Math.round(120 * scale),
          Math.round(logicalY * scale),
          1,
          1,
        ).data,
      );
    return {
      boundaryLane: Number(canvas.dataset.bppSideBoundaryLane),
      canvasOffset: rowBounds.top - canvasBounds.top,
      separatorHeight: getComputedStyle(row, "::before").height,
      boundaryPixel: pixelAt(3 * 52 + 1),
      regularPixel: pixelAt(1 * 52 + 1),
    };
  });
  let boundaryGeometry = await readBoundaryGeometry();
  expect(boundaryGeometry).not.toBeNull();
  expect(boundaryGeometry.boundaryLane).toBe(3);
  expect(Math.abs(boundaryGeometry.canvasOffset - 3 * 52)).toBeLessThan(0.5);
  expect(boundaryGeometry.separatorHeight).toBe("2px");
  await expect.poll(async () => {
    boundaryGeometry = await readBoundaryGeometry();
    return boundaryGeometry !== null
      && boundaryGeometry.boundaryPixel[3] > 0
      && boundaryGeometry.regularPixel[3] > 0
      && boundaryGeometry.boundaryPixel.join(",")
        !== boundaryGeometry.regularPixel.join(",");
  }).toBe(true);
  expect(boundaryGeometry.boundaryPixel).not.toEqual(
    boundaryGeometry.regularPixel,
  );

  await page.getByTestId("lane-filter-trigger").click();
  await expect(page.getByTestId("lane-filter-popover")).toBeVisible();
  await expect(page.getByTestId("lane-filter-popover")).toHaveClass(
    /data-\[state=open\]:animate-in/,
  );
  await expect(
    page
      .getByTestId("lane-filter-popover")
      .locator('[data-slot="checkbox"]'),
  ).toHaveCount(6);

  await page.getByTestId("lane-filter-skill").click();
  await expect(page.getByTestId("timeline-sticky-hero-label")).toBeHidden();
  await expect.poll(stickyHeroPaintedPixels).toBe(0);
  await expect(page.getByTestId("timeline-lane-labels")).not.toContainText(
    "Quick Thinking",
  );
  await expect(page.getByTestId("timeline-lane-2")).toHaveAttribute(
    "data-bpp-entity-id",
    "opponent-hero",
  );
  await expect(page.getByTestId("timeline-lane-2")).toHaveAttribute(
    "data-bpp-side-boundary",
    "opponent",
  );
  await expect(page.getByTestId("timeline-canvas")).toHaveAttribute(
    "data-bpp-side-boundary-lane",
    "2",
  );

  await page.getByTestId("lane-filter-player").click();
  await expect(
    page.locator("[data-bpp-entity-id]"),
  ).toHaveCount(2);
  await expect(page.getByTestId("timeline-lane-0")).toHaveAttribute(
    "data-bpp-entity-id",
    "opponent-hero",
  );
  await expect(
    page.locator('[data-bpp-side-boundary="opponent"]'),
  ).toHaveCount(0);

  await page.getByTestId("report-tab-statistics").click();
  await page.getByTestId("report-tab-timeline").click();
  await expect(
    page.locator("[data-bpp-entity-id]"),
  ).toHaveCount(2);
  await expect(page.getByTestId("lane-filter-hidden-count")).toHaveText("2");

  await page.getByTestId("lane-filter-trigger").click();
  await page.getByTestId("lane-filter-reset").click();
  await expect(
    page.locator("[data-bpp-entity-id]"),
  ).toHaveCount(5);
  await expect(page.getByTestId("timeline-lane-3")).toHaveAttribute(
    "data-bpp-side-boundary",
    "opponent",
  );

  await page.getByTestId("lane-filter-hero").click();
  await expect(page.getByTestId("timeline-sticky-hero-label")).toBeHidden();
  await expect(page.getByTestId("timeline-sticky-hero-events")).toBeHidden();
  await page.getByTestId("lane-filter-item").click();
  await page.getByTestId("lane-filter-skill").click();
  await page.getByTestId("lane-filter-effect").click();
  await expect(page.locator("[data-bpp-entity-id]")).toHaveCount(0);
  await expect(page.getByTestId("timeline-lane-filter-empty")).toBeVisible();
  await expect(page.getByTestId("timeline-canvas")).toHaveCSS(
    "height",
    "52px",
  );
  await page.getByTestId("lane-filter-reset").click();
  await expect(page.locator("[data-bpp-entity-id]")).toHaveCount(5);
  await expect(page.getByTestId("timeline-sticky-hero-label")).toBeHidden();
});

test("pins one aligned hero lane and replaces it at the opponent section", async ({
  page,
}) => {
  await page.setViewportSize({ width: 999, height: 857 });
  await page.goto(`${scrollRecordingReportUrl}?lang=en`);

  const stickyLabel = page.getByTestId("timeline-sticky-hero-label");
  const stickyCanvas = page.getByTestId("timeline-sticky-hero-events");
  const timelineScroll = page.getByTestId("timeline-scroll");
  const laneLabels = page.getByTestId("timeline-lane-labels");
  const playerHeroLane = laneLabels.locator(
    '[data-bpp-entity-id="player-hero"]',
  );
  const opponentHeroLane = laneLabels.locator(
    '[data-bpp-entity-id="opponent-hero"]',
  );
  const opponentLane = Number(
    await opponentHeroLane.getAttribute("data-bpp-lane-index"),
  );
  expect(opponentLane).toBeGreaterThan(0);
  const visibleHeroAvatarCount = (entityId) =>
    page.evaluate((heroEntityId) => {
      const scroll = document.querySelector(
        '[data-bpp-test-id="timeline-scroll"]',
      );
      if (!(scroll instanceof HTMLElement)) return 0;
      const scrollBounds = scroll.getBoundingClientRect();
      const labels = document.querySelector(
        '[data-bpp-test-id="timeline-lane-labels"]',
      );
      const originalLane = Array.from(
        labels?.querySelectorAll("[data-bpp-entity-id]") ?? [],
      ).find(
        (lane) => lane.getAttribute("data-bpp-entity-id") === heroEntityId,
      );
      const stickyLane = document.querySelector(
        '[data-bpp-test-id="timeline-sticky-hero-label"]',
      );
      const avatars = [
        originalLane?.querySelector(
          '[data-bpp-test-id^="timeline-lane-icon-"]',
        ),
        stickyLane?.getAttribute("data-bpp-sticky-hero-entity-id")
          === heroEntityId
          ? stickyLane.querySelector(
            '[data-bpp-test-id="timeline-sticky-hero-icon"]',
          )
          : null,
      ].filter(Boolean);
      return Array.from(avatars).filter((avatar) => {
        if (!(avatar instanceof HTMLElement) || avatar.closest("[hidden]")) {
          return false;
        }
        const bounds = avatar.getBoundingClientRect();
        return bounds.width > 0
          && bounds.height > 0
          && bounds.right > scrollBounds.left
          && bounds.left < scrollBounds.right
          && bounds.bottom > scrollBounds.top
          && bounds.top < scrollBounds.bottom;
      }).length;
    }, entityId);

  await timelineScroll.evaluate((scroll) => {
    scroll.scrollTop = 0;
    scroll.dispatchEvent(new Event("scroll"));
  });
  await expect
    .poll(() => timelineScroll.evaluate((scroll) => scroll.scrollTop))
    .toBe(0);
  await expect(stickyLabel).toBeHidden();
  await expect(stickyCanvas).toBeHidden();
  await expect(stickyCanvas).toHaveAttribute(
    "data-bpp-hero-health-visible",
    "false",
  );
  await expect(page.getByTestId("timeline-lane-icon-0")).toBeVisible();
  await expect
    .poll(() => visibleHeroAvatarCount("player-hero"))
    .toBe(1);

  await timelineScroll.evaluate((scroll) => {
    scroll.scrollTop = 53;
  });
  await expect(stickyLabel).toBeVisible();
  await expect(stickyCanvas).toBeVisible();
  await expect(stickyCanvas).toHaveAttribute(
    "data-bpp-hero-health-visible",
    "true",
  );
  await expect(playerHeroLane).toBeEmpty();
  await expect
    .poll(() => visibleHeroAvatarCount("player-hero"))
    .toBe(1);
  await expect(stickyLabel).toHaveAttribute(
    "data-bpp-sticky-hero-entity-id",
    "player-hero",
  );
  await expect(stickyLabel).toHaveClass(/bpp-side-player/u);
  await expect(stickyLabel).toContainText("Fixture Player");
  await expect(
    page.getByTestId("timeline-sticky-hero-side"),
  ).toHaveText("Player");
  await expect(
    page.getByTestId("timeline-sticky-hero-side"),
  ).toHaveClass(/text-nano/u);
  const stickyBadgeTypography = await page
    .getByTestId("timeline-sticky-hero-side")
    .evaluate((element) => {
      const style = getComputedStyle(element);
      const root = getComputedStyle(document.documentElement);
      const nanoRem = Number.parseFloat(root.getPropertyValue("--text-nano"));
      const rootFontSize = Number.parseFloat(root.fontSize);
      return {
        actualFontSize: Number.parseFloat(style.fontSize),
        expectedFontSize: nanoRem * rootFontSize,
      };
    });
  expect(
    Math.abs(
      stickyBadgeTypography.actualFontSize
        - stickyBadgeTypography.expectedFontSize,
    ),
  ).toBeLessThan(0.1);
  await expect(stickyLabel).not.toContainText("hero");
  await expect(stickyCanvas).toHaveAttribute(
    "data-bpp-sticky-side",
    "player",
  );
  const playerStickySideColor = await stickyLabel.evaluate(
    (element) =>
      getComputedStyle(element).getPropertyValue("--bpp-sticky-side"),
  );
  expect(playerStickySideColor).not.toBe("");

  const readStickyGeometry = () => page.evaluate(() => {
    const ruler = document.querySelector(
      '[data-bpp-test-id="timeline-ruler-canvas"]',
    );
    const label = document.querySelector(
      '[data-bpp-test-id="timeline-sticky-hero-label"]',
    );
    const canvas = document.querySelector(
      '[data-bpp-test-id="timeline-sticky-hero-events"]',
    );
    if (
      !(ruler instanceof HTMLCanvasElement)
      || !(label instanceof HTMLElement)
      || !(canvas instanceof HTMLCanvasElement)
    ) {
      return null;
    }
    const rulerBounds = ruler.getBoundingClientRect();
    const labelBounds = label.getBoundingClientRect();
    const canvasBounds = canvas.getBoundingClientRect();
    return {
      canvasHeight: canvasBounds.height,
      canvasRatio:
        canvas.width / Number.parseFloat(canvas.style.width),
      canvasTop: canvasBounds.top,
      labelHeight: labelBounds.height,
      labelTop: labelBounds.top,
      rulerBottom: rulerBounds.bottom,
    };
  });
  let geometry = await readStickyGeometry();
  expect(geometry).not.toBeNull();
  expect(Math.abs(geometry.labelTop - geometry.rulerBottom)).toBeLessThan(1);
  expect(Math.abs(geometry.canvasTop - geometry.rulerBottom)).toBeLessThan(1);
  expect(geometry.labelHeight).toBe(52);
  expect(geometry.canvasHeight).toBe(52);
  expect(geometry.canvasRatio).toBeCloseTo(2, 1);

  await timelineScroll.evaluate((scroll, lane) => {
    scroll.scrollTop = (lane + 1) * 52 + 1;
  }, opponentLane);
  await expect(stickyLabel).toHaveAttribute(
    "data-bpp-sticky-hero-entity-id",
    "opponent-hero",
  );
  await expect(stickyLabel).toHaveAttribute(
    "data-bpp-side-boundary",
    "opponent",
  );
  await expect(stickyLabel).toHaveClass(/bpp-side-opponent/u);
  await expect(stickyLabel).toContainText("Fixture Opponent");
  await expect(
    page.getByTestId("timeline-sticky-hero-side"),
  ).toHaveText("Opponent");
  await expect(stickyLabel).not.toContainText("hero");
  await expect(stickyCanvas).toHaveAttribute(
    "data-bpp-sticky-side",
    "opponent",
  );
  await expect(opponentHeroLane).toBeEmpty();
  await expect(playerHeroLane).not.toBeEmpty();
  await expect
    .poll(() => visibleHeroAvatarCount("opponent-hero"))
    .toBe(1);
  const opponentStickySideColor = await stickyLabel.evaluate(
    (element) =>
      getComputedStyle(element).getPropertyValue("--bpp-sticky-side"),
  );
  expect(opponentStickySideColor).not.toBe(playerStickySideColor);
  geometry = await readStickyGeometry();
  expect(Math.abs(geometry.labelTop - geometry.rulerBottom)).toBeLessThan(1);
  expect(Math.abs(geometry.canvasTop - geometry.rulerBottom)).toBeLessThan(1);

  const stickyBounds = await stickyCanvas.boundingBox();
  expect(stickyBounds).not.toBeNull();
  await page.mouse.click(
    stickyBounds.x + stickyBounds.width * 0.25,
    stickyBounds.y + 17,
  );
  await expect(page.getByTestId("frame-inspector-entity")).toHaveText(
    "Fixture Opponent",
  );
  await expect(stickyLabel).toHaveClass(/is-related-target/u);

  await timelineScroll.evaluate((scroll) => {
    scroll.scrollTop = 0;
  });
  await expect(stickyLabel).toBeHidden();
  await expect(stickyCanvas).toBeHidden();
  await expect(playerHeroLane).not.toBeEmpty();
  await expect(opponentHeroLane).not.toBeEmpty();
  await expect(page.getByTestId("timeline-lane-icon-0")).toBeVisible();
  await expect
    .poll(() => visibleHeroAvatarCount("player-hero"))
    .toBe(1);
});

test("scrolls a combat log target lane into the visible timeline viewport", async ({
  page,
}) => {
  await page.setViewportSize({ width: 999, height: 857 });
  await page.goto(`${scrollRecordingReportUrl}?lang=en`);
  await page.getByTestId("combat-log-dock-toggle").click();

  const timelineScroll = page.getByTestId("timeline-scroll");
  await timelineScroll.evaluate((scroll) => {
    scroll.scrollTop = 0;
  });
  const damageRow = page
    .getByTestId("combat-log-entry")
    .filter({ hasText: "Damage" });
  await expect(damageRow).toHaveCount(1);
  await damageRow.click();

  const targetLane = page.locator(
    '[data-bpp-entity-id="opponent-hero"]',
  );
  await expect(targetLane).toHaveClass(/is-jump-target/u);
  await expect
    .poll(() =>
      page.evaluate(() => {
        const scroll = document.querySelector(
          '[data-bpp-test-id="timeline-scroll"]',
        );
        const ruler = document.querySelector(
          '[data-bpp-test-id="timeline-ruler-canvas"]',
        );
        const sticky = document.querySelector(
          '[data-bpp-test-id="timeline-sticky-hero-label"]',
        );
        const lane = document.querySelector(
          '[data-bpp-entity-id="opponent-hero"]',
        );
        if (
          !(scroll instanceof HTMLElement)
          || !(ruler instanceof HTMLElement)
          || !(lane instanceof HTMLElement)
        ) {
          return false;
        }
        const scrollBounds = scroll.getBoundingClientRect();
        const rulerBounds = ruler.getBoundingClientRect();
        const stickyBounds =
          sticky instanceof HTMLElement && !sticky.hidden
            ? sticky.getBoundingClientRect()
            : null;
        const laneBounds = lane.getBoundingClientRect();
        const visibleTop = Math.max(
          rulerBounds.bottom,
          stickyBounds?.bottom ?? Number.NEGATIVE_INFINITY,
        );
        return laneBounds.top >= visibleTop - 1
          && laneBounds.bottom <= scrollBounds.bottom + 1;
      })
    )
    .toBe(true);
});

test("renders both combatants on the shared state band at device scale", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);

  const labels = page.getByTestId("state-band-labels");
  const stateTime = labels.locator("[data-bpp-state-time]");
  const health = page.getByTestId("state-label-health");
  await expect(health).toContainText("1,000");
  await expect(health).toContainText("1,200");
  await expect(page.getByTestId("state-label-healthRegen")).toContainText("10");
  await expect(page.getByTestId("state-label-healthRegen")).toContainText("5");

  for (const testId of [
    "state-band-canvas",
    "timeline-ruler-canvas",
    "timeline-canvas",
    "timeline-sticky-hero-events",
  ]) {
    await expect
      .poll(() =>
        page.getByTestId(testId).evaluate((canvas) => {
          const logicalWidth = Number.parseFloat(canvas.style.width);
          return canvas.width / logicalWidth;
        }),
      )
      .toBeCloseTo(2, 1);
  }

  const stateCanvas = page.getByTestId("state-band-canvas");
  const stateBounds = await stateCanvas.boundingBox();
  expect(stateBounds).not.toBeNull();
  const logicalWidth = await stateCanvas.evaluate(
    (canvas) =>
      Number.parseFloat(canvas.style.width)
      || canvas.getBoundingClientRect().width,
  );
  const logicalX = 14 + (3_000 / 8_000) * (logicalWidth - 78);
  await page.mouse.move(
    stateBounds.x + logicalX * (stateBounds.width / logicalWidth),
    stateBounds.y + stateBounds.height / 2,
  );
  await expect(stateTime).toHaveText("3.00s");
  await expect(
    health.locator('[data-bpp-state-side="player"]'),
  ).toHaveText("900");
  await expect(
    health.locator('[data-bpp-state-side="opponent"]'),
  ).toHaveText("1,080");

  await page.mouse.move(1, 1);
  await expect(stateTime).toHaveText("0.00s");
  await expect(
    health.locator('[data-bpp-state-side="player"]'),
  ).toHaveText("1,000");
  await expect(
    health.locator('[data-bpp-state-side="opponent"]'),
  ).toHaveText("1,200");
});

test("highlights and toggles state lines from the metric legend", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);

  const canvas = page.getByTestId("state-band-canvas");
  const timelineCanvas = page.getByTestId("timeline-scene-canvas");
  const health = page.getByTestId("state-label-health");
  const burn = page.getByTestId("state-label-burn");
  const readPlayerAreaPixels = () =>
    timelineCanvas.evaluate((element) => {
      const context = element.getContext("2d");
      const logicalWidth =
        Number.parseFloat(element.style.width)
        || element.getBoundingClientRect().width;
      const logicalHeight =
        Number.parseFloat(element.style.height)
        || element.getBoundingClientRect().height;
      const scaleX = element.width / logicalWidth;
      const scaleY = element.height / logicalHeight;
      const logicalX = 14 + (2_500 / 8_000) * (logicalWidth - 78);
      const pixel = (logicalY) =>
        Array.from(
          context.getImageData(
            Math.round(logicalX * scaleX),
            Math.round(logicalY * scaleY),
            1,
            1,
          ).data,
        );
      return {
        hero: pixel(40),
        item: pixel(52 + 40),
      };
    });
  await expect(health).toHaveAttribute("aria-pressed", "true");
  await expect(canvas).toHaveAttribute(
    "data-bpp-visible-metrics",
    "health,rage,healthRegen,shield,burn,poison",
  );
  await expect(timelineCanvas).toHaveAttribute(
    "data-bpp-hero-health-visible",
    "true",
  );
  await expect(timelineCanvas).toHaveAttribute(
    "data-bpp-hero-health-lanes",
    "player:0,opponent:3",
  );
  const visibleAreaPixels = await readPlayerAreaPixels();
  expect(visibleAreaPixels.hero).not.toEqual(visibleAreaPixels.item);

  await burn.hover();
  await expect(canvas).toHaveAttribute(
    "data-bpp-highlighted-metric",
    "burn",
  );
  await page.mouse.move(1, 1);
  await expect(canvas).toHaveAttribute(
    "data-bpp-highlighted-metric",
    "",
  );

  await health.focus();
  await expect(canvas).toHaveAttribute(
    "data-bpp-highlighted-metric",
    "health",
  );
  await health.locator("strong").click();
  await expect(health).toHaveAttribute("aria-pressed", "false");
  await expect(canvas).toHaveAttribute(
    "data-bpp-visible-metrics",
    "rage,healthRegen,shield,burn,poison",
  );
  await expect(canvas).toHaveAttribute(
    "data-bpp-highlighted-metric",
    "",
  );
  await expect(timelineCanvas).toHaveAttribute(
    "data-bpp-hero-health-visible",
    "false",
  );
  const hiddenAreaPixels = await readPlayerAreaPixels();
  expect(hiddenAreaPixels.hero).toEqual(hiddenAreaPixels.item);

  await health.press(" ");
  await expect(health).toHaveAttribute("aria-pressed", "true");
  await expect(canvas).toHaveAttribute(
    "data-bpp-visible-metrics",
    "health,rage,healthRegen,shield,burn,poison",
  );
  await expect(canvas).toHaveAttribute(
    "data-bpp-highlighted-metric",
    "health",
  );
  await expect(timelineCanvas).toHaveAttribute(
    "data-bpp-hero-health-visible",
    "true",
  );
  const restoredAreaPixels = await readPlayerAreaPixels();
  expect(restoredAreaPixels.hero).not.toEqual(restoredAreaPixels.item);
});

test("pins a solid axis on click while pointer hover drives a dashed axis", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  await expect(page.getByTestId("timeline-ruler-canvas")).toBeVisible();
  await expect(page.getByTestId("timeline-canvas")).toBeVisible();

  const readGuidePixels = (pinnedRatio, previewRatio) =>
    page.getByTestId("timeline-ruler-canvas").evaluate((canvas, ratios) => {
      const context = canvas.getContext("2d");
      const logicalWidth =
        Number.parseFloat(canvas.style.width)
        || canvas.getBoundingClientRect().width;
      const scale = canvas.width / logicalWidth;
      const rowAt = (logicalY) =>
        context.getImageData(
          0,
          Math.round(logicalY * scale),
          canvas.width,
          1,
        ).data;
      const topRow = rowAt(2);
      const dashGapRow = rowAt(6);
      const pixelAt = (row, logicalX) => {
        const index = Math.round(logicalX * scale) * 4;
        return Array.from(row.slice(index, index + 4));
      };
      const topBackground = pixelAt(topRow, 50);
      const dashGapBackground = pixelAt(dashGapRow, 50);
      const differs = (left, right) =>
        left.some((value, channel) => value !== right[channel]);
      const xsAround = (ratio) => Array.from(
        { length: 7 },
        (_, index) => logicalWidth * ratio - 3 + index,
      );
      return {
        pinnedAxisVisible: xsAround(ratios.pinnedRatio).some((logicalX) =>
          differs(pixelAt(topRow, logicalX), topBackground)),
        previewDashVisible: xsAround(ratios.previewRatio).some((logicalX) =>
          differs(pixelAt(topRow, logicalX), topBackground)),
        previewGapClear: xsAround(ratios.previewRatio).every((logicalX) =>
          !differs(
            pixelAt(dashGapRow, logicalX),
            dashGapBackground,
          )),
      };
    }, { pinnedRatio, previewRatio });

  const rulerBounds = await page
    .getByTestId("timeline-ruler-canvas")
    .boundingBox();
  expect(rulerBounds).not.toBeNull();
  const guideRatios = await visibleGuideRatios(page, rulerBounds);
  await page.mouse.move(
    rulerBounds.x + rulerBounds.width * guideRatios.preview,
    rulerBounds.y + 10,
  );
  await expect.poll(async () => {
    const pixels = await readGuidePixels(
      14 / rulerBounds.width,
      guideRatios.preview,
    );
    return pixels.pinnedAxisVisible
      && pixels.previewDashVisible
      && pixels.previewGapClear;
  }).toBe(true);

  await page.mouse.click(
    rulerBounds.x + rulerBounds.width * guideRatios.pinned,
    rulerBounds.y + 10,
  );
  await expect.poll(async () => {
    const pixels = await readGuidePixels(
      guideRatios.pinned,
      guideRatios.pinned,
    );
    return pixels.pinnedAxisVisible && !pixels.previewGapClear;
  }).toBe(true);

  await page.mouse.move(
    rulerBounds.x + rulerBounds.width * guideRatios.preview,
    rulerBounds.y + 10,
  );
  await expect.poll(async () => {
    const pixels = await readGuidePixels(
      guideRatios.pinned,
      guideRatios.preview,
    );
    return pixels.pinnedAxisVisible
      && pixels.previewDashVisible
      && pixels.previewGapClear;
  }).toBe(true);

  await page.mouse.move(5, 5);
  await expect.poll(async () => {
    const pixels = await readGuidePixels(
      guideRatios.pinned,
      guideRatios.preview,
    );
    return pixels.pinnedAxisVisible && !pixels.previewDashVisible;
  }).toBe(true);
});

test("updates the hover preview axis for adjacent pointer positions", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  const canvas = page.getByTestId("timeline-canvas");
  const tooltip = page.getByTestId("timeline-tooltip");
  const bounds = await canvas.boundingBox();
  expect(bounds).not.toBeNull();

  await page.mouse.move(bounds.x + bounds.width * 0.2, bounds.y + 20);
  await expect
    .poll(() => tooltip.getAttribute("data-bpp-combat-ms"))
    .not.toBe("");
  const first = Number(
    await tooltip.getAttribute("data-bpp-combat-ms"),
  );

  await page.mouse.move(bounds.x + bounds.width * 0.2 + 1, bounds.y + 20);
  await expect
    .poll(async () =>
      Number(await tooltip.getAttribute("data-bpp-combat-ms"))
    )
    .toBeGreaterThan(first);
  const second = Number(
    await tooltip.getAttribute("data-bpp-combat-ms"),
  );
  expect(second - first).toBeLessThan(100);
});

test("paused recording preview cannot move the pinned solid axis", async ({
  page,
}) => {
  await page.addInitScript(() => {
    const states = new WeakMap();
    const stateFor = (media) => {
      let state = states.get(media);
      if (!state) {
        state = { currentTime: 0, paused: true };
        states.set(media, state);
      }
      return state;
    };
    Object.defineProperty(HTMLMediaElement.prototype, "currentTime", {
      configurable: true,
      get() {
        return stateFor(this).currentTime;
      },
      set(value) {
        stateFor(this).currentTime = Number(value);
        queueMicrotask(() => this.dispatchEvent(new Event("timeupdate")));
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "paused", {
      configurable: true,
      get() {
        return stateFor(this).paused;
      },
    });
    HTMLMediaElement.prototype.play = function play() {
      stateFor(this).paused = false;
      this.dispatchEvent(new Event("play"));
      return Promise.resolve();
    };
    HTMLMediaElement.prototype.pause = function pause() {
      stateFor(this).paused = true;
      this.dispatchEvent(new Event("pause"));
    };
  });
  await page.goto(`${recordingReportUrl}?lang=en`);

  const recordingControls = page.getByTestId("recording-controls");
  await expect(recordingControls).toBeVisible();
  expect(
    await recordingControls.evaluate(
      (element) =>
        element.closest('[data-bpp-test-id="workbench-footer"]') !== null,
    ),
  ).toBe(true);
  await expect(
    page
      .getByTestId("recording-window")
      .getByTestId("recording-controls"),
  ).toHaveCount(0);
  await expect(page.getByTestId("recording-speed")).toHaveText("1×");
  await page.getByTestId("recording-speed").click();
  await expect(page.getByTestId("recording-speed-popover")).toBeVisible();
  await page.getByTestId("recording-speed-option-1.5").click();
  await expect(page.getByTestId("recording-speed")).toHaveText("1.5×");
  await expect
    .poll(async () => {
      const popover = page.getByTestId("recording-speed-popover");
      return (await popover.count()) === 0
        ? "closed"
        : await popover.getAttribute("data-state");
    })
    .toBe("closed");
  await expect(page.getByText("Exact sync", { exact: true })).toHaveCount(0);

  const video = page.getByTestId("recording-video");
  await video.evaluate((element) => {
    Object.defineProperties(element, {
      videoWidth: { configurable: true, value: 1024 },
      videoHeight: { configurable: true, value: 768 },
    });
    element.dispatchEvent(new Event("loadedmetadata"));
  });
  await expect(page.getByTestId("recording-window")).toHaveAttribute(
    "data-video-aspect",
    "1024:768",
  );
  const recordingAspect = await page
    .locator(".bpp-recording-media")
    .evaluate((element) => {
      const bounds = element.getBoundingClientRect();
      return bounds.width / bounds.height;
    });
  expect(recordingAspect).toBeCloseTo(4 / 3, 2);

  await page.getByTestId("recording-play-toggle").click();
  await expect(video).toHaveJSProperty(
    "paused",
    false,
  );
  await page.getByTestId("recording-play-toggle").click();
  await expect(video).toHaveJSProperty(
    "paused",
    true,
  );
  const footerAndRecording = await page.evaluate(() => {
    const footer = document.querySelector(
      '[data-bpp-test-id="workbench-footer"]',
    );
    const recording = document.querySelector(
      '[data-bpp-test-id="recording-window"]',
    );
    const footerBounds = footer?.getBoundingClientRect();
    const recordingBounds = recording?.getBoundingClientRect();
    return {
      gap:
        footerBounds && recordingBounds
          ? footerBounds.top - recordingBounds.bottom
          : Number.NaN,
    };
  });
  expect(footerAndRecording.gap).toBeGreaterThanOrEqual(8);

  const dragHandleBounds = await page
    .getByTestId("recording-drag-handle")
    .boundingBox();
  expect(dragHandleBounds).not.toBeNull();
  await page.mouse.move(
    dragHandleBounds.x + 40,
    dragHandleBounds.y + dragHandleBounds.height / 2,
  );
  await page.mouse.down();
  await page.mouse.move(
    dragHandleBounds.x + 40,
    page.viewportSize().height + 200,
    { steps: 8 },
  );
  await page.mouse.up();
  const draggedFooterGap = await page.evaluate(() => {
    const footer = document.querySelector(
      '[data-bpp-test-id="workbench-footer"]',
    );
    const recording = document.querySelector(
      '[data-bpp-test-id="recording-window"]',
    );
    return (
      footer.getBoundingClientRect().top
      - recording.getBoundingClientRect().bottom
    );
  });
  expect(draggedFooterGap).toBeGreaterThanOrEqual(8);

  await page.getByTestId("report-tab-statistics").click();
  await expect(recordingControls).toBeVisible();
  await expect(page.getByTestId("timeline-zoom-toolbar")).toBeHidden();
  await page.getByTestId("report-tab-timeline").click();

  const ruler = page.getByTestId("timeline-ruler-canvas");
  const bounds = await ruler.boundingBox();
  expect(bounds).not.toBeNull();
  const guideRatios = await visibleGuideRatios(page, bounds);
  await page.mouse.click(
    bounds.x + bounds.width * guideRatios.pinned,
    bounds.y + 10,
  );
  await page.waitForTimeout(50);
  await page.mouse.move(
    bounds.x + bounds.width * guideRatios.preview,
    bounds.y + 10,
  );
  await page.waitForTimeout(50);

  const readGuides = () => ruler.evaluate((canvas, ratios) => {
    const context = canvas.getContext("2d");
    const logicalWidth =
      Number.parseFloat(canvas.style.width)
      || canvas.getBoundingClientRect().width;
    const scale = canvas.width / logicalWidth;
    const row = context.getImageData(
      0,
      Math.round(2 * scale),
      canvas.width,
      1,
    ).data;
    const pixelAt = (logicalX) => {
      const index = Math.round(logicalX * scale) * 4;
      return Array.from(row.slice(index, index + 4));
    };
    const background = pixelAt(50);
    const differs = (pixel) =>
      pixel.some((value, channel) => value !== background[channel]);
    const visibleNear = (ratio) =>
      Array.from(
        { length: 7 },
        (_, index) => logicalWidth * ratio - 3 + index,
      ).some((logicalX) => differs(pixelAt(logicalX)));
    return {
      pinnedVisible: visibleNear(ratios.pinned),
      previewVisible: visibleNear(ratios.preview),
    };
  }, guideRatios);

  await expect.poll(readGuides).toEqual({
    pinnedVisible: true,
    previewVisible: true,
  });
  await expect(page.getByTestId("recording-video")).toHaveJSProperty(
    "paused",
    true,
  );
  await page.getByTestId("recording-play-toggle").click();
  await expect(video).toHaveJSProperty("paused", false);
  const playingMediaTime = await video.evaluate(
    (element) => element.currentTime,
  );
  await page.mouse.move(
    bounds.x + bounds.width * guideRatios.pinned,
    bounds.y + 10,
  );
  await page.waitForTimeout(50);
  await expect(video).toHaveJSProperty("currentTime", playingMediaTime);
  await video.evaluate((element) => {
    element.currentTime = 8.25;
  });
  await expect(video).toHaveJSProperty("paused", true);
  await expect(video).toHaveJSProperty("currentTime", 8);
  await expect(page.getByTestId("recording-timecode")).toHaveText(
    "00:08.000",
  );
  await expect(page.getByTestId("recording-play-toggle")).toHaveAttribute(
    "aria-label",
    "Play",
  );
});

test("coalesces paused recording preview seeks at animation-frame cadence", async ({
  page,
}) => {
  await page.addInitScript(() => {
    window.__bppVideoSeeking = false;
    const states = new WeakMap();
    const stateFor = (media) => {
      let state = states.get(media);
      if (!state) {
        state = { currentTime: 0, paused: true };
        states.set(media, state);
      }
      return state;
    };
    Object.defineProperty(HTMLMediaElement.prototype, "currentTime", {
      configurable: true,
      get() {
        return stateFor(this).currentTime;
      },
      set(value) {
        stateFor(this).currentTime = Number(value);
        queueMicrotask(() => this.dispatchEvent(new Event("timeupdate")));
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "paused", {
      configurable: true,
      get() {
        return stateFor(this).paused;
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "seeking", {
      configurable: true,
      get() {
        return window.__bppVideoSeeking;
      },
    });
  });
  await page.goto(`${recordingReportUrl}?lang=en`);
  await page
    .getByTestId("recording-video")
    .dispatchEvent("loadedmetadata");
  const ruler = page.getByTestId("timeline-ruler-canvas");
  const before = await page.evaluate(() => ({
    dispatches: window.__BPP_VIEWER_TEST__?.previewDispatchCount ?? -1,
    seeks: window.__BPP_VIEWER_TEST__?.recordingPreviewSeekCount ?? -1,
  }));

  await ruler.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    for (let index = 0; index < 36; index += 1) {
      element.dispatchEvent(
        new PointerEvent("pointermove", {
          bubbles: true,
          clientX:
            bounds.left
            + bounds.width * (0.12 + (index % 24) * 0.03),
          clientY: bounds.top + bounds.height / 2,
        }),
      );
    }
  });
  await expect
    .poll(() =>
      page.evaluate(() =>
        window.__BPP_VIEWER_TEST__?.previewDispatchCount ?? -1
      )
    )
    .toBeGreaterThan(before.dispatches);
  const afterMovement = await page.evaluate(() => ({
    dispatches: window.__BPP_VIEWER_TEST__?.previewDispatchCount ?? -1,
    seeks: window.__BPP_VIEWER_TEST__?.recordingPreviewSeekCount ?? -1,
  }));
  const previewDispatches = afterMovement.dispatches - before.dispatches;
  expect(previewDispatches).toBeGreaterThan(0);
  await expect
    .poll(() =>
      page.evaluate(() =>
        window.__BPP_VIEWER_TEST__?.recordingPreviewSeekCount ?? -1
      )
    )
    .toBe(before.seeks + 1);
  const burstSeeks = before.seeks + 1;

  await ruler.evaluate(async (element) => {
    const bounds = element.getBoundingClientRect();
    for (const ratio of [0.18, 0.72, 0.32, 0.82]) {
      element.dispatchEvent(
        new PointerEvent("pointermove", {
          bubbles: true,
          clientX: bounds.left + bounds.width * ratio,
          clientY: bounds.top + bounds.height / 2,
        }),
      );
      await new Promise((resolve) =>
        requestAnimationFrame(() =>
          requestAnimationFrame(() => requestAnimationFrame(resolve))
        )
      );
    }
  });
  await expect
    .poll(() =>
      page.evaluate(() =>
        window.__BPP_VIEWER_TEST__?.previewDispatchCount ?? -1
      )
    )
    .toBeGreaterThan(afterMovement.dispatches);
  await expect
    .poll(() =>
      page.evaluate(() =>
        window.__BPP_VIEWER_TEST__?.recordingPreviewSeekCount ?? -1
      )
    )
    .toBeGreaterThan(burstSeeks);
});

test("applies only the latest hover target while a Firefox-style seek is in flight", async ({
  page,
}) => {
  await page.addInitScript(() => {
    const states = new WeakMap();
    const stateFor = (media) => {
      let state = states.get(media);
      if (!state) {
        state = {
          currentTime: 0,
          paused: true,
          seeking: false,
          seekCalls: [],
        };
        states.set(media, state);
      }
      return state;
    };
    const beginSeek = (media, value, kind) => {
      const state = stateFor(media);
      state.currentTime = Number(value);
      state.seeking = true;
      state.seekCalls.push({ kind, seconds: Number(value) });
    };
    Object.defineProperty(HTMLMediaElement.prototype, "currentTime", {
      configurable: true,
      get() {
        return stateFor(this).currentTime;
      },
      set(value) {
        beginSeek(this, value, "exact");
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "paused", {
      configurable: true,
      get() {
        return stateFor(this).paused;
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "seeking", {
      configurable: true,
      get() {
        return stateFor(this).seeking;
      },
    });
    HTMLMediaElement.prototype.fastSeek = function fastSeek(value) {
      beginSeek(this, value, "fast");
    };
    window.__bppSeekProbe = {
      calls(media) {
        return [...stateFor(media).seekCalls];
      },
      complete(media) {
        const state = stateFor(media);
        state.seeking = false;
        media.dispatchEvent(new Event("seeked"));
        media.dispatchEvent(new Event("timeupdate"));
      },
      reset(media) {
        const state = stateFor(media);
        state.seeking = false;
        state.seekCalls.length = 0;
      },
    };
  });
  await page.goto(`${recordingReportUrl}?lang=en`);
  const video = page.getByTestId("recording-video");
  await video.dispatchEvent("loadedmetadata");
  await video.evaluate((element) => {
    window.__bppSeekProbe.complete(element);
    window.__bppSeekProbe.reset(element);
  });

  const ruler = page.getByTestId("timeline-ruler-canvas");
  const moveToRatio = async (ratio) => {
    await ruler.evaluate(async (element, nextRatio) => {
      const bounds = element.getBoundingClientRect();
      element.dispatchEvent(
        new PointerEvent("pointermove", {
          bubbles: true,
          clientX: bounds.left + bounds.width * nextRatio,
          clientY: bounds.top + bounds.height / 2,
        }),
      );
      await new Promise((resolve) =>
        requestAnimationFrame(() => requestAnimationFrame(resolve))
      );
    }, ratio);
  };
  const seekCalls = () =>
    video.evaluate((element) => window.__bppSeekProbe.calls(element));

  await moveToRatio(0.18);
  await expect.poll(seekCalls).toHaveLength(1);
  await moveToRatio(0.42);
  await moveToRatio(0.76);
  await expect.poll(seekCalls).toHaveLength(1);

  const firstCall = (await seekCalls())[0];
  expect(firstCall.kind).toBe("fast");
  await video.evaluate((element) => window.__bppSeekProbe.complete(element));
  await expect.poll(seekCalls).toHaveLength(2);
  const secondCall = (await seekCalls())[1];
  expect(secondCall.kind).toBe("fast");
  expect(secondCall.seconds).toBeGreaterThan(firstCall.seconds);

  await video.evaluate((element) => window.__bppSeekProbe.complete(element));
  expect(await seekCalls()).toHaveLength(2);
});

test("uses the scrub proxy for live hover and syncs the full video once on leave", async ({
  page,
}) => {
  await page.addInitScript(() => {
    const states = new WeakMap();
    const stateFor = (media) => {
      let state = states.get(media);
      if (!state) {
        state = {
          currentTime: 0,
          paused: true,
          seeking: false,
          seekCalls: [],
        };
        states.set(media, state);
      }
      return state;
    };
    const beginSeek = (media, value, kind) => {
      const state = stateFor(media);
      state.currentTime = Number(value);
      state.seeking = true;
      state.seekCalls.push({ kind, seconds: Number(value) });
    };
    const nativeSetAttribute = Element.prototype.setAttribute;
    Element.prototype.setAttribute = function setAttribute(name, value) {
      if (this instanceof HTMLMediaElement && name === "src") return;
      nativeSetAttribute.call(this, name, value);
    };
    Object.defineProperty(HTMLMediaElement.prototype, "src", {
      configurable: true,
      get() {
        return "";
      },
      set() {},
    });
    Object.defineProperty(HTMLMediaElement.prototype, "currentTime", {
      configurable: true,
      get() {
        return stateFor(this).currentTime;
      },
      set(value) {
        beginSeek(this, value, "exact");
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "paused", {
      configurable: true,
      get() {
        return stateFor(this).paused;
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "seeking", {
      configurable: true,
      get() {
        return stateFor(this).seeking;
      },
    });
    HTMLMediaElement.prototype.fastSeek = function fastSeek(value) {
      beginSeek(this, value, "fast");
    };
    HTMLMediaElement.prototype.play = function play() {
      stateFor(this).paused = false;
      this.dispatchEvent(new Event("play"));
      return Promise.resolve();
    };
    HTMLMediaElement.prototype.pause = function pause() {
      stateFor(this).paused = true;
      this.dispatchEvent(new Event("pause"));
    };
    window.addEventListener(
      "error",
      (event) => {
        if (!(event.target instanceof HTMLMediaElement)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
      },
      true,
    );
    window.__bppSeekProbe = {
      calls(media) {
        return [...stateFor(media).seekCalls];
      },
      complete(media) {
        const state = stateFor(media);
        state.seeking = false;
        media.dispatchEvent(new Event("seeked"));
        media.dispatchEvent(new Event("timeupdate"));
      },
      reset(media) {
        const state = stateFor(media);
        state.seeking = false;
        state.seekCalls.length = 0;
      },
    };
  });
  await page.goto(`${scrubRecordingReportUrl}?lang=en`);
  const fullVideo = page.getByTestId("recording-video");
  const scrubVideo = page.getByTestId("recording-scrub-video");
  await fullVideo.dispatchEvent("loadedmetadata");
  await scrubVideo.dispatchEvent("loadedmetadata");
  await fullVideo.evaluate((element) => window.__bppSeekProbe.reset(element));
  await scrubVideo.evaluate((element) => window.__bppSeekProbe.reset(element));

  const ruler = page.getByTestId("timeline-ruler-canvas");
  const bounds = await ruler.boundingBox();
  expect(bounds).not.toBeNull();
  await page.mouse.move(
    bounds.x + bounds.width * 0.3,
    bounds.y + bounds.height / 2,
  );
  await expect
    .poll(() =>
      scrubVideo.evaluate((element) =>
        window.__bppSeekProbe.calls(element)
      )
    )
    .toHaveLength(1);
  expect(
    await scrubVideo.evaluate((element) =>
      window.__bppSeekProbe.calls(element)[0]?.kind
    ),
  ).toBe("fast");
  expect(
    await fullVideo.evaluate((element) =>
      window.__bppSeekProbe.calls(element)
    ),
  ).toHaveLength(0);
  await expect(scrubVideo).toHaveAttribute("data-preview-active", "true");

  await scrubVideo.evaluate((element) =>
    window.__bppSeekProbe.complete(element)
  );
  await page.mouse.move(4, 4);
  await expect
    .poll(() =>
      fullVideo.evaluate((element) =>
        window.__bppSeekProbe.calls(element)
      )
    )
    .toHaveLength(1);
  await expect(scrubVideo).toHaveAttribute("data-preview-active", "true");
  await fullVideo.evaluate((element) =>
    window.__bppSeekProbe.complete(element)
  );
  await expect(scrubVideo).toHaveAttribute("data-preview-active", "false");
});

test("preserves the timeline viewport and recording navigation across tabs", async ({
  page,
}) => {
  await page.addInitScript(() => {
    const states = new WeakMap();
    const stateFor = (media) => {
      let state = states.get(media);
      if (!state) {
        state = { currentTime: 0, paused: true };
        states.set(media, state);
      }
      return state;
    };
    Object.defineProperty(HTMLMediaElement.prototype, "currentTime", {
      configurable: true,
      get() {
        return stateFor(this).currentTime;
      },
      set(value) {
        stateFor(this).currentTime = Number(value);
        queueMicrotask(() => this.dispatchEvent(new Event("timeupdate")));
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "paused", {
      configurable: true,
      get() {
        return stateFor(this).paused;
      },
    });
    HTMLMediaElement.prototype.play = function play() {
      stateFor(this).paused = false;
      this.dispatchEvent(new Event("play"));
      return Promise.resolve();
    };
    HTMLMediaElement.prototype.pause = function pause() {
      stateFor(this).paused = true;
      this.dispatchEvent(new Event("pause"));
    };
  });
  await page.goto(`${scrollRecordingReportUrl}?lang=en`);

  const video = page.getByTestId("recording-video");
  await video.evaluate((element) => {
    element.dispatchEvent(new Event("loadedmetadata"));
  });
  const timelineScroll = page.getByTestId("timeline-scroll");
  const requestedViewport = await timelineScroll.evaluate((element) => {
    element.scrollLeft = Math.min(420, element.scrollWidth - element.clientWidth);
    element.scrollTop = Math.min(260, element.scrollHeight - element.clientHeight);
    return {
      left: element.scrollLeft,
      top: element.scrollTop,
    };
  });
  expect(requestedViewport.left).toBeGreaterThan(0);
  expect(requestedViewport.top).toBeGreaterThan(0);

  await page.getByTestId("report-tab-statistics").click();
  await expect(page.getByTestId("statistics-section")).toBeVisible();
  await expect(page.getByTestId("recording-previous-event")).toBeEnabled();
  await expect(page.getByTestId("recording-next-event")).toBeEnabled();
  await page.getByTestId("recording-next-event").click();
  await expect(page.getByTestId("recording-timecode")).toHaveText("00:02.000");
  await expect(
    page.getByTestId("state-band-labels").locator("span").first(),
  ).toContainText("2.00s");

  await page.getByTestId("report-tab-timeline").click();
  await expect(page.getByTestId("timeline-section")).toBeVisible();
  await expect
    .poll(() =>
      timelineScroll.evaluate((element) => ({
        left: element.scrollLeft,
        top: element.scrollTop,
      })),
    )
    .toEqual(requestedViewport);
});

test("remounts recording at the hidden selection with truthful playback UI", async ({
  page,
}) => {
  await page.addInitScript(() => {
    const states = new WeakMap();
    const stateFor = (media) => {
      let state = states.get(media);
      if (!state) {
        state = { currentTime: 0, paused: true };
        states.set(media, state);
      }
      return state;
    };
    Object.defineProperty(HTMLMediaElement.prototype, "currentTime", {
      configurable: true,
      get() {
        return stateFor(this).currentTime;
      },
      set(value) {
        stateFor(this).currentTime = Number(value);
        queueMicrotask(() => this.dispatchEvent(new Event("timeupdate")));
      },
    });
    Object.defineProperty(HTMLMediaElement.prototype, "paused", {
      configurable: true,
      get() {
        return stateFor(this).paused;
      },
    });
    HTMLMediaElement.prototype.play = function play() {
      stateFor(this).paused = false;
      this.dispatchEvent(new Event("play"));
      return Promise.resolve();
    };
    HTMLMediaElement.prototype.pause = function pause() {
      stateFor(this).paused = true;
      this.dispatchEvent(new Event("pause"));
    };
  });
  await page.goto(`${recordingReportUrl}?lang=en`);

  const firstVideo = page.getByTestId("recording-video");
  await firstVideo.evaluate((element) => {
    element.dataset.recordingInstance = "before-hide";
    element.dispatchEvent(new Event("loadedmetadata"));
  });
  await page.getByTestId("recording-next-event").click();
  await expect(firstVideo).toHaveJSProperty("currentTime", 2);
  await expect(page.getByTestId("recording-timecode")).toHaveText("00:02.000");
  await expect(page.getByTestId("frame-inspector-popover")).toHaveCount(0);
  await page.getByTestId("recording-play-toggle").click();
  await expect(firstVideo).toHaveJSProperty("paused", false);

  const recordingToggle = page.getByTestId("recording-visibility-toggle");
  await expect(recordingToggle).toHaveCount(1);
  await expect(
    page
      .getByTestId("header-utility-toolbar")
      .getByTestId("recording-visibility-toggle"),
  ).toHaveCount(0);
  await expect(
    page
      .getByTestId("workbench-footer")
      .getByTestId("recording-visibility-toggle"),
  ).toHaveCount(1);
  await expect(recordingToggle).toBeVisible();
  await expect(recordingToggle).toHaveAttribute("aria-pressed", "true");
  await recordingToggle.click();
  await expect(recordingToggle).toHaveAttribute("aria-pressed", "false");
  await expect(page.getByTestId("recording-window")).toHaveCount(0);
  await expect(page.getByTestId("recording-video")).toHaveCount(0);
  await expect(page.getByTestId("workbench-footer")).toBeVisible();
  await expect(recordingToggle).toBeVisible();
  await page.getByTestId("report-tab-statistics").click();
  await expect(page.getByTestId("workbench-footer")).toBeVisible();
  await expect(recordingToggle).toBeVisible();
  await page.getByTestId("report-tab-timeline").click();
  await page.evaluate(() => {
    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }
  });
  await page.keyboard.press("ArrowRight");
  await expect(
    page.getByTestId("state-band-labels").locator("span").first(),
  ).toContainText("3.00s");

  await recordingToggle.click();
  await expect(recordingToggle).toHaveAttribute("aria-pressed", "true");
  const remountedVideo = page.getByTestId("recording-video");
  await expect(remountedVideo).toBeVisible();
  await expect(remountedVideo).not.toHaveAttribute(
    "data-recording-instance",
    "before-hide",
  );
  await remountedVideo.evaluate((element) => {
    element.dispatchEvent(new Event("loadedmetadata"));
  });
  await expect(remountedVideo).toHaveJSProperty("currentTime", 3);
  await expect(remountedVideo).toHaveJSProperty("paused", true);
  await expect(page.getByTestId("recording-timecode")).toHaveText("00:03.000");
  await expect(page.getByTestId("recording-play-toggle")).toHaveAttribute(
    "aria-label",
    "Play",
  );
});

test("event navigation skips frames without a rendered cluster", async ({
  page,
}) => {
  await page.goto(`${navigationReportUrl}?lang=en`);

  const currentTime = page
    .getByTestId("state-band-labels")
    .locator("span")
    .first();
  await page.keyboard.press("ArrowRight");
  await expect(currentTime).toContainText("2.00s");

  await page.keyboard.press("ArrowRight");
  await expect(currentTime).toContainText("3.00s");

  await page.keyboard.press("ArrowLeft");
  await expect(currentTime).toContainText("2.00s");
  await expect(page.getByTestId("frame-inspector-popover")).toHaveCount(0);
});

test("supports time zoom, statistics navigation, entity coverage, and sorting", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);

  const canvas = page.getByTestId("timeline-canvas");
  const initialWidth = await canvas.evaluate((element) =>
    Number.parseFloat(element.style.width),
  );
  const eventViewportWidth = await page
    .getByTestId("timeline-scroll")
    .evaluate((element) => {
      const labels = element.querySelector(
        '[data-bpp-test-id="timeline-lane-labels"]',
      );
      return element.clientWidth - (labels?.getBoundingClientRect().width ?? 0);
    });
  expect(initialWidth).toBeGreaterThanOrEqual(eventViewportWidth * 2);
  await page.getByTestId("time-zoom-in").click();
  await expect(page.getByTestId("time-zoom-reset")).toHaveText("150%");
  const zoomedWidth = await canvas.evaluate((element) =>
    Number.parseFloat(element.style.width),
  );
  expect(zoomedWidth).toBeGreaterThan(initialWidth);
  await page.getByTestId("time-zoom-reset").click();
  await expect(page.getByTestId("time-zoom-reset")).toHaveText("100%");

  const timelineTab = page.getByTestId("report-tab-timeline");
  const statisticsTab = page.getByTestId("report-tab-statistics");
  const tabGeometryBefore = await Promise.all(
    [timelineTab, statisticsTab].map((tab) => tab.boundingBox()),
  );
  await statisticsTab.click();
  const tabGeometryAfter = await Promise.all(
    [timelineTab, statisticsTab].map((tab) => tab.boundingBox()),
  );
  for (let index = 0; index < tabGeometryBefore.length; index += 1) {
    expect(tabGeometryBefore[index]).not.toBeNull();
    expect(tabGeometryAfter[index]).not.toBeNull();
    expect(
      Math.abs(tabGeometryBefore[index].x - tabGeometryAfter[index].x),
    ).toBeLessThan(0.5);
    expect(
      Math.abs(
        tabGeometryBefore[index].width - tabGeometryAfter[index].width,
      ),
    ).toBeLessThan(0.5);
  }
  const tabStyles = await Promise.all(
    [timelineTab, statisticsTab].map((tab) =>
      tab.evaluate((element) => {
        const style = getComputedStyle(element);
        return {
          borderBottomColor: style.borderBottomColor,
          borderBottomWidth: style.borderBottomWidth,
          borderRadius: style.borderRadius,
        };
      }),
    ),
  );
  expect(tabStyles[0].borderBottomColor).not.toBe(
    tabStyles[1].borderBottomColor,
  );
  expect(tabStyles[1].borderBottomWidth).toBe("2px");
  expect(tabStyles[1].borderRadius).toBe("0px");

  await expect(page.getByTestId("statistics-section")).toBeVisible();
  await expect(
    page.getByText("Top damage sources", { exact: true }),
  ).toHaveCount(0);
  const itemRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Training Blade" });
  const skillRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Quick Thinking" });
  await expect(itemRow).toHaveCount(1);
  await expect(skillRow).toHaveCount(1);
  await expect(itemRow).toHaveAttribute("data-entity-type", "item");
  await expect(skillRow).toHaveAttribute("data-entity-type", "skill");
  await expect(skillRow).toContainText("7.85s");
  await expect(skillRow).not.toContainText("7,850ms");
  const activityArtGeometry = await Promise.all(
    [itemRow, skillRow].map((row) =>
      row.locator('[data-entity-art-size="activity"]').evaluate((art) => {
        const bounds = art.getBoundingClientRect();
        const copy = art.parentElement?.nextElementSibling;
        const copyBounds = copy?.getBoundingClientRect();
        return {
          height: bounds.height,
          copyLeft: copyBounds?.left ?? 0,
        };
      }),
    ),
  );
  expect(activityArtGeometry[0].height).toBe(44);
  expect(activityArtGeometry[1].height).toBe(36);
  expect(
    Math.abs(
      activityArtGeometry[0].copyLeft - activityArtGeometry[1].copyLeft,
    ),
  ).toBeLessThan(0.5);

  const activityScroll = page.getByTestId(
    "statistics-entity-activity-scroll",
  );
  const playerGroupHeader = page.getByTestId(
    "statistics-activity-group-player",
  );
  await activityScroll.evaluate((element) => {
    element.scrollTop = 96;
  });
  await expect
    .poll(() =>
      playerGroupHeader.evaluate((element) => {
        const scroller = element.closest(
          '[data-bpp-test-id="statistics-entity-activity-scroll"]',
        );
        const tableHeader = scroller?.querySelector(
          '[data-slot="table-header"]',
        );
        if (!scroller || !tableHeader) return Number.POSITIVE_INFINITY;
        return Math.abs(
          element.getBoundingClientRect().top
            - tableHeader.getBoundingClientRect().bottom,
        );
      }),
    )
    .toBeLessThan(1);

  const groupBySide = page.getByTestId(
    "statistics-activity-group-by-side",
  );
  await expect(groupBySide).toHaveAttribute("data-state", "checked");
  const groupedRows = page.locator(
    "[data-bpp-test-id^='statistics-activity-row-']",
  );
  const groupedRowCount = await groupedRows.count();
  await groupBySide.click();
  await expect(groupBySide).toHaveAttribute("data-state", "unchecked");
  await expect(page.locator(".bpp-activity-group")).toHaveCount(0);
  const flatRows = page.locator(
    "[data-bpp-test-id^='statistics-activity-row-all-']",
  );
  await expect(flatRows).toHaveCount(groupedRowCount);
  const flatSides = new Set(await flatRows.evaluateAll((rows) =>
    rows.map((row) => row.getAttribute("data-side"))
  ));
  expect(flatSides).toEqual(new Set(["player", "opponent"]));

  await timelineTab.click();
  await statisticsTab.click();
  await expect(groupBySide).toHaveAttribute("data-state", "unchecked");
  await expect(page.locator(".bpp-activity-group")).toHaveCount(0);
  await groupBySide.click();
  await expect(groupBySide).toHaveAttribute("data-state", "checked");
  await expect(
    page.getByTestId("statistics-activity-group-player"),
  ).toBeVisible();
  await expect(
    page.getByTestId("statistics-activity-group-opponent"),
  ).toBeVisible();

  const damageSort = page.getByTestId("statistics-activity-sort-damage");
  await damageSort.click();
  await expect(damageSort.locator("xpath=..")).toHaveAttribute(
    "aria-sort",
    "ascending",
  );
});

test("explains every activity metric on hover and keyboard focus", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();
  const itemRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Training Blade" });

  const emptyBurn = itemRow.getByTestId("statistics-activity-value-burn");
  await emptyBurn.hover();
  const tooltip = page.locator(
    '[data-bpp-test-id="statistics-activity-cell-tooltip"]:not([data-state="closed"])',
  );
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toHaveClass(/animate-in/);
  await expect(tooltip).toContainText("Burn");
  await expect(tooltip).toContainText(
    "No affected targets were recorded",
  );

  const quantifiedDamage = itemRow.getByTestId(
    "statistics-activity-value-damage",
  );
  await expect(
    itemRow.getByTestId("statistics-activity-use-count"),
  ).toHaveText("8");
  await expect(quantifiedDamage).toContainText("525");
  await expect(
    quantifiedDamage.locator('[data-bpp-authoritative="native-card-stats"]'),
  ).toHaveCount(1);
  await quantifiedDamage.focus();
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText("Damage");
  await expect(tooltip).toContainText("Effects by target");
  await expect(
    tooltip.getByTestId("statistics-activity-target-amount"),
  ).toHaveText("120");
  await expect(
    tooltip.getByTestId("statistics-activity-target-count"),
  ).toHaveText("×1");
});

test("activity cells stay in statistics instead of jumping to a timeline lane", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();

  const itemRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Training Blade" });
  await expect(itemRow).not.toHaveAttribute("role", "button");
  await expect(itemRow).not.toHaveAttribute("tabindex", "0");
  await itemRow.getByTestId("statistics-activity-value-damage").click();
  await expect(page.getByTestId("statistics-section")).toBeVisible();
  await expect(page.getByTestId("timeline-section")).toBeHidden();
  await expect(
    page.locator('[data-bpp-entity-id="player-item"]'),
  ).not.toHaveClass(/is-jump-target/u);

  const skillRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Quick Thinking" });
  const hasteCell = skillRow.getByTestId("statistics-activity-value-haste");
  await hasteCell.focus();
  await hasteCell.press(" ");
  await expect(page.getByTestId("statistics-section")).toBeVisible();
  await expect(page.getByTestId("timeline-section")).toBeHidden();
  await expect(
    page.locator('[data-bpp-entity-id="player-skill"]'),
  ).not.toHaveClass(/is-jump-target/u);
});

test("keeps pointer hover imperative without React commits", async ({ page }) => {
  await page.goto(`${reportUrl}?lang=en`);
  const canvas = page.getByTestId("timeline-canvas");
  const bounds = await canvas.boundingBox();
  expect(bounds).not.toBeNull();
  const before = await page.evaluate(() => ({
    commits: window.__BPP_VIEWER_TEST__?.commitCount ?? -1,
    hoverDraws: window.__BPP_VIEWER_TEST__?.hoverDrawCount ?? -1,
    stateStaticDraws:
      window.__BPP_VIEWER_TEST__?.stateStaticDrawCount ?? -1,
    staticDraws: window.__BPP_VIEWER_TEST__?.timelineStaticDrawCount ?? -1,
  }));

  for (let index = 0; index < 24; index += 1) {
    await page.mouse.move(
      bounds.x + 24 + index * Math.max(2, (bounds.width - 48) / 24),
      bounds.y + 20 + (index % 3) * 18,
    );
  }
  await page.waitForTimeout(50);

  const after = await page.evaluate(() => ({
    commits: window.__BPP_VIEWER_TEST__?.commitCount ?? -1,
    hoverDraws: window.__BPP_VIEWER_TEST__?.hoverDrawCount ?? -1,
    stateStaticDraws:
      window.__BPP_VIEWER_TEST__?.stateStaticDrawCount ?? -1,
    staticDraws: window.__BPP_VIEWER_TEST__?.timelineStaticDrawCount ?? -1,
  }));
  expect(after.commits).toBe(before.commits);
  expect(after.hoverDraws).toBeGreaterThan(before.hoverDraws);
  expect(after.stateStaticDraws).toBe(before.stateStaticDraws);
  expect(after.staticDraws).toBe(before.staticDraws);
});

test("coalesces burst pointer input before imperative hover work", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  const canvas = page.getByTestId("timeline-canvas");
  const before = await page.evaluate(() => ({
    commits: window.__BPP_VIEWER_TEST__?.commitCount ?? -1,
    hoverDraws: window.__BPP_VIEWER_TEST__?.hoverDrawCount ?? -1,
    hoverInputs: window.__BPP_VIEWER_TEST__?.hoverInputCount ?? -1,
  }));

  await canvas.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    for (let index = 0; index < 48; index += 1) {
      element.dispatchEvent(
        new PointerEvent("pointermove", {
          bubbles: true,
          clientX: bounds.left + 24 + index * 2,
          clientY: bounds.top + 18 + (index % 3) * 10,
        }),
      );
    }
  });
  await expect
    .poll(() =>
      page.evaluate(() => ({
        hoverDraws: window.__BPP_VIEWER_TEST__?.hoverDrawCount ?? -1,
        hoverInputs: window.__BPP_VIEWER_TEST__?.hoverInputCount ?? -1,
      }))
    )
    .toEqual({
      hoverDraws: before.hoverDraws + 1,
      hoverInputs: before.hoverInputs + 48,
    });
  const after = await page.evaluate(() => ({
    commits: window.__BPP_VIEWER_TEST__?.commitCount ?? -1,
    hoverDraws: window.__BPP_VIEWER_TEST__?.hoverDrawCount ?? -1,
    hoverInputs: window.__BPP_VIEWER_TEST__?.hoverInputCount ?? -1,
  }));
  expect(after.commits).toBe(before.commits);
});

test("keeps timeline preprocessing stable for a time-only selection", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  await page.waitForTimeout(200);
  const before = await page.evaluate(() => ({
    controllers:
      window.__BPP_VIEWER_TEST__?.timelineControllerCount ?? -1,
    preprocess:
      window.__BPP_VIEWER_TEST__?.timelinePreprocessCount ?? -1,
  }));
  expect(before.controllers).toBeGreaterThan(0);
  expect(before.preprocess).toBeGreaterThan(0);

  const ruler = page.getByTestId("timeline-ruler-canvas");
  await ruler.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    element.dispatchEvent(
      new PointerEvent("pointerdown", {
        bubbles: true,
        button: 0,
        clientX: bounds.left + bounds.width * 0.5,
        clientY: bounds.top + bounds.height * 0.5,
      }),
    );
  });
  await expect(
    page.getByTestId("state-band-labels").locator("span").first(),
  ).not.toContainText("0.00s");
  await page.waitForTimeout(100);

  const after = await page.evaluate(() => ({
    controllers:
      window.__BPP_VIEWER_TEST__?.timelineControllerCount ?? -1,
    preprocess:
      window.__BPP_VIEWER_TEST__?.timelinePreprocessCount ?? -1,
  }));
  expect(after).toEqual(before);
});

test("paints themed ECharts and a structured hover card", async ({
  page,
}) => {
  await page.setViewportSize({ width: 999, height: 857 });
  await page.goto(`${chartReportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();
  const host = page.getByTestId("statistics-echarts-output");
  await expect(host.locator("canvas")).toHaveCount(1);
  await expect(
    page.getByTestId("statistics-echarts-effects").locator("canvas"),
  ).toHaveCount(1);
  const damageTypesHost = page.getByTestId(
    "statistics-echarts-damage-types",
  );
  await expect(damageTypesHost.locator("canvas")).toHaveCount(1);
  await expect(page.getByTestId("statistics-chart-legend").first()).toContainText(
    "Player",
  );
  const chartGeometry = await page
    .getByTestId("statistics-chart-grid")
    .evaluate((element) => {
      const cards = Array.from(element.children).map((child) =>
        child.getBoundingClientRect(),
      );
      const bounds = element.getBoundingClientRect();
      return {
        height: bounds.height,
        widths: cards.map((card) => card.width),
        yPositions: cards.map((card) => card.y),
      };
    });
  expect(chartGeometry.height).toBeLessThan(180);
  expect(chartGeometry.widths).toHaveLength(3);
  expect(chartGeometry.widths.every((width) => width > 280)).toBe(true);
  expect(
    chartGeometry.yPositions.every(
      (position) =>
        Math.abs(position - chartGeometry.yPositions[0]) < 0.5,
    ),
  ).toBe(true);
  const paintedPixels = await host.locator("canvas").evaluate((canvas) => {
    const context = canvas.getContext("2d");
    if (!context) return 0;
    const pixels = context.getImageData(
      0,
      0,
      canvas.width,
      canvas.height,
    ).data;
    let painted = 0;
    for (let index = 3; index < pixels.length; index += 16) {
      if (pixels[index] !== 0) painted += 1;
    }
    return painted;
  });
  expect(paintedPixels).toBeGreaterThan(100);

  await host.getByTestId("statistics-chart-hit-0").hover();
  const tooltip = page.getByTestId("statistics-chart-tooltip");
  await expect(tooltip).toBeVisible();
  await expect(tooltip.locator(".bpp-chart-tooltip-title")).toHaveText(
    "Damage",
  );
  await expect(tooltip.locator(".bpp-chart-tooltip-row")).toHaveCount(2);
  await expect(tooltip).toContainText("Player");
  await expect(tooltip).toContainText("Opponent");
  await expect(tooltip).toContainText("320");
  await expect(tooltip).toContainText("180");
  expect(
    await tooltip.locator(".bpp-chart-tooltip-swatch").evaluateAll(
      (elements) =>
        elements.map(
          (element) => getComputedStyle(element).backgroundColor,
        ),
    ),
  ).toEqual(["rgb(232, 194, 104)", "rgb(224, 106, 85)"]);
  const tooltipSurface = await tooltip.evaluate((element) => {
    const surface = element.parentElement;
    if (!surface) return null;
    const styles = getComputedStyle(surface);
    return {
      backgroundColor: styles.backgroundColor,
      borderColor: styles.borderColor,
      borderRadius: styles.borderRadius,
      color: styles.color,
    };
  });
  expect(tooltipSurface).not.toBeNull();
  expect(tooltipSurface.backgroundColor).toBe("rgb(23, 19, 16)");
  expect(tooltipSurface.borderColor).not.toBe("rgb(255, 255, 255)");
  expect(Number.parseFloat(tooltipSurface.borderRadius)).toBeGreaterThan(0);
  expect(tooltipSurface.color).not.toBe("rgb(0, 0, 0)");

  await damageTypesHost.getByTestId("statistics-chart-hit-0").hover();
  await expect(tooltip.locator(".bpp-chart-tooltip-title")).toHaveText(
    "Direct damage",
  );
  const damageTypeRows = tooltip.locator(".bpp-chart-tooltip-row");
  await expect(damageTypeRows.nth(0)).toContainText("Player");
  await expect(damageTypeRows.nth(0)).toContainText("100");
  await expect(damageTypeRows.nth(1)).toContainText("Opponent");
  await expect(damageTypeRows.nth(1)).toContainText("200");

  const burnHeader = page.getByTestId("statistics-activity-sort-burn");
  expect(
    await burnHeader.evaluate(
      (element) => getComputedStyle(element).borderRadius,
    ),
  ).toBe("0px");
  const sortableHeaders = [
    page.getByTestId("statistics-activity-sort-triggers"),
    page.getByTestId("statistics-activity-sort-damage"),
    burnHeader,
  ];
  for (const header of sortableHeaders) {
    const insets = await header.evaluate((element) => {
      const bounds = element.getBoundingClientRect();
      const content = Array.from(element.children)
        .map((child) => child.getBoundingClientRect())
        .filter((child) => child.width > 0 && child.height > 0);
      return {
        left: Math.min(...content.map((child) => child.left)) - bounds.left,
        right: bounds.right
          - Math.max(...content.map((child) => child.right)),
      };
    });
    expect(insets.left).toBeGreaterThanOrEqual(10);
    expect(insets.right).toBeGreaterThanOrEqual(10);
  }

  const labelPositions = async () =>
    Promise.all(
      sortableHeaders.map(async (header) => {
        const bounds = await header.locator("span.whitespace-nowrap").boundingBox();
        expect(bounds).not.toBeNull();
        return bounds.x;
      }),
    );
  const positionsBeforeSort = await labelPositions();
  await burnHeader.click();
  const positionsAfterSort = await labelPositions();
  positionsAfterSort.forEach((position, index) => {
    expect(Math.abs(position - positionsBeforeSort[index])).toBeLessThan(0.5);
  });
});

test("keeps mounted ECharts instances across statistics rerenders and disposes on unmount", async ({
  page,
}) => {
  await page.goto(`${chartReportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();
  await expect
    .poll(() =>
      page.evaluate(() => ({
        dispose: window.__BPP_VIEWER_TEST__?.echartsDisposeCount ?? -1,
        init: window.__BPP_VIEWER_TEST__?.echartsInitCount ?? -1,
      }))
    )
    .toEqual({ dispose: 0, init: 3 });

  await page.getByTestId("statistics-activity-sort-damage").click();
  await page.getByTestId("statistics-activity-group-by-side").click();
  await page.getByTestId("locale-switch").click();
  await expect
    .poll(() =>
      page.evaluate(() => ({
        dispose: window.__BPP_VIEWER_TEST__?.echartsDisposeCount ?? -1,
        init: window.__BPP_VIEWER_TEST__?.echartsInitCount ?? -1,
      }))
    )
    .toEqual({ dispose: 0, init: 3 });

  await page.getByTestId("report-tab-timeline").click();
  await expect
    .poll(() =>
      page.evaluate(() => ({
        dispose: window.__BPP_VIEWER_TEST__?.echartsDisposeCount ?? -1,
        init: window.__BPP_VIEWER_TEST__?.echartsInitCount ?? -1,
      }))
    )
    .toEqual({ dispose: 3, init: 3 });
});

test("distinguishes damage kinds and treats the selected lane as the implicit target", async ({
  page,
}) => {
  await page.goto(`${damageKindsReportUrl}?lang=en`);
  const markers = [
    {
      dx: 0,
      dy: -11,
      label: "Direct damage",
      groupToken: "damage-direct",
      icon:
        "../report-assets/objects/00/0000000000000000000000000000000000000000000000000000000000000000.png",
    },
    {
      dx: -10,
      dy: 11,
      label: "Burn",
      groupToken: "damage-burn",
      icon:
        "../report-assets/objects/11/1111111111111111111111111111111111111111111111111111111111111111.png",
    },
    {
      dx: 10,
      dy: 11,
      label: "Poison",
      groupToken: "damage-poison",
      icon:
        "../report-assets/objects/22/2222222222222222222222222222222222222222222222222222222222222222.png",
    },
  ];
  for (const marker of markers) {
    let point = null;
    await expect
      .poll(async () => {
        point = await timelineMarkerPoint(page, {
          combatMs: 2_000,
          durationMs: 8_000,
          entityId: "opponent-hero",
          dx: marker.dx,
          dy: marker.dy,
        });
        if (!point) return "";
        await page.mouse.move(point.x, point.y);
        return (
          (await page
            .getByTestId("timeline-tooltip-label")
            .textContent()) ?? ""
        ).trim();
      })
      .toBe(marker.label);
    await expect(page.getByTestId("timeline-tooltip-count")).toHaveText(
      "1 event",
    );
    expect(point).not.toBeNull();
    await page.mouse.click(point.x, point.y);

    await expect(page.getByTestId("frame-inspector-entity")).toHaveText(
      "Fixture Opponent",
    );
    await expect(page.getByTestId("frame-event-kind")).toHaveText(
      marker.label,
    );
    await expect(page.getByTestId("focused-cluster-group")).toHaveAttribute(
      "data-bpp-event-token",
      marker.groupToken,
    );
    await expect(page.getByTestId("focused-cluster-event")).toHaveCount(1);
    await expect(page.getByTestId("event-source-entity")).toHaveCount(1);
    await expect(page.getByTestId("event-target-entity")).toHaveCount(0);
    await expect(page.getByTestId("frame-event-native-icon")).toHaveAttribute(
      "src",
      marker.icon,
    );
    await expect(
      page
        .getByTestId("frame-event-list")
        .locator('[data-slot="accordion-trigger"]'),
    ).toHaveCount(0);

    if (marker.groupToken === "damage-direct") {
      const sourceTreeGeometry = await page
        .getByTestId("focused-cluster-event")
        .evaluate((element) => {
          const line = element.querySelector(
            '[data-bpp-test-id="frame-event-relation-line"]',
          );
          const branch = element.querySelector(
            '[data-bpp-test-id="frame-event-relation-branch"]',
          );
          const source = element.querySelector(
            '[data-bpp-test-id="event-source-entity"]',
          );
          const styles = (node) => node ? getComputedStyle(node) : null;
          return {
            branchWidth: styles(branch)?.borderTopWidth ?? "0px",
            lineWidth: styles(line)?.borderLeftWidth ?? "0px",
            sourceText: source?.textContent?.trim() ?? "",
          };
        });
      expect(sourceTreeGeometry.branchWidth).not.toBe("0px");
      expect(sourceTreeGeometry.lineWidth).not.toBe("0px");
      expect(sourceTreeGeometry.sourceText).toContain("Training Blade");
    }
    await page.getByTestId("frame-inspector-close").click();
  }
});

test("lays out five same-time lane markers as separate hit targets", async ({
  page,
}) => {
  await page.goto(`${markerLayoutReportUrl}?lang=en`);
  const markers = [
    { dx: -20, dy: -11, label: "Direct damage" },
    { dx: 0, dy: -11, label: "Burn" },
    { dx: 20, dy: -11, label: "Poison" },
    { dx: -10, dy: 11, label: "Healing" },
    { dx: 10, dy: 11, label: "Shield" },
  ];
  const points = [];
  for (const marker of markers) {
    let point = null;
    await expect
      .poll(async () => {
        point = await timelineMarkerPoint(page, {
          combatMs: 4_000,
          durationMs: 8_000,
          entityId: "player-hero",
          dx: marker.dx,
          dy: marker.dy,
        });
        if (!point) return "";
        await page.mouse.move(point.x, point.y);
        return (
          (await page.getByTestId("timeline-tooltip-label").textContent())
          ?? ""
        ).trim();
      })
      .toBe(marker.label);
    await expect(page.getByTestId("timeline-tooltip-count")).toHaveText(
      "1 event",
    );
    points.push(point);
  }
  for (let left = 0; left < points.length; left += 1) {
    for (let right = left + 1; right < points.length; right += 1) {
      expect(
        Math.hypot(
          points[left].x - points[right].x,
          points[left].y - points[right].y,
        ),
      ).toBeGreaterThan(16);
    }
  }
});

test("renders destroy and structural attributes consistently across timeline, inspector, and statistics", async ({
  page,
}) => {
  await page.goto(`${structuralReportUrl}?lang=en`);

  const destroyPoint = await timelineMarkerPoint(page, {
    combatMs: 4_000,
    durationMs: 8_000,
    entityId: "player-item",
  });
  expect(destroyPoint).not.toBeNull();
  await page.mouse.move(destroyPoint.x, destroyPoint.y);
  await expect(page.getByTestId("timeline-tooltip-label")).toHaveText(
    "Destroyed",
  );
  await page.mouse.click(destroyPoint.x, destroyPoint.y);
  await expect(page.getByTestId("frame-inspector-entity")).toHaveText(
    "Ice Swan",
  );
  await expect(page.getByTestId("frame-event-kind")).toHaveText(
    "Destroyed",
  );
  await expect(page.getByTestId("event-source-entity")).toHaveText(
    "Disintegration Ray",
  );
  await page.getByTestId("frame-inspector-close").click();

  const attributePoint = await timelineMarkerPoint(page, {
    combatMs: 4_000,
    durationMs: 8_000,
    entityId: "player-item-multicast",
  });
  expect(attributePoint).not.toBeNull();
  await page.mouse.move(attributePoint.x, attributePoint.y);
  await expect(page.getByTestId("timeline-tooltip-label")).toHaveText(
    "Attribute change",
  );
  await expect(page.getByTestId("timeline-tooltip-count")).toHaveText(
    "2 events",
  );
  await page.mouse.click(attributePoint.x, attributePoint.y);
  await expect(page.getByTestId("frame-inspector-entity")).toHaveText(
    "Zarlic",
  );
  await expect(page.getByTestId("frame-event-kind")).toHaveText([
    "Damage stat",
    "Multicast",
  ]);
  await expect(page.getByTestId("frame-event-amount")).toHaveText([
    "+20",
    "−1",
  ]);
  await expect(page.getByTestId("frame-event-transition")).toHaveText([
    "10 → 30",
    "2 → 1",
  ]);
  await expect(page.getByTestId("focused-cluster-event").first()).toHaveAttribute(
    "data-bpp-diff-polarity",
    "increase",
  );
  await expect(page.getByTestId("focused-cluster-event").nth(1)).toHaveAttribute(
    "data-bpp-diff-polarity",
    "decrease",
  );
  await expect(page.getByTestId("event-source-entity")).toHaveCount(0);

  await page.getByTestId("frame-inspector-close").click();
  await page.getByTestId("report-tab-statistics").click();
  const destroyRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Disintegration Ray" });
  await expect(
    destroyRow.getByTestId("statistics-activity-value-destroy"),
  ).toContainText("×1");
  await destroyRow
    .getByTestId("statistics-activity-value-destroy")
    .hover();
  const activityTooltip = page.locator(
    '[data-bpp-test-id="statistics-activity-cell-tooltip"]:not([data-state="closed"])',
  );
  await expect(activityTooltip).toBeVisible();
  await expect(activityTooltip).toContainText("Effects by target");
  await expect(
    activityTooltip.getByTestId("statistics-activity-target-name"),
  ).toHaveText(["Ice Swan"]);
  await expect(
    activityTooltip.getByTestId("statistics-activity-target-count"),
  ).toHaveText(["×1"]);

  const zarlicRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Zarlic" });
  await expect(
    zarlicRow.getByTestId("statistics-activity-value-damageModifier"),
  ).toContainText("+20");
  await expect(
    zarlicRow.getByTestId("statistics-activity-value-multicast"),
  ).toContainText("-1");

  const sorbetRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Sorbet" });
  await expect(
    sorbetRow.getByTestId("statistics-activity-value-cooldownReduction"),
  ).toContainText("+10%");
  await expect(
    sorbetRow.getByTestId("statistics-activity-value-critChance"),
  ).toContainText("+5%");
  await expect(
    page.getByTestId("statistics-activity-sort-destroy"),
  ).toContainText("Destroyed");
});

test("lists every affected target in an activity tooltip", async ({
  page,
}) => {
  await page.goto(`${structuralReportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();
  const sourceRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Disintegration Ray" });
  await sourceRow
    .getByTestId("statistics-activity-value-haste")
    .hover();
  const tooltip = page.locator(
    '[data-bpp-test-id="statistics-activity-cell-tooltip"]:not([data-state="closed"])',
  );

  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText("Effects by target");
  await expect(
    tooltip.getByTestId("statistics-activity-target-name"),
  ).toHaveText(["Zarlic", "Sorbet"]);
  await expect(
    tooltip.getByTestId("statistics-activity-target-amount"),
  ).toHaveText(["2s", "2s"]);
  await expect(
    tooltip.getByTestId("statistics-activity-target-count"),
  ).toHaveText(["×1", "×1"]);
});

test("shows a target's structural transition in its activity tooltip", async ({
  page,
}) => {
  await page.goto(`${structuralReportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();
  const zarlicRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Zarlic" });
  await zarlicRow
    .getByTestId("statistics-activity-value-multicast")
    .hover();
  const tooltip = page.locator(
    '[data-bpp-test-id="statistics-activity-cell-tooltip"]:not([data-state="closed"])',
  );

  await expect(tooltip).toBeVisible();
  await expect(
    tooltip.getByTestId("statistics-activity-target-name"),
  ).toHaveText(["Zarlic"]);
  await expect(
    tooltip.getByTestId("statistics-activity-target-transition"),
  ).toHaveText("2 → 1");
  await expect(
    tooltip.getByTestId("statistics-activity-target-amount"),
  ).toHaveText("-1");
  await expect(
    tooltip.getByTestId("statistics-activity-target-count"),
  ).toHaveText("×1");
  await expect(tooltip).not.toContainText("Average");
  await expect(tooltip).not.toContainText("First → last");
});

test("groups same-frame direct damage sources under one exact total", async ({
  page,
}) => {
  await page.goto(`${directDamageSummaryReportUrl}?lang=en`);
  const canvas = page.getByTestId("timeline-canvas");
  let point = null;
  await expect
    .poll(async () => {
      const bounds = await canvas.boundingBox();
      if (!bounds) return "";
      point = {
        x: bounds.x + bounds.width * 0.25,
        y: bounds.y + 3.5 * 52 - 12,
      };
      await page.mouse.move(point.x, point.y);
      return (
        (await page.getByTestId("timeline-tooltip-count").textContent()) ?? ""
      ).trim();
    })
    .toBe("2 events");
  expect(point).not.toBeNull();
  await page.mouse.click(point.x, point.y);

  await expect(page.getByTestId("frame-event-total")).toHaveText("2 events");
  await expect(page.getByTestId("focused-cluster-event")).toHaveCount(1);
  await expect(page.getByTestId("frame-event-kind")).toHaveText(
    "Direct damage",
  );
  await expect(page.getByTestId("frame-event-native-icon")).toHaveAttribute(
    "src",
    "../report-assets/objects/33/3333333333333333333333333333333333333333333333333333333333333333.png",
  );
  await expect(page.getByTestId("frame-event-amount")).toHaveText("1,430");
  await expect(page.getByTestId("event-source-entity")).toHaveText([
    "Silver Stake",
    "Wolf",
  ]);
  await expect(page.getByTestId("frame-event-relation-branch")).toHaveCount(2);
  await expect(page.getByTestId("frame-event-list")).not.toContainText("740");
  await expect(page.getByTestId("frame-event-list")).not.toContainText("690");
});

test("scopes the inspector to the exact clicked cluster", async ({
  page,
}) => {
  await page.goto(`${denseReportUrl}?lang=en`);

  const canvas = page.getByTestId("timeline-canvas");
  const timelineWidthBeforeSelection = await page
    .getByTestId("timeline-scroll")
    .evaluate((element) => element.getBoundingClientRect().width);
  const clickCluster = async (laneOffset, expectedCount) => {
    let point = null;
    await expect
      .poll(async () => {
        const bounds = await canvas.boundingBox();
        if (!bounds) return "";
        point = {
          x: bounds.x + bounds.width * 0.25,
          y: bounds.y + laneOffset,
        };
        await page.mouse.move(point.x, point.y);
        return (
          (await page
            .getByTestId("timeline-tooltip-count")
            .textContent()) ?? ""
        ).trim();
      })
      .toBe(expectedCount);
    expect(point).not.toBeNull();
    await page.mouse.click(point.x, point.y);
  };

  await clickCluster(3.5 * 52 - 9, "80 events");

  await expect(page.getByTestId("frame-inspector-popover")).toBeVisible();
  await expect(page.getByTestId("frame-inspector")).toBeVisible();
  await expect(page.getByTestId("frame-inspector-entity")).toHaveText(
    "Fixture Opponent",
  );
  await expect(page.getByTestId("frame-inspector-entity-type")).toHaveText(
    "hero",
  );
  await expect(page.getByTestId("frame-inspector-time")).toContainText("s");
  await expect(page.getByTestId("frame-inspector-frame")).toContainText(
    "Frame",
  );
  await expect(page.getByTestId("frame-event-total")).toHaveText("80 events");
  await expect(page.getByTestId("frame-event-relation")).toHaveCount(80);
  await expect(
    page.getByTestId("frame-event-list").locator("details"),
  ).toHaveCount(0);
  await expect(
    page.getByTestId("frame-event-list").locator('[data-slot="card"]'),
  ).toHaveCount(0);
  await expect(page.getByTestId("frame-event-list")).not.toContainText(
    "Original records",
  );
  await expect(page.getByTestId("focused-cluster-group")).toHaveAttribute(
    "data-bpp-event-token",
    "damage-direct",
  );
  await expect(page.getByTestId("focused-cluster-event")).toHaveCount(80);
  await expect(page.getByTestId("frame-event-list")).not.toContainText(
    "Healing",
  );
  await expect(page.getByTestId("event-source-entity").first()).toContainText(
    "Training Blade",
  );
  await expect(
    page
      .getByTestId("event-source-entity")
      .first()
      .locator('[data-entity-type="item"]'),
  ).toHaveCount(1);
  await expect(page.getByTestId("event-target-entity")).toHaveCount(0);
  await expect(
    page.getByTestId("focused-cluster-event").first().getByText("Source", {
      exact: true,
    }),
  ).toHaveCount(0);
  await expect(
    page.getByTestId("focused-cluster-event").first().getByText("Target", {
      exact: true,
    }),
  ).toHaveCount(0);
  const relationGeometry = await page
    .getByTestId("focused-cluster-event")
    .first()
    .evaluate((element) => {
      const source = element.querySelector(
        '[data-bpp-test-id="event-source-entity"]',
      );
      const line = element.querySelector(
        '[data-bpp-test-id="frame-event-relation-line"]',
      );
      const branch = element.querySelector(
        '[data-bpp-test-id="frame-event-relation-branch"]',
      );
      const eventBounds = element.getBoundingClientRect();
      const sourceBounds = source?.getBoundingClientRect();
      const lineStyle = line ? getComputedStyle(line) : null;
      const branchStyle = branch ? getComputedStyle(branch) : null;
      return {
        eventLeft: eventBounds.left,
        sourceLeft: sourceBounds?.left ?? 0,
        branchWidth: branchStyle?.borderTopWidth ?? "0px",
        lineWidth: lineStyle?.borderLeftWidth ?? "0px",
      };
    });
  expect(relationGeometry.sourceLeft).toBeGreaterThan(
    relationGeometry.eventLeft,
  );
  expect(relationGeometry.branchWidth).not.toBe("0px");
  expect(relationGeometry.lineWidth).not.toBe("0px");
  await expect(page.getByTestId("timeline-lane-1")).toHaveClass(
    /is-related-source/u,
  );
  const inspectorGeometry = await page.evaluate(() => {
    const inspector = document.querySelector(
      '[data-bpp-test-id="frame-inspector"]',
    );
    const inspectorHeader = document.querySelector(
      '[data-bpp-test-id="frame-inspector-header"]',
    );
    const popover = document.querySelector(
      '[data-bpp-test-id="frame-inspector-popover"]',
    );
    const inspectorBounds = inspector?.getBoundingClientRect();
    const headerBounds = inspectorHeader?.getBoundingClientRect();
    const popoverBounds = popover?.getBoundingClientRect();
    return {
      headerHeight: headerBounds?.height ?? 0,
      inspectorWidth: inspectorBounds?.width ?? 0,
      popoverBottom: popoverBounds?.bottom ?? 0,
      popoverLeft: popoverBounds?.left ?? 0,
      popoverRight: popoverBounds?.right ?? 0,
      popoverTop: popoverBounds?.top ?? 0,
    };
  });
  expect(inspectorGeometry.inspectorWidth).toBeGreaterThanOrEqual(320);
  expect(inspectorGeometry.inspectorWidth).toBeLessThanOrEqual(361);
  expect(inspectorGeometry.headerHeight).toBeLessThanOrEqual(56);
  expect(inspectorGeometry.popoverLeft).toBeGreaterThanOrEqual(0);
  expect(inspectorGeometry.popoverTop).toBeGreaterThanOrEqual(0);
  expect(inspectorGeometry.popoverRight).toBeLessThanOrEqual(
    page.viewportSize().width,
  );
  expect(inspectorGeometry.popoverBottom).toBeLessThanOrEqual(
    page.viewportSize().height,
  );
  await expect
    .poll(() =>
      page
        .getByTestId("timeline-scroll")
        .evaluate((element) => element.getBoundingClientRect().width)
    )
    .toBe(timelineWidthBeforeSelection);
  await page.mouse.move(2, 2);
  await expect(page.getByTestId("timeline-lane-1")).toHaveClass(
    /is-related-source/u,
  );
  await expect(page.getByTestId("timeline-tooltip")).toBeHidden();
  const canvasBoundsWithPopover = await canvas.boundingBox();
  expect(canvasBoundsWithPopover).not.toBeNull();
  await page.mouse.move(
    canvasBoundsWithPopover.x + canvasBoundsWithPopover.width * 0.75,
    canvasBoundsWithPopover.y + 3.5 * 52 - 9,
  );
  await expect(page.getByTestId("timeline-tooltip")).toBeHidden();

  await page.getByTestId("frame-inspector-close").click();
  await expect(page.getByTestId("frame-inspector")).toBeHidden();
  await expect(page.getByTestId("frame-inspector-popover")).toBeHidden();
  await expect(page.getByTestId("timeline-lane-1")).not.toHaveClass(
    /is-related-source/u,
  );

  await clickCluster(3.5 * 52 + 9, "1 event");
  await expect(page.getByTestId("frame-inspector-entity")).toHaveText(
    "Fixture Opponent",
  );
  await expect(page.getByTestId("focused-cluster-group")).toHaveAttribute(
    "data-bpp-event-token",
    "heal",
  );
  await expect(page.getByTestId("focused-cluster-event")).toHaveAttribute(
    "data-bpp-event-id",
    "dense-heal",
  );
  await expect(page.getByTestId("frame-event-total")).toHaveText("1 event");
  await expect(
    page
      .getByTestId("focused-cluster-group")
      .locator('[data-slot="accordion-trigger"]'),
  ).toHaveCount(0);
  const popoverMotion = await page
    .getByTestId("frame-inspector-popover")
    .evaluate((element) => {
      const style = getComputedStyle(element);
      return {
        animationDuration: style.animationDuration,
        animationName: style.animationName,
      };
    });
  expect(popoverMotion.animationName).not.toBe("none");
  expect(Number.parseFloat(popoverMotion.animationDuration)).toBeGreaterThanOrEqual(
    0.15,
  );
  await expect(page.getByTestId("event-source-entity")).toContainText(
    "Practice Shield",
  );
  await expect(page.getByTestId("timeline-lane-4")).toHaveClass(
    /is-related-source/u,
  );
  await page.mouse.move(2, 2);
  await expect(page.getByTestId("timeline-lane-4")).toHaveClass(
    /is-related-source/u,
  );

  await page.getByTestId("frame-inspector-close").click();
  await expect(page.getByTestId("frame-inspector")).toBeHidden();
  await expect(page.getByTestId("frame-inspector-popover")).toBeHidden();
  await expect(page.getByTestId("timeline-lane-4")).not.toHaveClass(
    /is-related-source/u,
  );
});

test("keeps the page chrome bounded while the timeline owns horizontal overflow", async ({
  page,
}) => {
  await page.setViewportSize({ width: 480, height: 857 });
  await page.goto(`${scrollRecordingReportUrl}?lang=zh-CN`);

  const dimensions = await page.evaluate(() => {
    const timeline = document.querySelector(
      "[data-bpp-test-id='timeline-scroll']",
    );
    return {
      documentClientWidth: document.documentElement.clientWidth,
      documentScrollWidth: document.documentElement.scrollWidth,
      timelineClientWidth: timeline?.clientWidth ?? 0,
      timelineScrollWidth: timeline?.scrollWidth ?? 0,
    };
  });
  expect(dimensions.documentScrollWidth).toBeLessThanOrEqual(
    dimensions.documentClientWidth,
  );
  expect(dimensions.timelineScrollWidth).toBeGreaterThan(
    dimensions.timelineClientWidth,
  );
  const timeline = page.getByTestId("timeline-scroll");
  await timeline.evaluate((element) => {
    element.scrollLeft = 0;
    element.scrollTop = 0;
  });
  await timeline.hover();
  await page.mouse.wheel(0, 240);
  expect(await timeline.evaluate((element) => element.scrollLeft)).toBe(0);
  await expect
    .poll(() => timeline.evaluate((element) => element.scrollTop))
    .toBeGreaterThan(0);
  const verticalScrollTop = await timeline.evaluate((element) =>
    element.scrollTop
  );
  await page.keyboard.down("Shift");
  await page.mouse.wheel(0, -120);
  await page.keyboard.up("Shift");
  expect(await timeline.evaluate((element) => element.scrollLeft)).toBe(0);
  expect(await timeline.evaluate((element) => element.scrollTop)).toBe(
    verticalScrollTop,
  );
  await page.keyboard.down("Shift");
  await page.mouse.wheel(0, 240);
  await page.keyboard.up("Shift");
  await expect
    .poll(() => timeline.evaluate((element) => element.scrollLeft))
    .toBeGreaterThan(0);
  expect(await timeline.evaluate((element) => element.scrollTop)).toBe(
    verticalScrollTop,
  );
  const shiftedScrollLeft = await timeline.evaluate((element) =>
    element.scrollLeft
  );
  await page.keyboard.down("Shift");
  await page.mouse.wheel(0, -120);
  await page.keyboard.up("Shift");
  await expect
    .poll(() => timeline.evaluate((element) => element.scrollLeft))
    .toBeLessThan(shiftedScrollLeft);
  expect(await timeline.evaluate((element) => element.scrollTop)).toBe(
    verticalScrollTop,
  );
  await expect(page.getByTestId("report-tab-timeline")).toHaveText("时间轴");
});

test("gives the timeline the viewport and keeps at least ten lanes visible", async ({
  page,
}) => {
  await page.setViewportSize({ width: 838, height: 857 });
  await page.goto(`${reportUrl}?lang=en`);
  const geometry = await page.getByTestId("timeline-scroll").evaluate((scroll) => {
    const firstLane = document.querySelector(
      "[data-bpp-test-id='timeline-lane-0']",
    );
    const laneHeight = firstLane?.getBoundingClientRect().height ?? 0;
    const labels = document.querySelector(
      "[data-bpp-test-id='timeline-lane-labels']",
    );
    const labelsTop = labels?.getBoundingClientRect().top ?? 0;
    const footer = document.querySelector(
      "[data-bpp-test-id='workbench-footer']",
    );
    const section = document.querySelector(
      "[data-bpp-test-id='timeline-section']",
    );
    const scrollBounds = scroll.getBoundingClientRect();
    const footerBounds = footer?.getBoundingClientRect();
    const sectionBounds = section?.getBoundingClientRect();
    return {
      clientHeight: scroll.clientHeight,
      footerHeight: footerBounds?.height ?? 0,
      footerScrollGap:
        footerBounds === undefined
          ? Number.NaN
          : footerBounds.top - scrollBounds.bottom,
      footerSectionGap:
        footerBounds === undefined || sectionBounds === undefined
          ? Number.NaN
          : footerBounds.top - sectionBounds.bottom,
      laneHeight,
      visibleLaneHeight: (footerBounds?.top ?? window.innerHeight) - labelsTop,
    };
  });
  expect(geometry.clientHeight).toBeGreaterThanOrEqual(750);
  expect(geometry.footerHeight).toBe(36);
  expect(geometry.footerScrollGap).toBeGreaterThanOrEqual(0);
  expect(geometry.footerScrollGap).toBeLessThanOrEqual(1);
  expect(Math.abs(geometry.footerSectionGap)).toBeLessThan(0.5);
  expect(geometry.visibleLaneHeight / geometry.laneHeight).toBeGreaterThanOrEqual(
    10,
  );
});

test("virtualizes the footer combat log and highlights every visible row from the active frame", async ({
  page,
}) => {
  await page.addInitScript(() => {
    const originalScrollTo = Element.prototype.scrollTo;
    window.__bppCombatLogScrollBehaviors = [];
    Element.prototype.scrollTo = function scrollTo(...args) {
      if (
        this.getAttribute?.("data-bpp-test-id") === "combat-log-viewport"
        && typeof args[0] === "object"
        && args[0] !== null
      ) {
        window.__bppCombatLogScrollBehaviors.push(
          args[0].behavior ?? "auto",
        );
      }
      return originalScrollTo.apply(this, args);
    };
  });
  await page.goto(`${denseReportUrl}?lang=en`);
  await page.getByTestId("combat-log-dock-toggle").click();

  const dock = page.getByTestId("footer-replay-dock");
  const log = page.getByTestId("combat-log");
  const rows = page.getByTestId("combat-log-entry");
  await expect(dock).toBeVisible();
  await expect(log).toHaveAttribute("data-bpp-total-count", "84");
  await expect(rows).not.toHaveCount(0);
  expect(await rows.count()).toBeLessThan(84);
  expect((await rows.first().boundingBox()).height).toBeLessThanOrEqual(47);
  await expect(rows.first()).toHaveAttribute("data-bpp-frame-start", "true");
  const nativeKindIcons = page.getByTestId("combat-log-kind-native-icon");
  await expect(nativeKindIcons.first()).toBeVisible();
  await expect(nativeKindIcons.first()).toHaveAttribute(
    "src",
    /report-assets\/objects\/.+\.png$/,
  );
  const kindColumnGeometry = await rows.first().evaluate((element) => {
    const kind = element.querySelector(
      '[data-bpp-test-id="combat-log-kind"]',
    )?.getBoundingClientRect();
    const source = element.querySelector(
      '[data-bpp-test-id="combat-log-source"]',
    )?.getBoundingClientRect();
    return {
      width: kind?.width ?? Number.NaN,
      trailingGap:
        kind === undefined || source === undefined
          ? Number.NaN
          : source.left - kind.right,
    };
  });
  expect(kindColumnGeometry.width).toBeLessThanOrEqual(78);
  expect(kindColumnGeometry.trailingGap).toBeLessThanOrEqual(9);
  const columnAlignment = await rows.evaluateAll((elements) => {
    const testIds = [
      "combat-log-time",
      "combat-log-kind",
      "combat-log-source",
      "combat-log-source-label",
      "combat-log-relation",
      "combat-log-target",
      "combat-log-target-label",
      "combat-log-amount",
    ];
    return Object.fromEntries(
      testIds.map((testId) => {
        const positions = elements.slice(0, 6).map((element) =>
          element.querySelector(
            `[data-bpp-test-id="${testId}"]`,
          )?.getBoundingClientRect().left
        );
        return [testId, positions];
      }),
    );
  });
  for (const [testId, positions] of Object.entries(columnAlignment)) {
    expect(
      positions.every((position) => Number.isFinite(position)),
      `${testId} should exist in every sampled row`,
    ).toBe(true);
    expect(
      Math.max(...positions) - Math.min(...positions),
      `${testId} should stay on one fixed column`,
    ).toBeLessThanOrEqual(1);
  }
  expect(
    await rows.evaluateAll((elements) =>
      elements.every((element) => {
        const relation = element.querySelector(
          '[data-bpp-test-id="combat-log-relation"]',
        );
        return relation?.textContent?.trim() === ""
          && relation.querySelector("svg") === null;
      })
    ),
  ).toBe(true);
  expect(
    await rows.evaluateAll((elements) =>
      elements.some(
        (element) =>
          element.getAttribute("data-bpp-frame-start") === "false",
      )
    ),
  ).toBe(true);
  const sameFrameHighlight = await rows.evaluateAll((elements) => {
    const highlighted = elements.filter(
      (element) => element.getAttribute("data-bpp-same-frame") === "true",
    );
    return {
      count: highlighted.length,
      frames: Array.from(
        new Set(
          highlighted.map((element) =>
            element.getAttribute("data-bpp-frame")
          ),
        ),
      ),
    };
  });
  expect(sameFrameHighlight.count).toBeGreaterThan(1);
  expect(sameFrameHighlight.frames).toEqual(["40"]);
  await expect(
    page.locator(
      '[data-bpp-test-id="combat-log-entry"][data-bpp-active="true"]',
    ),
  ).toHaveCount(1);
  const activeEntry = page.locator(
    '[data-bpp-test-id="combat-log-entry"][data-bpp-active="true"]',
  );
  await expect(activeEntry).toHaveAttribute("data-bpp-combat-ms", "2000");
  await expect(activeEntry).toHaveAttribute("aria-current", "true");
  await expect(activeEntry).toContainText("Currently selected event");
  const otherEntryAtFrame = page.locator(
    '[data-bpp-test-id="combat-log-entry"]'
      + '[data-bpp-same-frame="true"][data-bpp-active="false"]',
  ).first();
  await expect(otherEntryAtFrame).not.toHaveAttribute("aria-current", "true");
  await expect(otherEntryAtFrame).toContainText(
    "Occurred in the same frame as the current event",
  );
  const selectedIndex = await otherEntryAtFrame.getAttribute("data-index");
  expect(selectedIndex).not.toBeNull();
  await otherEntryAtFrame.click();
  await expect(
    page.locator(
      `[data-bpp-test-id="combat-log-entry"][data-index="${selectedIndex}"]`,
    ),
  ).toHaveAttribute("data-bpp-active", "true");
  await expect(
    page.locator(
      '[data-bpp-test-id="combat-log-entry"][data-bpp-same-frame="true"]',
    ),
  ).toHaveCount(sameFrameHighlight.count);
  await expect(page.getByTestId("frame-inspector-popover")).toHaveCount(0);

  const viewport = page.getByTestId("combat-log-viewport");
  const pinnedScrollTop = await viewport.evaluate((element) =>
    element.scrollTop
  );
  const ruler = page.getByTestId("timeline-ruler-canvas");
  const rulerBounds = await ruler.boundingBox();
  expect(rulerBounds).not.toBeNull();
  const previewAtRulerEnd = () => ruler.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    element.dispatchEvent(new PointerEvent("pointermove", {
      bubbles: true,
      clientX: bounds.left + bounds.width * 0.95,
      clientY: bounds.top + bounds.height * 0.5,
    }));
  });
  await page.evaluate(() => {
    window.__bppCombatLogScrollBehaviors.length = 0;
  });
  await previewAtRulerEnd();
  await expect(log).toHaveAttribute("data-bpp-follow-source", "hover");
  await expect(activeEntry).not.toHaveAttribute("data-index", selectedIndex);
  await expect
    .poll(async () => viewport.evaluate((element) => element.scrollTop))
    .not.toBe(pinnedScrollTop);
  await expect
    .poll(() =>
      page.evaluate(() =>
        window.__bppCombatLogScrollBehaviors.at(-1) ?? ""
      )
    )
    .toBe("auto");
  expect(
    await page.evaluate(() =>
      window.__bppCombatLogScrollBehaviors.includes("smooth")
    ),
  ).toBe(false);
  await expect
    .poll(() =>
      page.evaluate(() => {
        const viewport = document.querySelector(
          '[data-bpp-test-id="combat-log-viewport"]',
        );
        const active = document.querySelector(
          '[data-bpp-test-id="combat-log-entry"][data-bpp-active="true"]',
        );
        const viewportBounds = viewport?.getBoundingClientRect();
        const activeBounds = active?.getBoundingClientRect();
        return Boolean(
          viewportBounds
          && activeBounds
          && activeBounds.top >= viewportBounds.top - 1
          && activeBounds.bottom <= viewportBounds.bottom + 1,
        );
      })
    )
    .toBe(true);
  await ruler.dispatchEvent("pointerout");
  await expect(activeEntry).toHaveAttribute("data-bpp-combat-ms", "2000");

  const scrollGeometry = await viewport.evaluate((element) => ({
    clientHeight: element.clientHeight,
    scrollHeight: element.scrollHeight,
  }));
  expect(scrollGeometry.scrollHeight).toBeGreaterThan(
    scrollGeometry.clientHeight,
  );
  await viewport.hover();
  await page.mouse.wheel(0, 240);
  await expect(page.getByTestId("combat-log-resume")).toBeVisible();
  await ruler.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    element.dispatchEvent(new PointerEvent("pointermove", {
      bubbles: true,
      clientX: bounds.left + bounds.width * 0.05,
      clientY: bounds.top + bounds.height * 0.5,
    }));
  });
  await expect(log).toHaveAttribute("data-bpp-follow-source", "hover");
  await expect(page.getByTestId("combat-log-resume")).toBeHidden();
  await ruler.dispatchEvent("pointerout");

  await viewport.hover();
  await page.mouse.wheel(0, 240);
  await expect(page.getByTestId("combat-log-resume")).toBeVisible();
  await page.getByTestId("combat-log-resume").click();
  await expect(page.getByTestId("combat-log-resume")).toBeHidden();
  await viewport.press("PageDown");
  await expect(page.getByTestId("combat-log-resume")).toBeVisible();
});

test("keeps direct freeze applications in the combat log without countdown tick noise", async ({
  page,
}) => {
  await page.goto(`${statusApplicationReportUrl}?lang=en`);
  await page.getByTestId("combat-log-dock-toggle").click();

  const healingRow = page
    .locator(
      '[data-bpp-test-id="combat-log-entry"][data-bpp-combat-ms="8550"]',
    )
    .filter({ hasText: "Healing" });
  await expect(healingRow).toHaveCount(1);
  await expect(
    healingRow.getByTestId("combat-log-kind-native-icon"),
  ).toHaveAttribute(
    "src",
    /report-assets\/objects\/11\/1{64}\.png$/,
  );
  await expect(
    healingRow.getByTestId("combat-log-kind").locator("svg"),
  ).toHaveCount(0);

  const freezeRow = page
    .getByTestId("combat-log-entry")
    .filter({ hasText: "Freeze" });
  await expect(freezeRow).toHaveCount(1);
  await expect(freezeRow).toContainText("Petrifying Gaze");
  await expect(freezeRow.getByTestId("combat-log-target-summary"))
    .toHaveText("Targets");
  await expect(freezeRow.getByTestId("combat-log-entry-expand"))
    .toContainText("×2");
  await expect(freezeRow.getByTestId("combat-log-target-label")).toHaveCount(0);
  await expect(freezeRow).toContainText("1s");
  await expect(freezeRow).toContainText("×2");
  await expect(freezeRow.getByTestId("combat-log-relation")).toBeEmpty();
  await expect(page.getByTestId("combat-log-tree-root-arm")).toHaveCount(0);
  await expect(page.getByTestId("combat-log-tree-root-spine")).toHaveCount(0);
  await freezeRow.click();
  await expect(freezeRow).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByTestId("combat-log-arrow")).toHaveCount(0);
  const rootGeometry = await freezeRow.evaluate((row) => {
    const root = row.querySelector(
      '[data-bpp-test-id="combat-log-relation"]',
    )?.getBoundingClientRect();
    const arm = row.querySelector(
      '[data-bpp-test-id="combat-log-tree-root-arm"]',
    )?.getBoundingClientRect();
    const spine = row.querySelector(
      '[data-bpp-test-id="combat-log-tree-root-spine"]',
    )?.getBoundingClientRect();
    if (!root || !arm || !spine) return null;
    return {
      rootLeft: root.left,
      rootWidth: root.width,
      armLeft: arm.left,
      armRight: arm.right,
      armWidth: arm.width,
      spineLeft: spine.left,
    };
  });
  expect(rootGeometry).not.toBeNull();
  expect(rootGeometry.armLeft).toBeCloseTo(
    rootGeometry.rootLeft + rootGeometry.rootWidth / 2,
    0,
  );
  expect(rootGeometry.armRight).toBeCloseTo(
    rootGeometry.rootLeft + rootGeometry.rootWidth,
    0,
  );
  expect(rootGeometry.armWidth).toBeLessThan(rootGeometry.rootWidth * 0.6);
  expect(rootGeometry.spineLeft).toBeCloseTo(rootGeometry.armLeft, 0);
  const detailGroup = page.getByTestId("combat-log-entry-details");
  await expect(detailGroup).toBeVisible();
  const detailRows = page.getByTestId("combat-log-entry-detail");
  await expect(detailRows).toHaveCount(2);
  await expect(detailRows.nth(0)).toContainText("Practice Shield");
  await expect(detailRows.nth(1)).toContainText("Cash Cannon");
  await expect(
    detailRows.getByTestId("combat-log-detail-target-label"),
  ).toHaveText(["Practice Shield", "Cash Cannon"]);
  await expect(page.getByTestId("combat-log-detail-target")).toHaveCount(2);
  await expect(page.getByTestId("combat-log-tree-branch")).toHaveCount(2);
  await expect(page.getByTestId("combat-log-tree-spine")).toHaveCount(2);
  await expect(page.getByTestId("combat-log-tree-elbow")).toHaveCount(2);
  const detailGeometry = await detailGroup.evaluate((group) => {
    const branches = Array.from(
      group.querySelectorAll('[data-bpp-test-id="combat-log-tree-branch"]'),
    );
    const targets = Array.from(
      group.querySelectorAll('[data-bpp-test-id="combat-log-detail-target"]'),
    );
    const spines = Array.from(
      group.querySelectorAll('[data-bpp-test-id="combat-log-tree-spine"]'),
    );
    const rows = Array.from(
      group.querySelectorAll('[data-bpp-test-id="combat-log-entry-detail"]'),
    );
    return {
      branchHeights: branches.map((node) => node.getBoundingClientRect().height),
      branchLefts: branches.map((node) => node.getBoundingClientRect().left),
      targetLefts: targets.map((node) => node.getBoundingClientRect().left),
      spineHeights: spines.map((node) => node.getBoundingClientRect().height),
      rowHeights: rows.map((node) => node.getBoundingClientRect().height),
      paddingBottom: Number.parseFloat(getComputedStyle(group).paddingBottom),
    };
  });
  expect(
    Math.max(...detailGeometry.branchLefts)
      - Math.min(...detailGeometry.branchLefts),
  ).toBeLessThanOrEqual(1);
  expect(
    detailGeometry.targetLefts.every(
      (left, index) => left > detailGeometry.branchLefts[index],
    ),
  ).toBe(true);
  expect(detailGeometry.rowHeights).toEqual([32, 32]);
  expect(detailGeometry.spineHeights[0]).toBeCloseTo(
    detailGeometry.branchHeights[0] + 1,
    0,
  );
  expect(detailGeometry.spineHeights[1]).toBeCloseTo(
    detailGeometry.branchHeights[1] / 2,
    0,
  );
  expect(detailGeometry.paddingBottom).toBe(4);
  await expect(page.getByTestId("frame-inspector-popover")).toHaveCount(0);
  await detailRows.nth(0).click();
  await expect(page.getByTestId("frame-inspector-popover")).toHaveCount(0);
  await freezeRow.click();
  await expect(freezeRow).toHaveAttribute("aria-expanded", "false");
  await expect(freezeRow.getByTestId("combat-log-relation")).toBeEmpty();
  await expect(page.getByTestId("combat-log-tree-root-arm")).toHaveCount(0);
  await expect(page.getByTestId("combat-log-entry-details")).toHaveCount(0);
  await expect(page.getByTestId("combat-log")).not.toContainText("-50ms");
});

test("embeds the existing recording instance in the footer dock", async ({
  page,
}) => {
  await page.goto(`${recordingReportUrl}?lang=en`);
  const video = page.getByTestId("recording-video");
  await expect(video).toHaveCount(1);
  await video.evaluate((element) => {
    element.dataset.identitySentinel = "stable-recording-video";
    element.playbackRate = 1.5;
    window.__bppRecordingVideo = element;
  });
  await video.dispatchEvent("play");
  await page.getByTestId("combat-log-dock-toggle").click();

  await expect(page.getByTestId("footer-replay-dock")).toBeVisible();
  await expect(page.getByTestId("combat-log")).toHaveAttribute(
    "data-bpp-follow-source",
    "playback",
  );
  const ruler = page.getByTestId("timeline-ruler-canvas");
  const rulerBounds = await ruler.boundingBox();
  expect(rulerBounds).not.toBeNull();
  await ruler.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    element.dispatchEvent(new PointerEvent("pointermove", {
      bubbles: true,
      clientX: bounds.left + bounds.width * 0.85,
      clientY: bounds.top + bounds.height * 0.5,
    }));
  });
  await expect(page.getByTestId("combat-log")).toHaveAttribute(
    "data-bpp-follow-source",
    "playback",
  );
  await expect(page.getByTestId("recording-window")).toHaveAttribute(
    "data-bpp-recording-presentation",
    "docked",
  );
  await expect(video).toHaveCount(1);
  expect(
    await page.evaluate(
      () =>
        document.querySelector(
          '[data-bpp-test-id="recording-video"]',
        ) === window.__bppRecordingVideo,
    ),
  ).toBe(true);
  await expect(video).toHaveAttribute(
    "data-identity-sentinel",
    "stable-recording-video",
  );
  expect(await video.evaluate((element) => element.playbackRate)).toBe(1.5);
  const geometry = await page.evaluate(() => {
      const host = document.querySelector(
        '[data-bpp-test-id="footer-recording-host"]',
      );
      const recording = document.querySelector(
        '[data-bpp-test-id="recording-window"]',
      );
      const media = document.querySelector(".bpp-recording-media");
      const dock = document.querySelector(
        '[data-bpp-test-id="footer-replay-dock"]',
      );
      if (!host || !recording || !media || !dock) return null;
      const hostBounds = host.getBoundingClientRect();
      const recordingBounds = recording.getBoundingClientRect();
      const mediaBounds = media.getBoundingClientRect();
      const dockBounds = dock.getBoundingClientRect();
      return {
        dock: {
          left: dockBounds.left,
          right: dockBounds.right,
        },
        host: {
          height: hostBounds.height,
          width: hostBounds.width,
        },
        recording: {
          height: recordingBounds.height,
          left: recordingBounds.left,
          right: recordingBounds.right,
          width: recordingBounds.width,
        },
        media: {
          bottomGap: recordingBounds.bottom - mediaBounds.bottom,
          height: mediaBounds.height,
          topGap: mediaBounds.top - recordingBounds.top,
          width: mediaBounds.width,
        },
      };
    });
  expect(geometry).not.toBeNull();
  expect(
    Math.abs(geometry.recording.width - geometry.host.width),
  ).toBeLessThanOrEqual(1);
  expect(
    Math.abs(geometry.recording.height - geometry.host.height),
  ).toBeLessThanOrEqual(1);
  expect(geometry.recording.left).toBeGreaterThanOrEqual(geometry.dock.left);
  expect(geometry.recording.right).toBeLessThanOrEqual(geometry.dock.right);
  expect(
    Math.abs(geometry.media.topGap - geometry.media.bottomGap),
  ).toBeLessThanOrEqual(1);
  expect(
    Math.abs(geometry.media.width / geometry.media.height - 16 / 9),
  ).toBeLessThanOrEqual(0.02);
  expect(
    await page
      .getByTestId("recording-controls")
      .evaluate(
        (element) =>
          element.closest('[data-bpp-test-id="workbench-footer"]') !== null,
      ),
  ).toBe(true);

  const resizeHandle = page.getByTestId("footer-replay-dock-resize");
  await expect(resizeHandle).toHaveAttribute("role", "separator");
  await expect(resizeHandle).toHaveAttribute(
    "aria-orientation",
    "horizontal",
  );
  const resizeHandleBounds = await resizeHandle.boundingBox();
  expect(resizeHandleBounds).not.toBeNull();
  expect(resizeHandleBounds.height).toBeGreaterThanOrEqual(10);
  const dockHeightBeforePointerResize = (
    await page.getByTestId("footer-replay-dock").boundingBox()
  ).height;
  await page.mouse.move(
    resizeHandleBounds.x + resizeHandleBounds.width / 2,
    resizeHandleBounds.y + resizeHandleBounds.height / 2,
  );
  await page.mouse.down();
  await page.mouse.move(
    resizeHandleBounds.x + resizeHandleBounds.width / 2,
    resizeHandleBounds.y - 28,
    { steps: 6 },
  );
  await page.mouse.up();
  await expect
    .poll(async () => (
      await page.getByTestId("footer-replay-dock").boundingBox()
    ).height)
    .toBeGreaterThanOrEqual(dockHeightBeforePointerResize + 24);
  const heightBeforeKeyboardResize = (
    await page.getByTestId("footer-replay-dock").boundingBox()
  ).height;
  await resizeHandle.focus();
  await resizeHandle.press("ArrowUp");
  const heightAfterKeyboardResize = (
    await page.getByTestId("footer-replay-dock").boundingBox()
  ).height;
  expect(heightAfterKeyboardResize).toBeGreaterThan(
    heightBeforeKeyboardResize,
  );

  await page.getByTestId("combat-log-dock-toggle").click();
  await expect(page.getByTestId("footer-replay-dock")).toBeHidden();
  expect(
    await page.evaluate(
      () =>
        document.querySelector(
          '[data-bpp-test-id="recording-video"]',
        ) === window.__bppRecordingVideo,
    ),
  ).toBe(true);
  expect(await video.evaluate((element) => element.playbackRate)).toBe(1.5);
  await page.getByTestId("combat-log-dock-toggle").click();

  await page.setViewportSize({ width: 480, height: 857 });
  await expect(page.getByTestId("combat-log-source").first()).toBeVisible();
  const narrowRoute = await page
    .getByTestId("combat-log-source")
    .first()
    .boundingBox();
  expect(narrowRoute.width).toBeGreaterThan(24);
  const narrowDimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  expect(narrowDimensions.scrollWidth).toBeLessThanOrEqual(
    narrowDimensions.clientWidth,
  );

  await page.setViewportSize({ width: 320, height: 420 });
  await expect
    .poll(async () => (
      await page.getByTestId("footer-replay-dock").boundingBox()
    ).height)
    .toBeLessThanOrEqual(144);
  await expect(
    page.getByTestId("combat-log").getByText("Combat log", { exact: true }),
  ).toBeVisible();
  await expect(page.getByTestId("combat-log-entry").first()).toBeVisible();
  expect(
    (await page.getByTestId("timeline-scroll").boundingBox()).height,
  ).toBeGreaterThanOrEqual(175);
  const shortViewportDimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  expect(shortViewportDimensions.scrollWidth).toBeLessThanOrEqual(
    shortViewportDimensions.clientWidth,
  );
});

test("renders the same behavioral contract in all supported locales", async ({
  page,
}) => {
  const expectations = [
    ["en", "Timeline", "Statistics"],
    ["zh-CN", "时间轴", "统计"],
    ["zh-Hant", "時間軸", "統計"],
  ];

  for (const [locale, timeline, statistics] of expectations) {
    await page.goto(`${reportUrl}?lang=${locale}`);
    await expect(page.getByTestId("report-tab-timeline")).toHaveText(timeline);
    await expect(page.getByTestId("report-tab-statistics")).toHaveText(statistics);
  }
});
