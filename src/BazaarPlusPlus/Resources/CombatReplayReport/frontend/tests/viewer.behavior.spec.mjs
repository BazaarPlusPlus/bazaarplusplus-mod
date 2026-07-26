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
let denseReportUrl;
let recordingReportUrl;
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
      value: -320,
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
      value: -180,
      unit: "points",
      role: "received",
      attributionConfidence: "exact",
    }),
  );
  chartEnvelope.battleDocument.rawRecordCount += 2;
  await writeFile(
    join(fixtureDirectory, "chart-report.html"),
    reportHtml(chartEnvelope),
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
  denseReportUrl = pathToFileURL(
    join(fixtureDirectory, "dense-report.html"),
  ).href;
  recordingReportUrl = pathToFileURL(
    join(fixtureDirectory, "recording-report.html"),
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
  await expect(page.getByTestId("timeline-sticky-hero-label")).toHaveAttribute(
    "data-bpp-sticky-hero-entity-id",
    "player-hero",
  );
  await expect.poll(stickyHeroPaintedPixels).toBeGreaterThan(0);
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
      '[data-bpp-test-id="timeline-canvas"]',
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
  await expect(page.getByTestId("timeline-sticky-hero-label")).toHaveAttribute(
    "data-bpp-sticky-hero-entity-id",
    "player-hero",
  );
  await expect.poll(stickyHeroPaintedPixels).toBeGreaterThan(0);
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
  await expect(page.getByTestId("timeline-sticky-hero-label")).toHaveAttribute(
    "data-bpp-sticky-hero-entity-id",
    "player-hero",
  );
});

