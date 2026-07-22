import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import crypto from "node:crypto";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const baseUrl = process.argv[2] || "http://127.0.0.1:8765/";
const read = (relativePath) => fs.readFileSync(path.join(root, relativePath), "utf8");
const localAssetPath = (relativePath) => relativePath.split("?", 1)[0];
const assert = (condition, message) => { if (!condition) throw new Error(message); };
const paeth = (left, up, upperLeft) => {
  const estimate = left + up - upperLeft;
  const leftDistance = Math.abs(estimate - left);
  const upDistance = Math.abs(estimate - up);
  const upperLeftDistance = Math.abs(estimate - upperLeft);
  if (leftDistance <= upDistance && leftDistance <= upperLeftDistance) return left;
  return upDistance <= upperLeftDistance ? up : upperLeft;
};
const readRgbaPng = (relativePath) => {
  const buffer = fs.readFileSync(path.join(root, localAssetPath(relativePath)));
  assert(buffer.subarray(0, 8).equals(Buffer.from([137, 80, 78, 71, 13, 10, 26, 10])), `invalid PNG signature: ${relativePath}`);
  const width = buffer.readUInt32BE(16);
  const height = buffer.readUInt32BE(20);
  const bitDepth = buffer[24];
  const colorType = buffer[25];
  assert(bitDepth === 8 && colorType === 6, `native preview must use 8-bit RGBA: ${relativePath}`);

  const idat = [];
  for (let offset = 8; offset < buffer.length;) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.subarray(offset + 4, offset + 8).toString("ascii");
    if (type === "IDAT") idat.push(buffer.subarray(offset + 8, offset + 8 + length));
    offset += length + 12;
  }
  const filtered = zlib.inflateSync(Buffer.concat(idat));
  const bytesPerPixel = 4;
  const stride = width * bytesPerPixel;
  assert(filtered.length === height * (stride + 1), `unexpected PNG scanline length: ${relativePath}`);
  const pixels = Buffer.alloc(height * stride);
  let sourceOffset = 0;
  for (let y = 0; y < height; y += 1) {
    const filter = filtered[sourceOffset];
    sourceOffset += 1;
    const rowOffset = y * stride;
    for (let x = 0; x < stride; x += 1) {
      const left = x >= bytesPerPixel ? pixels[rowOffset + x - bytesPerPixel] : 0;
      const up = y > 0 ? pixels[rowOffset - stride + x] : 0;
      const upperLeft = y > 0 && x >= bytesPerPixel ? pixels[rowOffset - stride + x - bytesPerPixel] : 0;
      let predictor = 0;
      if (filter === 1) predictor = left;
      else if (filter === 2) predictor = up;
      else if (filter === 3) predictor = Math.floor((left + up) / 2);
      else if (filter === 4) predictor = paeth(left, up, upperLeft);
      else assert(filter === 0, `unsupported PNG filter ${filter}: ${relativePath}`);
      pixels[rowOffset + x] = (filtered[sourceOffset + x] + predictor) & 255;
    }
    sourceOffset += stride;
  }
  return { width, height, pixels };
};
const nativePreviewMetrics = (relativePath) => {
  const png = readRgbaPng(relativePath);
  let foregroundPixels = 0;
  let opaquePixels = 0;
  const background = [8, 15, 25];
  for (let offset = 0; offset < png.pixels.length; offset += 4) {
    if (png.pixels[offset + 3] === 255) opaquePixels += 1;
    if (Math.max(
      Math.abs(png.pixels[offset] - background[0]),
      Math.abs(png.pixels[offset + 1] - background[1]),
      Math.abs(png.pixels[offset + 2] - background[2]),
    ) > 18) foregroundPixels += 1;
  }
  const pixelCount = png.width * png.height;
  return {
    width: png.width,
    height: png.height,
    aspect: png.width / png.height,
    opaqueRatio: opaquePixels / pixelCount,
    foregroundRatio: foregroundPixels / pixelCount,
  };
};
const EXPECTED_RAW_EVENTS = 2527;
const EXPECTED_PROJECTED_GROUPS = 296;