test("pins one aligned hero lane and replaces it at the opponent section", async ({
  page,
}) => {
  await page.setViewportSize({ width: 999, height: 857 });
  await page.goto(`${scrollRecordingReportUrl}?lang=en`);

  const stickyLabel = page.getByTestId("timeline-sticky-hero-label");
  const stickyCanvas = page.getByTestId("timeline-sticky-hero-events");
  await expect(stickyLabel).toBeVisible();
  await expect(stickyCanvas).toBeVisible();
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

  const opponentHero = page.locator(
    '[data-bpp-entity-id="opponent-hero"]',
  );
  const opponentLane = Number(
    await opponentHero.getAttribute("data-bpp-lane-index"),
  );
  expect(opponentLane).toBeGreaterThan(0);
  await page.getByTestId("timeline-scroll").evaluate((scroll, lane) => {
    scroll.scrollTop = lane * 52 + 1;
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

  await page.getByTestId("timeline-scroll").evaluate((scroll) => {
    scroll.scrollTop = 0;
  });
  await expect(stickyLabel).toHaveAttribute(
    "data-bpp-sticky-hero-entity-id",
    "player-hero",
  );
  await expect(stickyLabel).toContainText("Fixture Player");
});

test("renders both combatants on the shared state band at device scale", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);

  await expect(page.getByTestId("state-label-health")).toContainText("1,000");
  await expect(page.getByTestId("state-label-health")).toContainText("1,200");
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
  await expect(page.getByTestId("recording-speed-popover")).toBeHidden();
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

  const guides = await ruler.evaluate((canvas, ratios) => {
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

  expect(guides.pinnedVisible).toBe(true);
  expect(guides.previewVisible).toBe(true);
  await expect(page.getByTestId("recording-video")).toHaveJSProperty(
    "paused",
    true,
  );
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
  await expect(page.getByTestId("focused-cluster-event")).toHaveAttribute(
    "data-bpp-event-id",
    "damage-1",
  );

  await page.keyboard.press("ArrowRight");
  await expect(currentTime).toContainText("3.00s");
  await expect(page.getByTestId("focused-cluster-event")).toHaveAttribute(
    "data-bpp-event-id",
    "charge-1",
  );

  await page.keyboard.press("ArrowLeft");
  await expect(currentTime).toContainText("2.00s");
  await expect(page.getByTestId("focused-cluster-event")).toHaveAttribute(
    "data-bpp-event-id",
    "damage-1",
  );
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
  await expect(tooltip).toContainText("Count");
  await expect(tooltip).toContainText("0");

  const quantifiedDamage = itemRow.getByTestId(
    "statistics-activity-value-damage",
  );
  await quantifiedDamage.focus();
  await expect(tooltip).toBeVisible();
  await expect(tooltip).toContainText("Damage");
  await expect(tooltip).toContainText("Total amount");
  await expect(tooltip).toContainText("120");
  await expect(tooltip).toContainText("Count");
  await expect(tooltip).toContainText("1");
});

test("activity rows expose button semantics and activate with Enter or Space", async ({
  page,
}) => {
  await page.goto(`${reportUrl}?lang=en`);
  await page.getByTestId("report-tab-statistics").click();

  const itemRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Training Blade" });
  await expect(itemRow).toHaveAttribute("role", "button");
  await expect(itemRow).toHaveAttribute(
    "aria-label",
    "Training Blade · Click to view the timeline lane",
  );
  await itemRow.focus();
  await itemRow.press("Enter");
  await expect(page.getByTestId("timeline-section")).toBeVisible();
  await expect(
    page.locator('[data-bpp-entity-id="player-item"]'),
  ).toHaveClass(/is-jump-target/u);

  await page.getByTestId("report-tab-statistics").click();
  const skillRow = page
    .locator("[data-bpp-test-id^='statistics-activity-row-']")
    .filter({ hasText: "Quick Thinking" });
  await skillRow.focus();
  await skillRow.press(" ");
  await expect(page.getByTestId("timeline-section")).toBeVisible();
  await expect(
    page.locator('[data-bpp-entity-id="player-skill"]'),
  ).toHaveClass(/is-jump-target/u);
});

test("keeps pointer hover imperative without React commits", async ({ page }) => {
  await page.goto(`${reportUrl}?lang=en`);
  const canvas = page.getByTestId("timeline-canvas");
  const bounds = await canvas.boundingBox();
  expect(bounds).not.toBeNull();
  const before = await page.evaluate(() => ({
    commits: window.__BPP_VIEWER_TEST__?.commitCount ?? -1,
    hoverDraws: window.__BPP_VIEWER_TEST__?.hoverDrawCount ?? -1,
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
  }));
  expect(after.commits).toBe(before.commits);
  expect(after.hoverDraws).toBeGreaterThan(before.hoverDraws);
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
  expect(chartGeometry.widths.every((width) => width > 400)).toBe(true);
  expect(
    Math.abs(chartGeometry.yPositions[0] - chartGeometry.yPositions[1]),
  ).toBeLessThan(0.5);
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
    .toEqual({ dispose: 0, init: 2 });

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
    .toEqual({ dispose: 0, init: 2 });

  await page.getByTestId("report-tab-timeline").click();
  await expect
    .poll(() =>
      page.evaluate(() => ({
        dispose: window.__BPP_VIEWER_TEST__?.echartsDisposeCount ?? -1,
        init: window.__BPP_VIEWER_TEST__?.echartsInitCount ?? -1,
      }))
    )
    .toEqual({ dispose: 2, init: 2 });
});

test("scopes the inspector to the exact clicked cluster", async ({
  page,
}) => {
  await page.goto(`${denseReportUrl}?lang=en`);

  const canvas = page.getByTestId("timeline-canvas");
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
    "damage",
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
  await expect(page.getByTestId("event-target-entity").first()).toContainText(
    "Fixture Opponent",
  );
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
    const timelineScroll = document.querySelector(
      '[data-bpp-test-id="timeline-scroll"]',
    );
    const inspectorBounds = inspector?.getBoundingClientRect();
    const headerBounds = inspectorHeader?.getBoundingClientRect();
    const timelineBounds = timelineScroll?.getBoundingClientRect();
    return {
      headerHeight: headerBounds?.height ?? 0,
      inspectorLeft: inspectorBounds?.left ?? 0,
      inspectorWidth: inspectorBounds?.width ?? 0,
      timelineRight: timelineBounds?.right ?? 0,
    };
  });
  expect(inspectorGeometry.inspectorWidth).toBeGreaterThanOrEqual(320);
  expect(inspectorGeometry.inspectorWidth).toBeLessThanOrEqual(400);
  expect(inspectorGeometry.headerHeight).toBeLessThanOrEqual(64);
  expect(
    Math.abs(
      inspectorGeometry.timelineRight - inspectorGeometry.inspectorLeft,
    ),
  ).toBeLessThanOrEqual(1);
  await page.mouse.move(2, 2);
  await expect(page.getByTestId("timeline-lane-1")).toHaveClass(
    /is-related-source/u,
  );

  await clickCluster(3.5 * 52 + 9, "1 events");
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
  await expect(page.getByTestId("frame-event-total")).toHaveText("1 events");
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
  await expect(page.getByTestId("timeline-lane-4")).not.toHaveClass(
    /is-related-source/u,
  );
});

test("keeps the page chrome bounded while the timeline owns horizontal overflow", async ({
  page,
}) => {
  await page.setViewportSize({ width: 480, height: 857 });
  await page.goto(`${reportUrl}?lang=zh-CN`);

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

  const ruler = page.getByTestId("timeline-ruler-canvas");
  const rulerBounds = await ruler.boundingBox();
  expect(rulerBounds).not.toBeNull();
  await ruler.evaluate((element) => {
    const bounds = element.getBoundingClientRect();
    element.dispatchEvent(new PointerEvent("pointermove", {
      bubbles: true,
      clientX: bounds.left + bounds.width * 0.05,
      clientY: bounds.top + bounds.height * 0.5,
    }));
  });
  await expect(log).toHaveAttribute("data-bpp-follow-source", "paused");
  await expect(activeEntry).not.toHaveAttribute("data-index", selectedIndex);
  await ruler.dispatchEvent("pointerout");
  await expect(activeEntry).toHaveAttribute("data-index", selectedIndex);

  const viewport = page.getByTestId("combat-log-viewport");
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
  await page.getByTestId("combat-log-resume").click();
  await expect(page.getByTestId("combat-log-resume")).toBeHidden();
  await rows.first().focus();
  await expect(page.getByTestId("combat-log-resume")).toBeVisible();
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
  await expect(page.getByTestId("combat-log-route").first()).toBeVisible();
  const narrowRoute = await page
    .getByTestId("combat-log-route")
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