const context = { window: {} };
vm.createContext(context);
vm.runInContext(read("i18n.js"), context);
vm.runInContext(read("battle-meta.js"), context);
vm.runInContext(read("native-assets.js"), context);
vm.runInContext(read("battle-video.js"), context);
const i18n = context.window.BPP_I18N;
const meta = context.window.BATTLE_META;
const assets = context.window.BATTLE_ASSETS;
const video = context.window.BATTLE_VIDEO;
const battle = JSON.parse(read("latest-battle.timeline.json"));
const appSource = read("app.js");
const styleSource = read("styles.css");
const indexHtml = read("index.html");
const reportRevision = indexHtml.match(/<meta name="bpp-report-revision" content="([^"]+)"/i)?.[1];
const entryRevisions = [...indexHtml.matchAll(/(?:src|href)="[^"]+\?v=([^"&]+)"/g)].map((match) => match[1]);

assert(Boolean(reportRevision), "report entry point must declare its asset revision");
assert(entryRevisions.length >= 6 && entryRevisions.every((revision) => revision === reportRevision), "every entry-point stylesheet and script must share one asset revision");
assert(read("styles.css").includes(`--bpp-report-style-revision: "${reportRevision}"`), "stylesheet revision does not match the report entry point");
assert(read("app.js").includes(`const REPORT_ASSET_REVISION = "${reportRevision}"`), "application revision does not match the report entry point");
assert(read("app.js").includes("function ensureAssetContract") && read("app.js").includes("location.replace(url)"), "runtime must reject mixed DOM and stylesheet revisions");

assert(meta.battleId === battle.BattleId, "battle metadata does not match the fixture");
assert(assets.battleId === battle.BattleId, "native assets do not match the fixture");
assert(video.battleId === battle.BattleId, "video metadata does not match the fixture");
assert(i18n.defaultLocale === "zh-CN" && JSON.stringify(i18n.supportedLocales) === JSON.stringify(["zh-CN", "en-US"]), "supported locale contract changed unexpectedly");
const localeKeys = Object.fromEntries(i18n.supportedLocales.map((locale) => [locale, Object.keys(i18n.messages[locale]).sort()]));
assert(JSON.stringify(localeKeys["zh-CN"]) === JSON.stringify(localeKeys["en-US"]), "locale packs must expose identical message keys");
for (const key of localeKeys["zh-CN"]) {
  const variants = i18n.supportedLocales.map((locale) => i18n.messages[locale][key]);
  assert(variants.every((value) => typeof value === "string" || (value && typeof value === "object")), `invalid i18n value for ${key}`);
  assert(variants.every((value) => typeof value === typeof variants[0]), `i18n value shape differs for ${key}`);
  const placeholders = variants.map((value) => [...JSON.stringify(value).matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort());
  assert(placeholders.every((value) => JSON.stringify(value) === JSON.stringify(placeholders[0])), `i18n placeholders differ for ${key}`);
}
assert(battle.FrameCount === 171 && battle.DurationMs === 8500 && battle.Events.length === EXPECTED_RAW_EVENTS, "fixture frame, duration or raw-event count changed unexpectedly");
const appNode = { classList: { contains: () => false }, textContent: "", innerHTML: "" };
const appContext = {
  window: { BPP_I18N: i18n, BATTLE_META: meta, BATTLE_ASSETS: assets, BATTLE_VIDEO: video },
  document: {
    documentElement: { lang: "", style: { setProperty() {} } },
    body: { dataset: {} },
    title: "",
    querySelector(selector) {
      if (selector === 'meta[name="bpp-report-revision"]') return { content: "verify-mismatch" };
      if (selector === "#app") return appNode;
      return null;
    },
  },
  location: { search: "", href: "http://verify.invalid/", replace() {} },
  matchMedia: () => ({ matches: false }),
  getComputedStyle: () => ({ getPropertyValue: () => '"verify-mismatch"' }),
  sessionStorage: { getItem: () => "already-checked", setItem() {}, removeItem() {} },
  URL,
  URLSearchParams,
  Intl,
  console,
};
appContext.window.window = appContext.window;
appContext.__fixture = battle;
vm.createContext(appContext);
vm.runInContext(appSource, appContext);
const projectedGroupCount = vm.runInContext(`(() => {
  state.raw = __fixture;
  state.meta = window.BATTLE_META;
  state.assets = window.BATTLE_ASSETS;
  state.lanes = hydrateLanes(state.meta, state.assets);
  state.laneById = new Map(state.lanes.map((lane) => [lane.id, lane]));
  return projectEvents(__fixture.Events).length;
})()`, appContext);
assert(projectedGroupCount === EXPECTED_PROJECTED_GROUPS, `fixture projected-event count changed: ${projectedGroupCount}`);
assert(JSON.stringify(assets.heroPortraitSources) === JSON.stringify({
  Player: {
    hero: "Jules",
    bundle: "skin_jul_01_assets_all.bundle",
    sprite: "Skin_JUL_01a_PreviewCollection_TUI",
    sha256: "d8cadba27ab5af317aada8642df8d0c8bbf4d25ab60de785b6044dab10490441",
  },
  Opponent: {
    hero: "Mak",
    bundle: "skin_mak_01_assets_all.bundle",
    sprite: "Skin_MAK_01a_PreviewCollection_TUI",
    sha256: "52d9ebf7908de13fb412c7a3de8db9f23785bd65a10cd4922d887a7202c525ae",
  },
}), "combatant portraits must come from the same local default-skin asset class");
for (const side of ["Player", "Opponent"]) {
  const portrait = fs.readFileSync(path.join(root, localAssetPath(assets.heroes[side])));
  const digest = crypto.createHash("sha256").update(portrait).digest("hex");
  assert(digest === assets.heroPortraitSources[side].sha256, `${side} portrait pixels do not match the declared native Sprite`);
  assert(assets.heroes[side].includes(`v=${digest.slice(0, 8)}`), `${side} portrait URL must carry its content hash`);
}
const expectedEventIconKeys = ["Damage", "Health", "Regen", "Shield", "Burn", "Poison", "Charge", "Destroy"];
assert(JSON.stringify(Object.keys(assets.eventIcons)) === JSON.stringify(expectedEventIconKeys), "native combat-event icon set changed unexpectedly");
assert(assets.eventIconSource?.bundle === "fonts_assets_all.bundle"
  && JSON.stringify(assets.eventIconSource.sprites) === JSON.stringify(expectedEventIconKeys), "combat-event icons must retain their local game sprite provenance");
const playerStateUpdates = battle.Events.filter((event) => event.Kind === "player-attribute" && ["Health", "Rage", "HealthRegen", "Shield"].includes(event.Action));
const playerStateCounts = Object.fromEntries(["Health", "Rage", "HealthRegen", "Shield"].map((action) => [action, playerStateUpdates.filter((event) => event.Action === action).length]));
const stateApplyActions = new Set(["PlayerRageApply", "PlayerRegenApply", "PlayerShieldApply"]);
const stateApplyEvents = battle.Events.filter((event) => event.Kind === "effect-executed" && stateApplyActions.has(event.Action));
assert(JSON.stringify(playerStateCounts) === JSON.stringify({ Health: 18, Rage: 8, HealthRegen: 8, Shield: 8 }), "fixture player-state update counts changed unexpectedly");
assert(playerStateUpdates.every((event) => event.CurrentValue - event.PreviousValue === event.Amount), "player-state values must remain internally consistent");
const skillLaneIds = new Set(meta.lanes.filter((lane) => lane.type === "skill").map((lane) => lane.id));
const rageSkillTriggerGroups = new Map();
for (const event of stateApplyEvents.filter((candidate) => candidate.Action === "PlayerRageApply" && skillLaneIds.has(candidate.Source))) {
  const key = [event.AtMs, event.Source || "system", event.TriggerSource || "", event.Action].join("|");
  if (!rageSkillTriggerGroups.has(key)) rageSkillTriggerGroups.set(key, []);
  rageSkillTriggerGroups.get(key).push(event);
}
const targetsOverlap = (left, right) => (left || []).some((target) => (right || []).includes(target));
const rageSkillTriggerResults = [...rageSkillTriggerGroups.values()].map((effects) => {
  const frames = new Set(effects.map((event) => event.Frame));
  const targets = [...new Set(effects.flatMap((event) => event.Targets || []))];
  const related = battle.Events.filter((event) => frames.has(event.Frame) && targetsOverlap(targets, event.Targets));
  const competingEffects = related.filter((event) => event.Kind === "effect-executed" && event.Action === "PlayerRageApply");
  const updates = related.filter((event) => event.Kind === "player-attribute" && event.Action === "Rage");
  const effectIds = new Set(effects.map((event) => event.Id));
  const exact = competingEffects.length === effects.length && competingEffects.every((event) => effectIds.has(event.Id)) && updates.length === 1;
  return { exact, updates };
});
assert(rageSkillTriggerGroups.size === 56, "fixture skill-originated Rage execution count changed unexpectedly");
assert(rageSkillTriggerResults.filter((result) => result.exact).length === 4, "fixture uniquely attributable Rage update count changed unexpectedly");
assert(rageSkillTriggerResults.filter((result) => !result.updates.length).length === 44, "fixture missing Rage update count changed unexpectedly");
assert(new Set(rageSkillTriggerResults.filter((result) => result.exact).map((result) => result.updates[0].Amount)).size > 1, "fixture must exercise multiple exact Rage deltas");
const visibleEffectGroups = new Map();
for (const event of battle.Events.filter((candidate) => candidate.Kind === "effect-executed")) {
  const keepSkillRage = event.Action === "PlayerRageApply" && skillLaneIds.has(event.Source);
  if ((stateApplyActions.has(event.Action) && !keepSkillRage) || event.Action === "CardModifyAttribute") continue;
  const key = [event.AtMs, event.Source || "system", event.TriggerSource || "", event.Action].join("|");
  if (!visibleEffectGroups.has(key)) visibleEffectGroups.set(key, []);
  visibleEffectGroups.get(key).push(event);
}
const effectCollisionGroups = Map.groupBy([...visibleEffectGroups.values()], (events) => `${events[0].AtMs}|${events[0].Source || ""}`);
const effectCollisions = [...effectCollisionGroups.values()].filter((groups) => groups.length > 1);
assert(effectCollisions.length === 24 && Math.max(...effectCollisions.map((groups) => groups.length)) === 2, "fixture visible effect-collision profile changed unexpectedly");
assert([...visibleEffectGroups.values()].some((events) => events.length > 1), "fixture must retain merged same-semantic records");
const stateEventsByFrame = new Map();
for (const event of battle.Events) {
  if (!stateEventsByFrame.has(event.Frame)) stateEventsByFrame.set(event.Frame, []);
  stateEventsByFrame.get(event.Frame).push(event);
}
const eventsForStateTarget = (update) => (stateEventsByFrame.get(update.Frame) || [])
  .filter((event) => event.Id !== update.Id && (event.Targets || []).some((target) => (update.Targets || []).includes(target)));
const unsourcedBurnChange = playerStateUpdates.find((update) => {
  const related = eventsForStateTarget(update);
  const prefix = update.Action === "Health" ? "Health:Burn" : update.Action === "Shield" ? "Shield:Burn" : null;
  return prefix && related.some((event) => event.Kind === "health" && event.Action === prefix)
    && !related.some((event) => event.Kind === "effect-executed" && event.Action === "PlayerBurnApply");
});
const sourcedStateApplyChange = playerStateUpdates.find((update) => {
  const applyAction = { Rage: "PlayerRageApply", HealthRegen: "PlayerRegenApply", Shield: "PlayerShieldApply" }[update.Action];
  return update.Amount > 0 && applyAction && eventsForStateTarget(update)
    .some((event) => event.Kind === "effect-executed" && event.Action === applyAction && event.Source);
});
assert(unsourcedBurnChange, "fixture must exercise a persistent burn settlement without a same-frame applier");
assert(sourcedStateApplyChange, "fixture must exercise a positive state change with a recorded effect source");
const routineCardAttributes = new Set(["Cooldown", "Haste", "Slow", "Freeze"]);
const cardAttributeChanges = battle.Events.filter((event) => event.Kind === "card-attribute" && !routineCardAttributes.has(event.Action)
  && (stateEventsByFrame.get(event.Frame) || []).some((candidate) => candidate.Kind === "effect-executed"
    && candidate.Action === "CardModifyAttribute"
    && (candidate.Targets || []).includes(event.Source)));
assert(cardAttributeChanges.length === 83, "fixture card-attribute change count changed unexpectedly");
assert(new Set(cardAttributeChanges.map((event) => event.Action)).size === 8, "fixture must exercise several attributable card attributes");
assert(cardAttributeChanges.some((event) => event.Action === "BurnApplyAmount" && event.Amount > 0), "fixture must exercise a sourced card-attribute increase");
const cardAttributeGroups = new Map();
for (const event of cardAttributeChanges) {
  const key = `${event.Frame}|${event.Source}`;
  if (!cardAttributeGroups.has(key)) cardAttributeGroups.set(key, []);
  cardAttributeGroups.get(key).push(event);
}
assert(cardAttributeGroups.size === 76 && [...cardAttributeGroups.values()].some((changes) => changes.length > 1), "fixture must exercise grouped same-frame card-attribute changes");
const trackedStatuses = new Set(["Haste", "Slow", "Freeze"]);
const statusStarts = battle.Events.filter((event) => event.Kind === "card-attribute" && trackedStatuses.has(event.Action) && event.PreviousValue <= 0 && event.CurrentValue > 0);
const statusStartCauses = statusStarts.map((start) => battle.Events.filter((event) => event.Frame === start.Frame
  && event.Kind === "effect-executed"
  && event.Action === `Card${start.Action}`
  && (event.Targets || []).includes(start.Source)));
assert(statusStarts.length === 7, "fixture status-start count changed unexpectedly");
assert(statusStartCauses.every((causes) => causes.length > 0), "every status interval must resolve at least one recorded source");

const laneOwners = new Map(meta.lanes.map((lane) => [lane.id, lane.owner]));
const fixtureStats = {
  player: { damage: 0, healing: 0, shield: 0, tempo: { Charge: 0, Haste: 0, Slow: 0, Freeze: 0 } },
  opponent: { damage: 0, healing: 0, shield: 0, tempo: { Charge: 0, Haste: 0, Slow: 0, Freeze: 0 } },
};
const tempoActions = { CardCharge: "Charge", CardHaste: "Haste", CardSlow: "Slow", CardFreeze: "Freeze" };
for (const event of battle.Events) {
  if (event.Kind === "health") {
    const targetOwner = event.Targets?.[0] === "player:Player" ? "player" : event.Targets?.[0] === "player:Opponent" ? "opponent" : null;
    const amount = Number(event.Amount) || 0;
    const subtype = event.Action.split(":")[1] || "Other";
    if (!targetOwner || !amount) continue;
    if (amount < 0) fixtureStats[targetOwner === "player" ? "opponent" : "player"].damage += Math.abs(amount);
    else if (subtype === "Heal" || subtype === "Regen") fixtureStats[targetOwner].healing += amount;
    else if (subtype === "Shield") fixtureStats[targetOwner].shield += amount;
    continue;
  }
  const tempo = event.Kind === "effect-executed" ? tempoActions[event.Action] : null;
  const owner = tempo ? laneOwners.get(event.Source) : null;
  if (tempo && fixtureStats[owner]) fixtureStats[owner].tempo[tempo] += 1;
}
assert(JSON.stringify(fixtureStats) === JSON.stringify({
  player: { damage: 6567, healing: 0, shield: 0, tempo: { Charge: 91, Haste: 9, Slow: 0, Freeze: 0 } },
  opponent: { damage: 10352, healing: 470, shield: 0, tempo: { Charge: 10, Haste: 17, Slow: 0, Freeze: 0 } },
}), "fixture statistics projection changed unexpectedly");
const currentDamageProjection = vm.runInContext(`(() => {
  state.stats = buildBattleStatistics(__fixture.Events);
  return {
    types: activeDamageTypes(),
    player: state.stats.output.player.damageTypes,
    opponent: state.stats.output.opponent.damageTypes,
  };
})()`, appContext);
assert(JSON.stringify(currentDamageProjection.types) === JSON.stringify(["Burn"]), "current fixture must exercise the direct single-category damage summary");
assert(currentDamageProjection.player.Burn === 6567 && currentDamageProjection.opponent.Burn === 10352, "single-category damage summary values changed unexpectedly");
const multiDamageProjection = vm.runInContext(`(() => {
  state.stats = { output: {
    player: { damageTypes: { Damage: 300, Burn: 700, Poison: 0, Other: 0 } },
    opponent: { damageTypes: { Damage: 250, Burn: 250, Poison: 500, Other: 0 } },
  } };
  const option = buildDamageStatsOption();
  return {
    types: activeDamageTypes(),
    stacks: option.series.map((series) => series.stack),
    playerTotal: option.series.reduce((sum, series) => sum + series.data[0].value, 0),
    opponentTotal: option.series.reduce((sum, series) => sum + series.data[1].value, 0),
    rawValues: option.series.map((series) => series.data.map((datum) => datum.rawValue)),
  };
})()`, appContext);
assert(JSON.stringify(multiDamageProjection.types) === JSON.stringify(["Damage", "Burn", "Poison"]), "synthetic fixture must exercise three damage categories");
assert(multiDamageProjection.stacks.every((stack) => stack === "damage-total"), "multi-category damage series must share one stack");
assert(Math.abs(multiDamageProjection.playerTotal - 100) < 1e-9 && Math.abs(multiDamageProjection.opponentTotal - 100) < 1e-9, "each multi-category damage bar must total 100%");
assert(JSON.stringify(multiDamageProjection.rawValues) === JSON.stringify([[300, 250], [700, 250], [0, 500]]), "stacked percentages must retain original damage values for labels and tooltips");

const itemLanes = meta.lanes.filter((lane) => lane.type === "item");
assert(itemLanes.every((lane) => ["Small", "Medium", "Large"].includes(lane.size)), "every item lane must carry its snapshot size");
assert(new Set(itemLanes.map((lane) => lane.size)).size === 3, "fixture must exercise all three item footprints");

const sourceFiles = ["index.html", "styles.css", "app.js", "i18n.js", "battle-meta.js", "native-assets.js", "battle-video.js"];
for (const source of sourceFiles) assert(!/https?:\/\//i.test(read(source)), `${source} contains a remote runtime dependency`);
for (const source of ["index.html", "styles.css", "app.js"]) assert(!/[\u3400-\u9fff]/.test(read(source)), `${source} contains hard-coded Chinese UI copy outside the locale pack`);
assert(read("index.html").indexOf("i18n.js") < read("index.html").indexOf("app.js"), "the locale pack must load before the application");
assert(read("index.html").indexOf("battle-video.js") < read("index.html").indexOf("app.js"), "video metadata must load before the application");
assert(read("app.js").includes('new URLSearchParams(location.search).get("lang")') && read("app.js").includes('data-control="locale"'), "the report must support query-selected and in-UI locale changes");
assert(read("app.js").includes('history.replaceState({}, "", url)') && read("app.js").includes('document.documentElement.lang = state.locale'), "locale changes must update the URL and document language without reimporting data");
assert(read("app.js").includes("new Intl.PluralRules") && read("app.js").includes("new Intl.ListFormat") && read("app.js").includes("new Intl.NumberFormat(state.locale") && read("app.js").includes("new Intl.DateTimeFormat(state.locale"), "locale-sensitive plural, list, number and date formatting is incomplete");
assert(!/body[^{}]*\{[^}]*min-width\s*:/s.test(read("styles.css")), "body must not restore a fixed desktop minimum width");
assert(!read("app.js").includes("perf-chip") && !read("styles.css").includes("perf-chip"), "performance diagnostics must not appear in the report UI");
assert(!/PERF_DIAGNOSTICS|BPP_RENDER_PERF|runPanBenchmark/.test(read("app.js")), "obsolete performance benchmark code must be removed");
assert(video.src === "videos/latest-battle.mp4" && !/^[a-z]+:\/\//i.test(video.src), "fixture video must use a local relative URL");
assert(video.sync.mode === "estimated" && video.sync.provenance === "visual-review-of-legacy-recording", "legacy fixture must remain explicitly estimated");
assert(video.sync.anchors.length === 3, "legacy fixture calibration anchor count changed unexpectedly");
assert(video.sync.anchors.every((anchor, index, anchors) => index === 0
  || (anchor.combatMs > anchors[index - 1].combatMs && anchor.mediaMs > anchors[index - 1].mediaMs)), "fixture sync anchors must be monotonic in both clocks");
assert(appSource.includes("captureClockMs") && appSource.includes("captureFrameIndex")
  && appSource.includes("cfrSlotIndex") && appSource.includes("encoderFrameIndex")
  && appSource.includes("mediaPtsMs"), "recording-time sync metadata fields are not consumed by the viewer");
assert(appSource.includes("function resolveVideoSyncMode")
  && appSource.includes('requestedMode !== "captured" && requestedMode !== "exact"')
  && appSource.includes("anchor.combatFrame != null")
  && appSource.includes("Number.isFinite(anchor.mediaPtsMs)"), "recording gate must require captured/exact anchors with combat frame/time and final media PTS");
assert(appSource.includes("const battleMatches = Boolean(config && raw.BattleId === config.battleId)")
  && appSource.includes('recordingStatus === "valid" ? config.src : null'), "viewer must reject recordings whose battle identity or metadata contract does not match");
assert(appSource.includes('legacy: "video.rerecordLegacy"') && appSource.includes('class="video-recording-notice"'), "legacy estimated recordings must render the compact re-record notice");
for (const removedVideoImportPath of ["video-import-button", "video-import-file", "importVideoFile", "URL.createObjectURL", "objectUrl"]) {
  assert(!appSource.includes(removedVideoImportPath), `viewer still exposes the removed recording replacement path: ${removedVideoImportPath}`);
}
assert(appSource.includes('aria-expanded="${video.expanded}"') && appSource.includes("function setVideoExpanded"), "valid recordings must use one accessible collapsible full-width workbench");
assert(appSource.includes("function syncAnchorsFor") && appSource.includes("if (seekAnchors.at(-1)?.combatMs === anchor.combatMs) continue;"), "playback must preserve repeated combat anchors while combat-time seeking stays deterministic");
assert(appSource.includes("requestVideoFrameCallback") && appSource.includes("syncTimelineFromVideo(metadata.mediaTime * 1000)"), "video playback must drive the timeline from presented media frames");
assert(appSource.includes("videoSeekInFlight") && appSource.includes("pendingVideoSeek") && appSource.includes("videoPreviewIntervalMs"), "hover preview must coalesce seeks and adapt its throttle");
assert(appSource.includes("!combatVideo.paused") && appSource.includes('queueVideoSeek(state.video.previewCombatMs, "preview", false)'), "timeline hover must not seek while video is playing");
assert(appSource.includes("commitVideoAtCombatTime(combatMs)") && !appSource.includes("fastSeek("), "timeline click must commit through the ordered seek queue");
assert(appSource.includes("function renderVideoSyncAnchorMarkup") && appSource.includes("anchors[Math.floor(anchors.length / 2)]"), "large recorded anchor sets must not expand into unbounded DOM");
assert(read("serve.mjs").includes('"Accept-Ranges": "bytes"') && read("serve.mjs").includes("response.writeHead(206")
  && read("serve.mjs").includes('"Content-Range"'), "local prototype server must support MP4 byte ranges");
assert(read("app.js").includes("const DEFAULT_TIME_PIXELS_PER_SECOND = 80"), "fixed default time scale is missing");
assert(read("app.js").includes("const ITEM_SIZE_SPANS = { Small: 1, Medium: 2, Large: 3 }"), "item-size span mapping is missing");
assert(read("app.js").includes('data-item-size="${escapeHtml(lane.size || "")}"'), "item rows must expose their recorded size to layout and visual tests");
assert(read("app.js").includes("const TIER_COLORS = {") && read("app.js").includes('data-tier="${escapeHtml(lane.tier || "")}"'), "item and skill thumbnails must retain their native tier identity");
assert(read("app.js").includes('const STATE_APPLY_ACTIONS = new Set(["PlayerRageApply", "PlayerRegenApply", "PlayerShieldApply"])'), "redundant player-state Apply markers are not classified");
assert(read("app.js").includes('const SKILL_STATE_TRIGGER_METRICS = { PlayerRageApply: "Rage" }'), "skill-originated Rage execution is not classified");
assert(read("app.js").includes('if (STATE_APPLY_ACTIONS.has(raw.Action) && !shouldProjectStateApplyAsSkillTrigger(raw)) continue;'), "generic player-state Apply records must stay excluded while skill-originated Rage execution remains visible");
assert(read("app.js").includes("function resolveSkillStateTriggerResult") && read("app.js").includes('stateResult: stateUpdates.length ? "related" : "no-change-recorded"'), "skill Rage execution must distinguish exact, related and missing numeric updates");
assert(read("app.js").includes('if (STATE_APPLY_ACTIONS.has(event.action) && !event.stateMetric) return false;'), "visible-event filtering must preserve classified skill-state executions");
assert(read("app.js").includes("function projectPlayerStateMetrics"), "authoritative player-state projection is missing");
assert(read("app.js").includes("function projectCardAttributeChanges"), "generic CardModifyAttribute effects must project to their resulting value changes");
assert(read("app.js").includes('if (raw.Action === "CardModifyAttribute") continue;'), "generic CardModifyAttribute markers must be replaced instead of duplicated");
assert(read("app.js").includes('const ROUTINE_CARD_ATTRIBUTES = new Set(["Cooldown", "Haste", "Slow", "Freeze"])'), "routine timeline-driven card attributes must remain excluded");
assert(read("app.js").includes('sourceConfidence: modifiers.length === 1 ? "direct" : "related"'), "ambiguous card-attribute sources must be labelled as related rather than exact");
assert(read("app.js").includes('if (event.kind === "card-attribute")') && read("app.js").includes('add(target, event, "received")'), "card-attribute nodes must be placed on the affected entity lane");
assert(read("app.js").includes("function resolveStateChangeContext"), "state changes must resolve same-frame settlement context");
assert(read("app.js").includes('"Health:Damage": "PlayerDamage"') && read("app.js").includes('"Health:Regen": null'), "state settlement-to-effect matching must distinguish sourced effects from unsourced ticks");
assert(read("app.js").includes("sources: context.sources") && read("app.js").includes("causeKeys: context.causeKeys") && read("app.js").includes("sourceConfidence: context.sourceConfidence"), "projected state changes must retain sources, confidence and language-neutral fallback keys");
assert(read("app.js").includes('competingUpdates.length === 1 && effectCauses.length === 1 && settlements.length <= 1 && causeKeys.length === 0') && read("app.js").includes('? "direct"') && read("app.js").includes('? "related"') && read("app.js").includes(': "settlement"'), "state-change source confidence must reject same-frame ambiguity");
assert(read("app.js").includes('event.sourceConfidence === "direct"') && read("app.js").includes('t("tooltip.settlement")'), "state-change UI must distinguish direct sources, related sources and settlement evidence");
assert(!read("app.js").includes("causeLabels"), "projected state changes must not cache locale-specific labels");
assert(read("app.js").includes('const PLAYER_STATE_ORDER = ["Health", "Rage", "HealthRegen", "Shield"]'), "shared state chart must include health, rage, regeneration and shield");
assert(read("app.js").includes('type: "line"') && read("app.js").includes('step: "end"'), "player-state tracks must use a registered step-line series");
assert(read("app.js").includes("changes: [], points: [{ atMs: 0, value: 0 }"), "missing enemy/player state updates must project as a zero track");
assert(read("app.js").includes("function signedMagnitude") && read("app.js").includes("function signedMagnitudeInverse"), "magnitude scale must support zero and negative values");
assert(read("app.js").includes('return absolute < 1 ? numeric : Math.sign(numeric) * (1 + Math.log10(absolute))'), "magnitude ticks must align to powers of ten");
assert(read("app.js").includes('data-control="state-scale"'), "state chart must expose linear and magnitude scales");
assert(read("app.js").includes('id: "combat-event-hitareas"'), "event hover hit-area series is missing");
assert(appSource.includes("const hitSize = Math.max(24") && appSource.includes("symbolSize: [hitSize, hitSize]"), "event hover hit area must remain at least 24×24px at every lane scale");
assert(read("app.js").includes("function spreadCollidingPlacements") && read("app.js").includes('const key = `${placement.laneId}|${placement.atMs}`'), "same-lane same-time event collisions must be grouped for display");
assert(appSource.includes("layout.collisionStep") && (appSource.match(/symbolOffset: \[0, placement\.offsetY \|\| 0\]/g) || []).length >= 2, "colliding event glyphs and hit areas must share the scale-derived vertical fan-out");
assert(read("app.js").includes('formatter: `×${event.count}`'), "merged same-semantic event records must expose a visible count badge");
assert(read("app.js").includes("function formatStateChangeTooltip") && read("app.js").includes("renderStateChangeTooltipPath(event)"), "state-change hover must show its values and causal path");
assert(read("app.js").includes('event.kind === "card-attribute"') && read("app.js").includes("card-attribute-tooltip"), "card-attribute hover must expose its delta, before/after values and source path");
assert(read("app.js").includes("function renderCardAttributeTooltipChanges") && read("styles.css").includes(".attribute-change-row"), "grouped card-attribute changes must remain individually readable in one hover card");
assert(read("app.js").includes('isCardAttribute ? t("detail.attributeChange")') && read("app.js").includes("event.attributeChanges.map((change)"), "pinned card-attribute details must preserve every grouped value change");
assert(read("app.js").includes('BurnApplyAmount: "attribute.BurnApplyAmount"') && read("app.js").includes('ShieldApplyAmount: "attribute.ShieldApplyAmount"'), "common card attributes must use locale keys");
assert(i18n.messages["zh-CN"]["attribute.BurnApplyAmount"] === "燃烧施加量" && i18n.messages["en-US"]["attribute.BurnApplyAmount"] === "Burn Applied", "card-attribute locale labels changed unexpectedly");
assert(i18n.messages["en-US"]["description.attributeChange"].other === "{count} Attribute Changes", "grouped card-attribute titles must use grammatical English plurals");
assert(read("app.js").includes("formatter: (params) => formatSigned(params.data.amount)"), "hovered state points must expose their signed delta next to the point");
assert(read("app.js").includes('t("tooltip.beforeAfter")') && read("app.js").includes('t("tooltip.time")'), "state-change tooltip facts must use explicit localized before/after and time labels");
assert(read("styles.css").includes(".state-change-tooltip") && read("styles.css").includes(".state-overview-tooltip"), "state overview and state-change hover cards need dedicated layout styles");
assert(read("app.js").includes("function buildEventIconSeries") && read("app.js").includes("function eventNativeIcon"), "native combat-event icon rendering is missing");
assert(read("app.js").includes('const HEALTH_EVENT_ICON_KEYS = {') && read("app.js").includes('const EVENT_ACTION_ICON_KEYS = {'), "native combat-event icon mappings are missing");
assert(read("app.js").includes("window.BATTLE_ASSETS?.eventIcons"), "shared combat-event icons must remain available for imported battles without fixture-specific entity art");
assert(!/EVENT_ACTION_ICON_KEYS\s*=\s*\{[^}]*Card(?:Haste|Slow|Freeze)/s.test(read("app.js")), "status action markers must not duplicate sustained native status icons");
assert(read("app.js").includes("renderStateMetricGlyph(metric)"), "state overview must reuse native health, regeneration and shield icons");
assert(/trigger:\s*"item",\s*\n\s*renderMode:\s*"html"/.test(read("app.js")), "combat-event tooltip must use the structured HTML renderer");
assert(read("app.js").includes('{ label: t("tooltip.time"), value: formatTime(event.atMs)'), "combat-event timestamps must carry an explicit localized label");
assert(read("app.js").includes('event.kind === "skill-trigger" && event.stateMetric') && read("app.js").includes("skillStateResultLabel(event)"), "skill Rage hover and detail must expose the recorded numeric result");
assert(i18n.messages["zh-CN"]["common.noNumericChangeRecorded"] === "未记录数值变化" && i18n.messages["en-US"]["common.noNumericChangeRecorded"] === "No numeric change recorded", "missing-update wording must remain explicit and localized");
assert(i18n.messages["zh-CN"]["tooltip.settlement"] === "结算依据" && i18n.messages["en-US"]["tooltip.settlement"] === "Settlement Basis", "settlement-only state evidence must have explicit localized wording");
assert(!read("app.js").includes('`${role ? `${role} · ` : ""}${formatTime(event.atMs)}'), "combat-event role and timestamp must not share an ambiguous inline phrase");
for (const action of ["Health:Damage", "Shield:Damage", "Health:Burn", "Shield:Burn", "Health:Heal", "Health:Regen", "Shield:Shield"]) {
  assert(read("app.js").includes(`"${action}": { labelKey:`), `health-event presentation missing for ${action}`);
}
assert(read("app.js").includes("formatMetricValue(Math.abs(Number(event.amount) || 0))"), "damage/healing tooltip values must display magnitude rather than an unexplained signed delta");
assert(read("styles.css").includes(".combat-tooltip-facts") && read("styles.css").includes(".combat-tooltip-path"), "structured combat tooltip styles are missing");
assert(read("app.js").includes('t("tooltip.sameFrame")') && read("app.js").includes('t("tooltip.viewFrameEvents"'), "crowded-frame hover must expose a bounded frame drill-down action");
assert(read("app.js").includes("function applyEventFocus"), "event relationship highlighting is missing");
assert(read("app.js").includes('kind: "status-interval"'), "status ranges must project into interactive events");
assert(read("app.js").includes("event.sources || []"), "status relationship highlighting must include every recorded source");
assert(/function buildStatusSeries[\s\S]*?silent:\s*false/.test(read("app.js")), "status ranges must accept hover and click interaction");
assert(/function buildStatusSeries[\s\S]*?role:\s*"received"/.test(read("app.js")), "status ranges must identify the affected lane as received");
assert(i18n.messages["zh-CN"]["header.eyebrow"].includes("终局战况分析") && i18n.messages["en-US"]["header.eyebrow"].includes("Post-combat Analysis"), "localized report titles changed unexpectedly");
assert(!/<div class="summary-stat"><span>技能触发<\/span>/.test(read("app.js")), "skill-trigger count must not occupy a header summary card");
assert(read("app.js").includes('class="role-key"'), "applied / received / trigger legend is missing");
assert(/function eventRoleForLane[\s\S]*?return "received";[\s\S]*?return "trigger";[\s\S]*?return "applied";/.test(read("app.js")), "event placement must classify received, trigger and applied roles");
assert(read("app.js").includes('color: isReceived ? "#09101a" : isTrigger || !useImage ? markerColor : "rgba(9,16,26,.001)"'), "native event roles must use a solid icon for applied and a hollow carrier for received");
assert(read("app.js").includes('role: placement.role'), "event hover hit areas must retain the displayed role");
assert(read("styles.css").includes("content: attr(data-role-short)") && read("app.js").includes("ownerMark.dataset.roleShort"), "related lanes must expose localized compact role badges");
assert(read("app.js").includes("hoveredEvent?.atMs ??"), "event hover must keep the cursor snapped to the event timestamp");
assert(read("app.js").includes('chart.on("globalout"'), "event focus must clear when the pointer leaves an ECharts canvas");
assert(read("app.js").includes("event.clientY < rect.top"), "event focus must clear at the report-level pointer boundary");
assert((read("app.js").match(/type: "hideTip"/g) || []).length >= 3, "two-chart hover must explicitly clear stale tooltips");
assert(!read("app.js").includes("status ? state.assets?.status?.[status]"), "status action markers must not repeat the interval icon");
assert(appSource.includes("const LANE_SCALE_LEVELS = [")
  && [36, 44, 56, 66].every((rowHeight) => appSource.includes(`rowHeight: ${rowHeight}`)), "lane zoom must expose the four specified row-height levels");
assert(appSource.includes("function applyLaneLayoutVariables")
  && appSource.includes('root.style.setProperty("--row-height"')
  && appSource.includes('root.style.setProperty("--lane-art-size"')
  && appSource.includes('root.style.setProperty("--lane-art-column"'), "one JS lane layout spec must drive row and art geometry");
const rootStyleRule = styleSource.match(/^:root\s*\{([\s\S]*?)\}/)?.[1] || "";
assert(!/--(?:row-height|lane-art-size|lane-art-column|lane-name-size|lane-meta-size|owner-mark-height)\s*:/.test(rootStyleRule), "lane geometry defaults must not be duplicated in the CSS root rule");
assert(appSource.includes("function captureLaneScaleAnchor") && appSource.includes("function restoreLaneScaleAnchor"), "lane zoom must preserve a semantic center-lane anchor");
const laneScaleUpdater = appSource.slice(appSource.indexOf("function setLaneScaleIndex"), appSource.indexOf("function resetView"));
assert(laneScaleUpdater.includes("requestAnimationFrame(() => {") && laneScaleUpdater.includes("replaceMerge: [\"series\"]")
  && !laneScaleUpdater.includes("render()"), "lane zoom must batch one Y-layout update without full report rerender");
assert(appSource.includes('id="lane-zoom-out"') && appSource.includes('id="lane-zoom-reset"') && appSource.includes('id="lane-zoom-in"'), "sticky lane zoom controls are missing");
assert(styleSource.includes("grid-template-columns: var(--lane-art-column) minmax(0, 1fr) auto"), "lane art column must scale from the shared layout variables");
assert(styleSource.includes("width: calc(var(--item-preview-aspect, .542) * var(--lane-art-size))"), "item thumbnails must use the exported native card-preview aspect ratio");
for (const [size, aspect] of Object.entries({ Small: ".542", Medium: "1.058", Large: "2.084" })) {
  assert(styleSource.includes(`.lane-row.item[data-item-size="${size}"] .lane-art { --item-preview-aspect: ${aspect};`), `${size} item preview aspect is missing`);
}
assert(assets.cardPreviewMode === "native-final", "fixture must use exported native final card previews");
assert(appSource.includes('lane.type === "item" && state.assets?.cardPreviewMode !== "native-final"')
  && appSource.includes('class="native-preview-placeholder"'), "imports without native final card previews must fail closed with an explicit placeholder");
assert(!/\.lane-row\.item[^\{]*\.lane-art img\s*\{[^}]*transform:/s.test(styleSource), "raw card material textures must not be repaired with CSS scaling");
assert(/\.lane-art img\s*\{[^}]*object-fit:\s*contain/s.test(styleSource), "native entity images must preserve their exported aspect ratio");
assert(/\.lane-row\.skill \.lane-art\s*\{[^}]*width:\s*var\(--lane-art-size\);[^}]*height:\s*var\(--lane-art-size\);[^}]*border-color:\s*color-mix\(in srgb, var\(--art-accent\) 82%/s.test(styleSource), "skill thumbnails must keep a dedicated square native-icon viewport and native tier ring");
assert(!/\.lane-row\.(?:player|opponent) \.lane-art/.test(read("styles.css")), "entity ownership color must not masquerade as item or skill tier styling");
assert(read("app.js").includes("state.raw.DurationMs / 1000 * state.pixelsPerSecond"), "timeline width must equal duration × pixels per second");
assert(!read("app.js").includes("dataZoom"), "fixed-width timeline must not use adaptive ECharts dataZoom");
assert(!read("app.js").includes("time-navigator"), "fixed-width timeline must not keep the old adaptive navigator");
assert(/\.timeline-scroll\s*\{[^}]*overflow:\s*auto/s.test(read("styles.css")), "timeline must use native two-axis scrolling");
assert(read("app.js").includes('addEventListener("resize"'), "visible range must stay synchronized across viewport changes");
assert(read("styles.css").includes("#state-chart, #combat-chart"), "state and event charts must share the fixed timeline width");
assert(read("styles.css").includes(".relation-band.trigger"), "timeline relationship bands are missing");
assert(read("app.js").includes("function renderStatsPage") && read("app.js").includes("function mountStats"), "statistics page is missing");
assert(read("app.js").includes("function buildBattleStatistics"), "battle statistics projection is missing");
assert(appSource.includes('id="output-chart"') && appSource.includes('id="damage-chart"') && appSource.includes('id="tempo-chart"'), "statistics page must include output, conditional damage composition and application charts");
assert(appSource.includes('damageTypes.length > 1 ? "multi" : damageTypes.length === 1 ? "single" : "empty"')
  && appSource.includes('damageMode === "multi" ? `<article class="stats-chart-card damage-card"')
  && appSource.includes("renderDirectDamageSummary(damageTypes[0])"), "damage composition must degrade to an empty or directly-labelled single-category summary");
assert(!/type:\s*["']pie["']/.test(appSource), "single-category damage must not render a zero-information pie or donut");
assert(/function buildDamageStatsOption[\s\S]*?stack:\s*"damage-total"/.test(appSource), "multi-category damage must use comparable 100% stacked bars");
assert(appSource.includes("new ResizeObserver") && appSource.includes("statsCharts.forEach((chart) => chart.resize())"), "statistics charts must resize from their dashboard container");
assert(read("app.js").includes('formatTempoStatsTooltip, { magnitude: false }'), "tempo counts must stay on a linear comparison axis");
assert(i18n.messages["zh-CN"]["stats.tempoApplications"] === "充能 / 加速 / 减速 / 冰冻", "statistics KPI must name every counted application explicitly");
assert(i18n.messages["en-US"]["stats.tempoApplications"] === "Charge / Haste / Slow / Freeze", "English statistics KPI must name every counted application explicitly");
assert(!Object.entries(i18n.messages["zh-CN"]).some(([key, value]) => key.startsWith("stats.") && typeof value === "string" && value.includes("节奏")), "statistics copy must not expose the ambiguous tempo label");
assert(/\.stats-grid\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1\.35fr\)\s*minmax\(280px,\s*\.65fr\)/s.test(read("styles.css")), "desktop statistics layout must pair output and composition charts");
assert(/\.tempo-card\s*\{[^}]*grid-column:\s*1\s*\/\s*-1/s.test(read("styles.css")), "application chart must span the dashboard width");
assert(/@media\s*\(max-width:\s*760px\)[\s\S]*?\.stats-grid\s*\{[^}]*grid-template-columns:\s*1fr/s.test(read("styles.css")), "statistics charts must collapse to one column on narrow screens");
assert(styleSource.includes("height: clamp(184px, calc(var(--chart-rows, 3) * 52px + 72px), 360px)")
  && !/(?:height|min-height):\s*(?:190|210|242)px/.test(styleSource), "statistics charts must use row-driven bounded height instead of the old fixed heights");
assert(/@media\s*\(min-height:\s*1200px\)[\s\S]*?\.stats-page\s*\{[^}]*align-items:\s*center/s.test(styleSource), "high-viewport statistics dashboard must be vertically balanced");
for (const requiredSize of ["stats-heading span", "stats-kpi span", "stats-chart-card > header span", "side-legend span", "damage-legend"]) {
  const selector = requiredSize.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  assert(new RegExp(`${selector}[^\\{]*\\{[^}]*font-size:\\s*(?:11|1[2-9]|[2-9]\\d)px`, "s").test(styleSource), `statistics text below 11px: ${requiredSize}`);
}

const referencedAssets = [
  ...Object.values(assets.entities)
    .filter((entity) => entity.kind !== "item" || assets.cardPreviewMode === "native-final")
    .map((entity) => entity.asset)
    .filter(Boolean),
  ...Object.values(assets.status),
  ...Object.values(assets.eventIcons),
  ...Object.values(assets.heroes),
];
for (const lane of meta.lanes.filter((lane) => lane.type !== "hero")) {
  const entity = assets.entities[lane.id];
  assert(entity, `missing native entity asset metadata: ${lane.id}`);
  assert(entity.kind === lane.type, `native entity asset kind mismatch: ${lane.id}`);
  assert(entity.asset.includes(lane.type === "item" ? "/cards/" : "/skills/"), `native entity asset directory mismatch: ${lane.id}`);
}
for (const relativePath of referencedAssets) assert(fs.existsSync(path.join(root, localAssetPath(relativePath))), `missing local asset: ${relativePath}`);
const itemAspectRanges = {
  Small: [0.52, 0.56],
  Medium: [1.04, 1.08],
  Large: [2.06, 2.11],
};
const itemMetrics = [];
for (const lane of meta.lanes.filter((lane) => lane.type === "item")) {
  const entity = assets.entities[lane.id];
  assert(entity.asset.includes("/cards/previews/") && /\.preview\.png\?v=[a-z0-9]+$/i.test(entity.asset), `item must reference a cache-revised native final preview: ${lane.name}`);
  const metrics = nativePreviewMetrics(entity.asset);
  const [minimumAspect, maximumAspect] = itemAspectRanges[lane.size];
  assert(metrics.aspect >= minimumAspect && metrics.aspect <= maximumAspect, `${lane.name} ${lane.size} preview aspect ${metrics.aspect.toFixed(3)} is outside the native range`);
  assert(metrics.height >= 1200, `${lane.name} preview is below the native capture resolution`);
  assert(metrics.opaqueRatio === 1, `${lane.name} preview must be fully opaque over its local backplate`);
  assert(metrics.foregroundRatio >= 0.55, `${lane.name} preview foreground ratio ${metrics.foregroundRatio.toFixed(3)} is too sparse`);
  itemMetrics.push({ lane, metrics });
}
for (const regressionName of ["Black Pepper", "Dragon Steak", "Serving Platter", "Jumbo Wok"]) {
  assert(itemMetrics.some(({ lane }) => lane.name === regressionName), `missing native-preview regression sample: ${regressionName}`);
}
const maximumWidthBySize = Object.fromEntries(Object.keys(itemAspectRanges).map((size) => [size, Math.max(...itemMetrics.filter(({ lane }) => lane.size === size).map(({ metrics }) => metrics.width))]));
assert(maximumWidthBySize.Medium > maximumWidthBySize.Small * 1.8, "Medium item previews must be materially wider than Small previews");
assert(maximumWidthBySize.Large > maximumWidthBySize.Medium * 1.8, "Large item previews must be materially wider than Medium previews");
const heroPortraitBuffers = Object.values(assets.heroes).map((relativePath) => fs.readFileSync(path.join(root, localAssetPath(relativePath))));
for (const buffer of heroPortraitBuffers) {
  assert(buffer.subarray(1, 4).toString("ascii") === "PNG", "combatant portrait must be a PNG");
  assert(buffer.readUInt32BE(16) === 128 && buffer.readUInt32BE(20) === 128, "combatant portrait must use the shared 128×128 thumbnail pipeline");
}
assert(!heroPortraitBuffers[0].equals(heroPortraitBuffers[1]), "player and opponent portraits must remain distinct");
assert(Object.values(assets.heroes).every((relativePath) => /\?v=[a-z0-9]+$/i.test(relativePath)), "combatant portrait URLs must carry an explicit cache revision");
for (const relativePath of Object.values(assets.eventIcons)) {
  const buffer = fs.readFileSync(path.join(root, localAssetPath(relativePath)));
  assert(buffer.subarray(1, 4).toString("ascii") === "PNG", "combat-event icon must be a PNG");
  assert(buffer.readUInt32BE(16) === 64 && buffer.readUInt32BE(20) === 72, "combat-event icon must preserve the native 64×72 sprite crop");
}
assert(Object.values(assets.eventIcons).every((relativePath) => /\?v=[a-z0-9]+$/i.test(relativePath)), "combat-event icon URLs must carry an explicit cache revision");

const bundlePath = path.join(root, "vendor/echarts/echarts.min.js");
const echartsEntry = read("vendor/echarts/bpp-echarts-entry.js");
const usesLineSeries = /id:\s*lineId,\s*\n\s*type:\s*"line"/.test(read("app.js"));
assert(!usesLineSeries || echartsEntry.includes("LineChart"), "every ECharts series type must be registered in the local tree-shaken bundle");
assert(echartsEntry.includes("BarChart") && !echartsEntry.includes("PieChart"), "statistics bundle entry must register bars without retaining the removed pie series");
assert(/vendor\/echarts\/echarts\.min\.js\?v=/.test(read("index.html")), "rebuilt ECharts bundle must have an explicit cache revision");
assert(fs.existsSync(bundlePath), "local ECharts bundle is missing");
assert(fs.statSync(bundlePath).size <= 600 * 1024, "custom ECharts bundle exceeds 600 KiB");
assert(fs.existsSync(path.join(root, "vendor/echarts/LICENSE")), "ECharts license is missing");
assert(fs.existsSync(path.join(root, "vendor/echarts/NOTICE")), "ECharts notice is missing");

const entryReferences = [...indexHtml.matchAll(/(?:src|href)="([^"]+)"/g)].map((match) => match[1]);
const httpReferences = [...new Set(["index.html", ...entryReferences, "latest-battle.timeline.json", ...referencedAssets])];
for (const relativePath of httpReferences) {
  const response = await fetch(new URL(relativePath, baseUrl));
  assert(response.ok, `HTTP ${response.status} for ${relativePath}`);
  if (/\.(?:html|css|js|json|mjs)(?:\?|$)/i.test(relativePath)) {
    assert(response.headers.get("cache-control") === "no-store", `mutable report resource must not be cached: ${relativePath}`);
  }
  assert((await response.arrayBuffer()).byteLength > 0, `empty HTTP response for ${relativePath}`);
}
const videoPath = path.join(root, localAssetPath(video.src));
assert(fs.existsSync(videoPath), `missing local fixture video: ${video.src}`);
const rangeResponse = await fetch(new URL(video.src, baseUrl), { headers: { Range: "bytes=0-1023" } });
assert(rangeResponse.status === 206, `fixture video range request returned HTTP ${rangeResponse.status}`);
assert(rangeResponse.headers.get("accept-ranges") === "bytes", "fixture video response does not advertise byte ranges");
assert(/^bytes 0-1023\//.test(rangeResponse.headers.get("content-range") || ""), "fixture video range response has an invalid Content-Range");
assert((await rangeResponse.arrayBuffer()).byteLength === 1024, "fixture video range response returned the wrong byte count");

console.log(JSON.stringify({
  battleId: battle.BattleId,
  rawEvents: battle.Events.length,
  projectedGroups: projectedGroupCount,
  playerStateUpdates: playerStateCounts,
  stateApplyEvents: stateApplyEvents.length,
  rageSkillTriggers: {
    total: rageSkillTriggerGroups.size,
    exactDelta: rageSkillTriggerResults.filter((result) => result.exact).length,
    noNumericUpdate: rageSkillTriggerResults.filter((result) => !result.updates.length).length,
  },
  effectCollisions: { groups: effectCollisions.length, maxDepth: Math.max(...effectCollisions.map((groups) => groups.length)) },
  statusStarts: statusStarts.length,
  fixtureStats,
  lanes: meta.lanes.length,
  nativeAssets: referencedAssets.length,
  video: {
    durationMs: video.expectedDurationMs,
    fps: video.fps,
    syncMode: video.sync.mode,
    anchors: video.sync.anchors.length,
    rangeBytes: 1024,
  },
  echartsBundleKiB: Math.round(fs.statSync(bundlePath).size / 1024),
  localHttpResources: httpReferences.length,
  reportRevision,
}, null, 2));
