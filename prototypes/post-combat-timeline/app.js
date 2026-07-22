const REPORT_ASSET_REVISION = "20260722b";
const DEFAULT_TIME_PIXELS_PER_SECOND = 80;
const MIN_TIME_PIXELS_PER_SECOND = 40;
const MAX_TIME_PIXELS_PER_SECOND = 240;
const TIME_TICK_STEPS_MS = [250, 500, 1000, 2000, 5000, 10000, 30000, 60000];
const STATE_CHART_HEIGHT = 156;
const DEFAULT_LANE_SCALE_INDEX = 1;
const LANE_SCALE_LEVELS = [
  { percent: 80, rowHeight: 36, artSize: 22, compactArtSize: 20, nameSize: 8, metaSize: 6.5, markerScale: .82, collisionStep: 12 },
  { percent: 100, rowHeight: 44, artSize: 28, compactArtSize: 24, nameSize: 9, metaSize: 7, markerScale: 1, collisionStep: 14 },
  { percent: 125, rowHeight: 56, artSize: 36, compactArtSize: 28, nameSize: 10, metaSize: 8, markerScale: 1.2, collisionStep: 18 },
  { percent: 150, rowHeight: 66, artSize: 44, compactArtSize: 32, nameSize: 11, metaSize: 9, markerScale: 1.38, collisionStep: 22 },
];
const VIDEO_PREVIEW_BASE_INTERVAL_MS = 80;
const VIDEO_PREVIEW_SLOW_INTERVAL_MS = 150;
const VIDEO_SEEK_EPSILON_MS = 24;

const ACTIONS = {
  PlayerBurnApply: ["action.PlayerBurnApply", "burn"],
  PlayerPoisonApply: ["action.PlayerPoisonApply", "status"],
  PlayerDamage: ["action.PlayerDamage", "damage"],
  PlayerHeal: ["action.PlayerHeal", "heal"],
  PlayerShieldApply: ["action.PlayerShieldApply", "charge"],
  PlayerRageApply: ["action.PlayerRageApply", "buff"],
  PlayerRegenApply: ["action.PlayerRegenApply", "heal"],
  CardCharge: ["action.CardCharge", "charge"],
  CardHaste: ["action.CardHaste", "status"],
  CardFreeze: ["action.CardFreeze", "status"],
  CardSlow: ["action.CardSlow", "status"],
  CardDestroy: ["action.CardDestroy", "damage"],
  CardForceUse: ["action.CardForceUse", "charge"],
  CardModifyAttribute: ["action.CardModifyAttribute", "system"],
  FlyingStart: ["action.FlyingStart", "buff"],
  Died: ["action.Died", "damage"],
};

const COLORS = {
  burn: "#ff965a",
  damage: "#ff627a",
  heal: "#62d89a",
  status: "#9a8cff",
  charge: "#68baff",
  buff: "#e9c46a",
  skill: "#ce82ff",
  system: "#7f8da2",
};
const OWNER_COLORS = { player: "#62d8e8", opponent: "#ff7588" };
const TIER_COLORS = {
  Bronze: "#b7794f",
  Silver: "#9fb3c8",
  Gold: "#e9c46a",
  Diamond: "#69d6e5",
  Legendary: "#ba86ef",
};

const STATUS_STYLES = {
  Haste: { fill: "rgba(99,216,239,.15)", hover: "rgba(99,216,239,.28)", line: "rgba(99,216,239,.78)" },
  Slow: { fill: "rgba(228,175,85,.16)", hover: "rgba(228,175,85,.29)", line: "rgba(228,175,85,.82)" },
  Freeze: { fill: "rgba(138,174,255,.18)", hover: "rgba(138,174,255,.31)", line: "rgba(138,174,255,.84)" },
};

const ITEM_SIZE_SPANS = { Small: 1, Medium: 2, Large: 3 };
const STATE_APPLY_ACTIONS = new Set(["PlayerRageApply", "PlayerRegenApply", "PlayerShieldApply"]);
const SKILL_STATE_TRIGGER_METRICS = { PlayerRageApply: "Rage" };
const PLAYER_STATE_METRICS = {
  Health: { labelKey: "metric.Health", glyph: "♥", iconKey: "Health", family: "damage", applyAction: null },
  Rage: { labelKey: "metric.Rage", glyph: "◆", family: "buff", applyAction: "PlayerRageApply" },
  HealthRegen: { labelKey: "metric.HealthRegen", glyph: "+", iconKey: "Regen", family: "heal", applyAction: "PlayerRegenApply" },
  Shield: { labelKey: "metric.Shield", glyph: "◇", iconKey: "Shield", family: "charge", applyAction: "PlayerShieldApply" },
};
const PLAYER_STATE_ORDER = ["Health", "Rage", "HealthRegen", "Shield"];
const HEALTH_EVENT_ICON_KEYS = {
  "Health:Damage": "Damage",
  "Shield:Damage": "Shield",
  "Health:Burn": "Burn",
  "Shield:Burn": "Burn",
  "Health:Heal": "Health",
  "Health:Regen": "Regen",
  "Shield:Shield": "Shield",
};
const EVENT_ACTION_ICON_KEYS = {
  PlayerDamage: "Damage",
  PlayerHeal: "Health",
  PlayerBurnApply: "Burn",
  PlayerPoisonApply: "Poison",
  CardCharge: "Charge",
  CardDestroy: "Destroy",
  Died: "Destroy",
};
const DAMAGE_TYPE_KEYS = ["Damage", "Burn", "Poison", "Other"];
const DAMAGE_TYPE_COLORS = { Damage: COLORS.damage, Burn: COLORS.burn, Poison: "#8f7bea", Other: COLORS.system };
const TEMPO_ACTIONS = { CardCharge: "Charge", CardHaste: "Haste", CardSlow: "Slow", CardFreeze: "Freeze" };
const TEMPO_KEYS = ["Charge", "Haste", "Slow", "Freeze"];
const HEALTH_EVENT_PRESENTATIONS = {
  "Health:Damage": { labelKey: "health.Health:Damage.label", valueLabelKey: "health.Health:Damage.value", family: "damage" },
  "Shield:Damage": { labelKey: "health.Shield:Damage.label", valueLabelKey: "health.Shield:Damage.value", family: "damage" },
  "Health:Burn": { labelKey: "health.Health:Burn.label", valueLabelKey: "health.Health:Burn.value", family: "burn" },
  "Shield:Burn": { labelKey: "health.Shield:Burn.label", valueLabelKey: "health.Shield:Burn.value", family: "burn" },
  "Health:Heal": { labelKey: "health.Health:Heal.label", valueLabelKey: "health.Health:Heal.value", family: "heal" },
  "Health:Regen": { labelKey: "health.Health:Regen.label", valueLabelKey: "health.Health:Regen.value", family: "heal" },
  "Shield:Shield": { labelKey: "health.Shield:Shield.label", valueLabelKey: "health.Shield:Shield.value", family: "charge" },
};
const STATE_SETTLEMENT_EFFECT_ACTIONS = {
  "Health:Damage": "PlayerDamage",
  "Shield:Damage": "PlayerDamage",
  "Health:Burn": "PlayerBurnApply",
  "Shield:Burn": "PlayerBurnApply",
  "Health:Heal": "PlayerHeal",
  "Health:Regen": null,
  "Shield:Shield": "PlayerShieldApply",
};
const STATE_SETTLEMENT_FALLBACK_KEYS = {
  "Health:Damage": "cause.Health:Damage",
  "Shield:Damage": "cause.Shield:Damage",
  "Health:Burn": "cause.Health:Burn",
  "Shield:Burn": "cause.Shield:Burn",
  "Health:Heal": "cause.Health:Heal",
  "Health:Regen": "cause.Health:Regen",
  "Shield:Shield": "cause.Shield:Shield",
};
const ROUTINE_CARD_ATTRIBUTES = new Set(["Cooldown", "Haste", "Slow", "Freeze"]);
const CARD_ATTRIBUTE_LABEL_KEYS = {
  Ammo: "attribute.Ammo",
  BurnApplyAmount: "attribute.BurnApplyAmount",
  DamageAmount: "attribute.DamageAmount",
  HealAmount: "attribute.HealAmount",
  PoisonApplyAmount: "attribute.PoisonApplyAmount",
  RegenApplyAmount: "attribute.RegenApplyAmount",
  ShieldApplyAmount: "attribute.ShieldApplyAmount",
};

const I18N = window.BPP_I18N;

function initialLocale() {
  const requested = new URLSearchParams(location.search).get("lang");
  if (!requested) return I18N.defaultLocale;
  const exact = I18N.supportedLocales.find((locale) => locale.toLowerCase() === requested.toLowerCase());
  if (exact) return exact;
  return requested.toLowerCase().startsWith("en") ? "en-US" : requested.toLowerCase().startsWith("zh") ? "zh-CN" : I18N.defaultLocale;
}

function interpolateMessage(template, variables) {
  return String(template).replace(/\{(\w+)\}/g, (_, key) => variables[key] ?? `{${key}}`);
}

function t(key, variables = {}, fallback = key) {
  const messages = I18N.messages[state?.locale] || I18N.messages[I18N.defaultLocale];
  const defaultMessages = I18N.messages[I18N.defaultLocale];
  const message = messages?.[key] ?? defaultMessages?.[key] ?? fallback;
  return typeof message === "string" ? interpolateMessage(message, variables) : fallback;
}

function tp(key, count, variables = {}) {
  const messages = I18N.messages[state?.locale] || I18N.messages[I18N.defaultLocale];
  const defaultMessages = I18N.messages[I18N.defaultLocale];
  const message = messages?.[key] ?? defaultMessages?.[key];
  const rule = new Intl.PluralRules(state?.locale || I18N.defaultLocale).select(Number(count) || 0);
  const template = typeof message === "object" ? message[rule] ?? message.other : message;
  return interpolateMessage(template ?? key, { count: formatMetricValue(count), ...variables });
}

function formatList(values) {
  const filtered = [...new Set(values.filter(Boolean))];
  return filtered.length ? new Intl.ListFormat(state.locale, { style: "short", type: "conjunction" }).format(filtered) : "";
}

const state = {
  locale: initialLocale(),
  raw: null,
  meta: window.BATTLE_META,
  assets: window.BATTLE_ASSETS,
  lanes: [],
  laneById: new Map(),
  events: [],
  stateMetrics: [],
  stateChanges: [],
  eventById: new Map(),
  intervals: [],
  frames: [],
  frameByNumber: new Map(),
  stats: null,
  scope: "all",
  family: "all",
  view: "timeline",
  stateScale: "magnitude",
  selected: null,
  selectedFrame: null,
  selectedRole: null,
  sourceName: "local",
  pixelsPerSecond: DEFAULT_TIME_PIXELS_PER_SECOND,
  laneScaleIndex: DEFAULT_LANE_SCALE_INDEX,
  viewStartMs: 0,
  viewEndMs: 0,
  video: null,
};

function laneLayout() {
  return LANE_SCALE_LEVELS[state.laneScaleIndex] || LANE_SCALE_LEVELS[DEFAULT_LANE_SCALE_INDEX];
}

function laneArtSize(layout = laneLayout()) {
  return matchMedia("(max-width: 680px)").matches ? layout.compactArtSize : layout.artSize;
}

function applyLaneLayoutVariables(layout = laneLayout()) {
  const artSize = laneArtSize(layout);
  const root = document.documentElement;
  root.style.setProperty("--row-height", `${layout.rowHeight}px`);
  root.style.setProperty("--lane-art-size", `${artSize}px`);
  root.style.setProperty("--lane-art-column", `${artSize * 3}px`);
  root.style.setProperty("--lane-name-size", `${layout.nameSize}px`);
  root.style.setProperty("--lane-meta-size", `${layout.metaSize}px`);
  root.style.setProperty("--owner-mark-height", `${Math.max(16, Math.round(layout.rowHeight * .42))}px`);
}

function applyLocaleDocument() {
  document.documentElement.lang = state.locale;
  document.title = t("app.title");
  document.body.dataset.dropLabel = t("app.dropToImport");
  const app = document.querySelector("#app");
  if (app?.classList.contains("app-loading")) app.textContent = t("app.loading");
}

function setLocale(locale) {
  const normalized = I18N.supportedLocales.find((candidate) => candidate.toLowerCase() === String(locale).toLowerCase());
  if (!normalized || normalized === state.locale) return;
  state.locale = normalized;
  const url = new URL(location.href);
  url.searchParams.set("lang", normalized);
  history.replaceState({}, "", url);
  if (state.raw) refreshLocalizedLanes();
  render();
}

function refreshLocalizedLanes() {
  if (state.raw.BattleId !== window.BATTLE_META?.battleId) state.meta = fallbackMeta(state.raw);
  const usedIds = new Set([...state.events, ...state.stateChanges, ...state.intervals].flatMap((event) => [
    event.source,
    event.triggerSource,
    ...(event.sources || []),
    ...(event.triggerSources || []),
    ...(event.targets || []),
  ].filter(Boolean)));
  state.lanes = hydrateLanes(state.meta, state.assets).filter((lane) => usedIds.has(lane.id));
  state.laneById = new Map(state.lanes.map((lane) => [lane.id, lane]));
}

function tierLabel(tier) {
  return tier ? t(`tier.${tier}`, {}, tier) : null;
}

function metricLabel(metric) {
  const definition = typeof metric === "string" ? PLAYER_STATE_METRICS[metric] : metric;
  const fallback = typeof metric === "string" ? metric : metric?.metric || "";
  return definition?.labelKey ? t(definition.labelKey, {}, fallback) : fallback;
}

let combatChart = null;
let stateChart = null;
let statsCharts = [];
let statsResizeObserver = null;
let focusClearTimer = null;
let hoveredEventId = null;
let combatVideo = null;
let videoFrameRequestId = null;
let videoFallbackFrameId = null;
let videoPreviewTimer = null;
let videoSeekInFlight = false;
let pendingVideoSeek = null;
let videoSeekStartedAt = 0;
let lastVideoUiUpdateAt = 0;
let lastStateReadoutAtMs = Number.NEGATIVE_INFINITY;
let suppressTimelineStageClickUntil = 0;
let laneScaleFrameId = null;
let pendingLaneScaleAnchor = null;

applyLocaleDocument();

if (ensureAssetContract()) startReport();

function ensureAssetContract() {
  const documentRevision = document.querySelector('meta[name="bpp-report-revision"]')?.content || "";
  const styleRevision = getComputedStyle(document.documentElement)
    .getPropertyValue("--bpp-report-style-revision")
    .trim()
    .replace(/^["']|["']$/g, "");
  const reloadKey = `bpp-report-reload:${REPORT_ASSET_REVISION}`;
  if (documentRevision === REPORT_ASSET_REVISION && styleRevision === REPORT_ASSET_REVISION) {
    sessionStorage.removeItem(reloadKey);
    return true;
  }
  if (!sessionStorage.getItem(reloadKey)) {
    sessionStorage.setItem(reloadKey, "1");
    const url = new URL(location.href);
    url.searchParams.set("assetRev", REPORT_ASSET_REVISION);
    location.replace(url);
    return false;
  }
  const app = document.querySelector("#app");
  if (app) app.innerHTML = `<div class="load-error">${escapeHtml(t("app.assetMismatch"))}</div>`;
  return false;
}

function startReport() {
  fetch("latest-battle.timeline.json", { cache: "no-store" })
    .then((response) => {
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    })
    .then((raw) => loadBattle(raw, "local"))
    .catch((error) => {
      document.querySelector("#app").innerHTML = `<div class="load-error">${escapeHtml(t("app.loadFailed", { message: error.message }))}</div>`;
    });

  bindDropTarget();
  bindViewportRangeSync();
}

function loadBattle(raw, sourceName) {
  validateBattle(raw);
  snapshotVideoPlayback();
  teardownVideoRuntime();
  state.raw = raw;
  state.sourceName = sourceName;
  state.meta = raw.BattleId === window.BATTLE_META?.battleId ? window.BATTLE_META : fallbackMeta(raw);
  state.assets = raw.BattleId === window.BATTLE_ASSETS?.battleId ? window.BATTLE_ASSETS : null;
  state.lanes = hydrateLanes(state.meta, state.assets);
  state.laneById = new Map(state.lanes.map((lane) => [lane.id, lane]));
  state.events = projectEvents(raw.Events);
  state.stateMetrics = projectPlayerStateMetrics(raw.Events, raw.DurationMs);
  state.stateChanges = state.stateMetrics.flatMap((metric) => metric.tracks.flatMap((track) => track.changes));
  state.intervals = projectStatusIntervals(raw.Events, raw.DurationMs);
  state.stats = buildBattleStatistics(raw.Events);
  state.eventById = new Map([...state.events, ...state.stateChanges, ...state.intervals].map((event) => [event.id, event]));
  state.frames = buildFrameIndex(raw.Events, [...state.events, ...state.stateChanges, ...state.intervals]);
  state.frameByNumber = new Map(state.frames.map((frame) => [frame.frame, frame]));
  refreshLocalizedLanes();
  state.selected = null;
  state.selectedFrame = null;
  state.selectedRole = null;
  state.pixelsPerSecond = DEFAULT_TIME_PIXELS_PER_SECOND;
  state.laneScaleIndex = DEFAULT_LANE_SCALE_INDEX;
  state.viewStartMs = 0;
  state.viewEndMs = 0;
  lastStateReadoutAtMs = Number.NEGATIVE_INFINITY;
  state.video = createVideoState(raw);
  render();
}

function validateBattle(raw) {
  if (!raw || typeof raw !== "object" || !Array.isArray(raw.Events)) throw new Error(t("app.invalidFile"));
  if (!Number.isFinite(raw.DurationMs) || raw.DurationMs <= 0) throw new Error(t("app.invalidDuration"));
}

function createVideoState(raw) {
  const config = window.BATTLE_VIDEO || null;
  const battleMatches = Boolean(config && raw.BattleId === config.battleId);
  const expectedDurationMs = Number(config?.expectedDurationMs) || 0;
  const syncAnchors = battleMatches ? normalizeSyncAnchors(config?.sync?.anchors, raw.DurationMs) : [];
  const syncMode = resolveVideoSyncMode(config?.sync?.mode, syncAnchors);
  const recordingStatus = !config?.src
    ? "missing"
    : !battleMatches
      ? "mismatch"
      : !["captured", "exact"].includes(config?.sync?.mode)
        ? "legacy"
        : syncMode === "unavailable"
          ? "invalid"
          : "valid";
  return {
    src: recordingStatus === "valid" ? config.src : null,
    configuredSrc: config?.src || null,
    recordingStatus,
    durationMs: expectedDurationMs,
    expectedDurationMs,
    syncMode,
    syncAnchors,
    expanded: matchMedia("(min-height: 1001px)").matches,
    expandedTouched: false,
    committedCombatMs: 0,
    previewCombatMs: null,
    mediaMs: 0,
    ready: false,
    error: null,
    wasPlaying: false,
    muted: false,
    timelineHoverActive: false,
    resumeAfterScrub: false,
    seekSamples: [],
    seekP95Ms: 0,
  };
}

function normalizeSyncAnchors(anchors, combatDurationMs) {
  const normalized = (Array.isArray(anchors) ? anchors : [])
    .map((anchor) => {
      const encoderFrameIndex = Number.isFinite(Number(anchor?.encoderFrameIndex)) ? Number(anchor.encoderFrameIndex) : null;
      const mediaPtsMs = Number.isFinite(Number(anchor?.mediaPtsMs)) ? Number(anchor.mediaPtsMs) : null;
      return {
        combatFrame: Number.isFinite(Number(anchor?.combatFrame)) ? Number(anchor.combatFrame) : null,
        combatMs: Number(anchor?.combatMs),
        captureClockMs: Number.isFinite(Number(anchor?.captureClockMs)) ? Number(anchor.captureClockMs) : null,
        captureFrameIndex: Number.isFinite(Number(anchor?.captureFrameIndex)) ? Number(anchor.captureFrameIndex) : null,
        cfrSlotIndex: Number.isFinite(Number(anchor?.cfrSlotIndex)) ? Number(anchor.cfrSlotIndex) : null,
        encoderFrameIndex,
        mediaPtsMs,
        mediaMs: mediaPtsMs,
      };
    })
    .filter((anchor) => anchor.combatFrame != null && Number.isFinite(anchor.combatMs) && Number.isFinite(anchor.mediaPtsMs))
    .map((anchor) => ({
      ...anchor,
      combatMs: clamp(anchor.combatMs, 0, combatDurationMs),
      mediaMs: Math.max(0, anchor.mediaMs),
    }))
    .sort((left, right) => left.mediaMs - right.mediaMs || left.combatMs - right.combatMs);
  const monotonic = [];
  for (const anchor of normalized) {
    const previous = monotonic.at(-1);
    if (previous && anchor.mediaMs <= previous.mediaMs) continue;
    if (previous && anchor.combatMs < previous.combatMs) continue;
    monotonic.push(anchor);
  }
  return monotonic.length >= 2 ? monotonic : [];
}

function resolveVideoSyncMode(requestedMode, anchors) {
  if (requestedMode !== "captured" && requestedMode !== "exact") return "unavailable";
  if (anchors.length < 2) return "unavailable";
  return requestedMode;
}

function syncAnchorsFor(inputKey) {
  const anchors = state.video?.syncAnchors || [];
  if (inputKey === "mediaMs") return anchors;
  const seekAnchors = [];
  for (const anchor of anchors) {
    if (seekAnchors.at(-1)?.combatMs === anchor.combatMs) continue;
    seekAnchors.push(anchor);
  }
  return seekAnchors;
}

function interpolateSyncTime(value, inputKey, outputKey) {
  const anchors = syncAnchorsFor(inputKey);
  if (!anchors.length) return Math.max(0, Number(value) || 0);
  if (anchors.length === 1 || value <= anchors[0][inputKey]) return anchors[0][outputKey];
  if (value >= anchors.at(-1)[inputKey]) return anchors.at(-1)[outputKey];
  for (let index = 1; index < anchors.length; index += 1) {
    const right = anchors[index];
    if (value > right[inputKey]) continue;
    const left = anchors[index - 1];
    const inputSpan = right[inputKey] - left[inputKey];
    if (inputSpan <= 0) return right[outputKey];
    const progress = (value - left[inputKey]) / inputSpan;
    return left[outputKey] + (right[outputKey] - left[outputKey]) * progress;
  }
  return anchors.at(-1)[outputKey];
}

function combatToMediaMs(combatMs) {
  return interpolateSyncTime(clamp(Number(combatMs) || 0, 0, state.raw.DurationMs), "combatMs", "mediaMs");
}

function mediaToCombatMs(mediaMs) {
  return clamp(interpolateSyncTime(Math.max(0, Number(mediaMs) || 0), "mediaMs", "combatMs"), 0, state.raw.DurationMs);
}

function videoSyncLabel(mode) {
  if (mode === "exact") return t("video.syncExact");
  if (mode === "captured") return t("video.syncCaptured");
  return t("video.syncUnavailable");
}

function snapshotVideoPlayback() {
  if (!combatVideo || !state.video) return;
  const mediaMs = Number.isFinite(combatVideo.currentTime) ? combatVideo.currentTime * 1000 : state.video.mediaMs;
  state.video.mediaMs = mediaMs;
  state.video.wasPlaying = !combatVideo.paused && !combatVideo.ended;
  if (state.video.wasPlaying) state.video.committedCombatMs = mediaToCombatMs(mediaMs);
}

function teardownVideoRuntime() {
  clearTimeout(videoPreviewTimer);
  videoPreviewTimer = null;
  pendingVideoSeek = null;
  videoSeekInFlight = false;
  if (combatVideo && videoFrameRequestId != null && typeof combatVideo.cancelVideoFrameCallback === "function") {
    combatVideo.cancelVideoFrameCallback(videoFrameRequestId);
  }
  if (videoFallbackFrameId != null) cancelAnimationFrame(videoFallbackFrameId);
  videoFrameRequestId = null;
  videoFallbackFrameId = null;
  combatVideo = null;
}

function mountVideo() {
  const video = document.querySelector("#combat-video");
  if (!video || !state.video) return;
  combatVideo = video;
  combatVideo.muted = state.video.muted;
  let metadataMounted = false;

  const onMetadata = () => {
    if (metadataMounted) return;
    metadataMounted = true;
    const durationMs = Number.isFinite(combatVideo.duration) ? combatVideo.duration * 1000 : state.video.expectedDurationMs;
    state.video.durationMs = durationMs;
    state.video.ready = durationMs > 0;
    state.video.error = null;
    queueVideoSeek(state.video.committedCombatMs, "mount", true);
    updateTimelineCursor(state.video.committedCombatMs);
    updateVideoUi(true);
    if (state.video.wasPlaying) combatVideo.play().catch(() => { state.video.wasPlaying = false; updateVideoUi(true); });
  };

  combatVideo.addEventListener("loadedmetadata", onMetadata);
  combatVideo.addEventListener("seeked", handleVideoSeeked);
  combatVideo.addEventListener("play", () => {
    state.video.wasPlaying = true;
    state.video.previewCombatMs = null;
    state.video.timelineHoverActive = false;
    pendingVideoSeek = null;
    startVideoPlaybackLoop();
    updateVideoUi(true);
  });
  combatVideo.addEventListener("pause", () => {
    state.video.wasPlaying = false;
    stopVideoPlaybackLoop();
    updateVideoUi(true);
  });
  combatVideo.addEventListener("ended", () => {
    state.video.wasPlaying = false;
    stopVideoPlaybackLoop();
    updateVideoUi(true);
  });
  combatVideo.addEventListener("error", () => {
    state.video.error = t("video.loadFailed");
    document.querySelector("#video-stage")?.classList.add("video-failed");
    updateVideoUi(true);
  });
  combatVideo.addEventListener("click", toggleVideoPlayback);

  document.querySelector("#video-play")?.addEventListener("click", toggleVideoPlayback);
  document.querySelector("#video-expand-toggle")?.addEventListener("click", () => setVideoExpanded(!state.video.expanded, true));
  document.querySelector("#video-step-back")?.addEventListener("click", () => commitVideoAtCombatTime(state.video.committedCombatMs - 500));
  document.querySelector("#video-step-forward")?.addEventListener("click", () => commitVideoAtCombatTime(state.video.committedCombatMs + 500));
  document.querySelector("#video-audio")?.addEventListener("click", () => {
    state.video.muted = !state.video.muted;
    combatVideo.muted = state.video.muted;
    updateVideoUi(true);
  });

  const scrubber = document.querySelector("#video-scrubber");
  scrubber?.addEventListener("pointerdown", () => {
    state.video.resumeAfterScrub = !combatVideo.paused;
    combatVideo.pause();
  });
  scrubber?.addEventListener("input", () => commitVideoAtCombatTime(Number(scrubber.value)));
  scrubber?.addEventListener("change", () => {
    if (!state.video.resumeAfterScrub) return;
    state.video.resumeAfterScrub = false;
    combatVideo.play().catch(() => updateVideoUi(true));
  });

  if (combatVideo.readyState >= 1) onMetadata();
  else updateVideoUi(true);
}

function setVideoExpanded(expanded, userInitiated = false) {
  if (!state.video?.src) return;
  state.video.expanded = Boolean(expanded);
  if (userInitiated) state.video.expandedTouched = true;
  const workbench = document.querySelector("#video-workbench");
  const stage = document.querySelector("#video-stage");
  const toggle = document.querySelector("#video-expand-toggle");
  workbench?.classList.toggle("is-expanded", state.video.expanded);
  if (stage) stage.hidden = !state.video.expanded;
  if (toggle) {
    toggle.setAttribute("aria-expanded", String(state.video.expanded));
    toggle.setAttribute("aria-label", t(state.video.expanded ? "video.collapse" : "video.expand"));
    toggle.textContent = t(state.video.expanded ? "video.collapseShort" : "video.expandShort");
  }
  requestAnimationFrame(() => {
    updateVisibleRange();
    combatChart?.resize();
    stateChart?.resize();
  });
}

function toggleVideoPlayback() {
  if (!combatVideo || !state.video?.ready) return;
  if (combatVideo.paused || combatVideo.ended) {
    state.video.previewCombatMs = null;
    if (combatVideo.ended) {
      state.video.committedCombatMs = 0;
      queueVideoSeek(0, "commit", true);
      updateTimelineCursor(0);
      updateVideoUi(true);
    }
    combatVideo.play().catch(() => updateVideoUi(true));
  } else {
    combatVideo.pause();
  }
}

function videoPreviewIntervalMs() {
  return state.video?.seekP95Ms > 110 ? VIDEO_PREVIEW_SLOW_INTERVAL_MS : VIDEO_PREVIEW_BASE_INTERVAL_MS;
}

function requestVideoPreview(combatMs) {
  if (!combatVideo || !state.video?.ready || !combatVideo.paused || state.video.resumeAfterScrub) return;
  state.video.previewCombatMs = clamp(combatMs, 0, state.raw.DurationMs);
  updateVideoUi(true);
  queueVideoSeek(state.video.previewCombatMs, "preview", false);
}

function queueVideoSeek(combatMs, reason, immediate) {
  if (!combatVideo || !state.video || combatVideo.readyState < 1) return;
  const normalizedCombatMs = clamp(Number(combatMs) || 0, 0, state.raw.DurationMs);
  const mediaMs = combatToMediaMs(normalizedCombatMs);
  pendingVideoSeek = { combatMs: normalizedCombatMs, mediaMs, reason };
  if (immediate) {
    clearTimeout(videoPreviewTimer);
    videoPreviewTimer = null;
    flushVideoSeek();
    return;
  }
  if (videoPreviewTimer || videoSeekInFlight) return;
  videoPreviewTimer = setTimeout(() => {
    videoPreviewTimer = null;
    flushVideoSeek();
  }, videoPreviewIntervalMs());
}

function flushVideoSeek() {
  if (!combatVideo || !state.video || videoSeekInFlight || !pendingVideoSeek || combatVideo.readyState < 1) return;
  const request = pendingVideoSeek;
  pendingVideoSeek = null;
  if (Math.abs(combatVideo.currentTime * 1000 - request.mediaMs) <= VIDEO_SEEK_EPSILON_MS) {
    state.video.mediaMs = request.mediaMs;
    updateVideoUi(true);
    if (pendingVideoSeek) flushVideoSeek();
    return;
  }
  videoSeekInFlight = true;
  videoSeekStartedAt = performance.now();
  combatVideo.currentTime = clamp(request.mediaMs / 1000, 0, Number.isFinite(combatVideo.duration) ? combatVideo.duration : request.mediaMs / 1000);
}

function handleVideoSeeked() {
  if (!state.video || !combatVideo) return;
  if (videoSeekInFlight) {
    const latency = performance.now() - videoSeekStartedAt;
    state.video.seekSamples.push(latency);
    if (state.video.seekSamples.length > 30) state.video.seekSamples.shift();
    const ordered = [...state.video.seekSamples].sort((left, right) => left - right);
    state.video.seekP95Ms = ordered[Math.max(0, Math.ceil(ordered.length * .95) - 1)] || 0;
  }
  videoSeekInFlight = false;
  state.video.mediaMs = combatVideo.currentTime * 1000;
  updateVideoUi(true);
  if (pendingVideoSeek) flushVideoSeek();
}

function commitVideoAtCombatTime(combatMs) {
  if (!state.video?.src) return;
  const normalized = clamp(Number(combatMs) || 0, 0, state.raw.DurationMs);
  state.video.committedCombatMs = normalized;
  state.video.previewCombatMs = null;
  state.video.resumeAfterScrub = false;
  combatVideo?.pause();
  queueVideoSeek(normalized, "commit", true);
  updateTimelineCursor(normalized);
  updateVideoUi(true);
}

function restoreCommittedVideoFrame() {
  if (!state.video?.src || !combatVideo?.paused || state.video.previewCombatMs == null) return;
  state.video.previewCombatMs = null;
  queueVideoSeek(state.video.committedCombatMs, "restore", true);
  updateVideoUi(true);
}

function startVideoPlaybackLoop() {
  stopVideoPlaybackLoop();
  if (!combatVideo) return;
  if (typeof combatVideo.requestVideoFrameCallback === "function") {
    const onFrame = (_now, metadata) => {
      if (!combatVideo || combatVideo.paused || combatVideo.ended) return;
      syncTimelineFromVideo(metadata.mediaTime * 1000);
      videoFrameRequestId = combatVideo.requestVideoFrameCallback(onFrame);
    };
    videoFrameRequestId = combatVideo.requestVideoFrameCallback(onFrame);
    return;
  }
  const onAnimationFrame = () => {
    if (!combatVideo || combatVideo.paused || combatVideo.ended) return;
    syncTimelineFromVideo(combatVideo.currentTime * 1000);
    videoFallbackFrameId = requestAnimationFrame(onAnimationFrame);
  };
  videoFallbackFrameId = requestAnimationFrame(onAnimationFrame);
}

function stopVideoPlaybackLoop() {
  if (combatVideo && videoFrameRequestId != null && typeof combatVideo.cancelVideoFrameCallback === "function") {
    combatVideo.cancelVideoFrameCallback(videoFrameRequestId);
  }
  if (videoFallbackFrameId != null) cancelAnimationFrame(videoFallbackFrameId);
  videoFrameRequestId = null;
  videoFallbackFrameId = null;
}

function syncTimelineFromVideo(mediaMs) {
  if (!state.video) return;
  state.video.mediaMs = mediaMs;
  state.video.committedCombatMs = mediaToCombatMs(mediaMs);
  if (!state.video.timelineHoverActive) {
    updateTimelineCursor(state.video.committedCombatMs);
    keepTimelinePlayheadVisible(state.video.committedCombatMs);
  }
  updateVideoUi(false);
}

function keepTimelinePlayheadVisible(combatMs) {
  const scroll = document.querySelector("#timeline-scroll");
  if (!scroll) return;
  const visibleWidth = visibleTimelineWidthPx(scroll);
  const x = timeToPixel(combatMs);
  const lower = scroll.scrollLeft + visibleWidth * .12;
  const upper = scroll.scrollLeft + visibleWidth * .88;
  if (x >= lower && x <= upper) return;
  scroll.scrollLeft = clamp(x - visibleWidth * .24, 0, Math.max(0, scroll.scrollWidth - scroll.clientWidth));
}

function updateVideoUi(force = false) {
  if (!state.video) return;
  const now = performance.now();
  if (!force && now - lastVideoUiUpdateAt < 50) return;
  lastVideoUiUpdateAt = now;
  const previewing = state.video.previewCombatMs != null && Boolean(combatVideo?.paused);
  const combatMs = previewing ? state.video.previewCombatMs : state.video.committedCombatMs;
  const mediaMs = previewing ? combatToMediaMs(combatMs) : state.video.mediaMs;
  const combatTime = document.querySelector("#video-combat-time");
  const mediaTime = document.querySelector("#video-media-time");
  const scrubber = document.querySelector("#video-scrubber");
  const play = document.querySelector("#video-play");
  const audio = document.querySelector("#video-audio");
  const hint = document.querySelector("#video-mode-hint");
  const previewBadge = document.querySelector("#video-preview-badge");
  if (combatTime) combatTime.textContent = formatTime(combatMs);
  if (mediaTime) mediaTime.textContent = formatTime(mediaMs);
  if (scrubber) scrubber.value = Math.round(combatMs);
  if (play) {
    const playing = Boolean(combatVideo && !combatVideo.paused && !combatVideo.ended);
    play.textContent = playing ? "Ⅱ" : "▶";
    play.setAttribute("aria-label", t(playing ? "video.pause" : "video.play"));
  }
  if (audio) {
    audio.textContent = state.video.muted ? "○" : "◕";
    audio.setAttribute("aria-label", t(state.video.muted ? "video.unmute" : "video.mute"));
  }
  if (hint) hint.textContent = state.video.error || t(combatVideo && !combatVideo.paused ? "video.hoverHintPlaying" : "video.hoverHintPaused");
  if (previewBadge) previewBadge.hidden = !previewing;
}

function renderVideoSyncAnchors() {
  const container = document.querySelector("#video-sync-anchors");
  const badge = document.querySelector("#video-sync-badge");
  if (container) container.innerHTML = state.video.syncAnchors.length
    ? renderVideoSyncAnchorMarkup(state.video.syncAnchors)
    : `<i>${escapeHtml(t("video.syncPending"))}</i>`;
  if (badge) {
    badge.className = `video-sync-badge ${state.video.syncMode}`;
    badge.textContent = videoSyncLabel(state.video.syncMode);
  }
}

function renderVideoSyncAnchorMarkup(anchors) {
  const visible = anchors.length <= 4
    ? anchors
    : [anchors[0], null, anchors[Math.floor(anchors.length / 2)], null, anchors.at(-1)];
  return visible.map((anchor) => anchor
    ? `<i>${formatTime(anchor.combatMs)}<b>↔</b>${formatTime(anchor.mediaMs)}</i>`
    : '<i class="video-anchor-gap" aria-hidden="true">…</i>').join("");
}

window.__BPP_VIDEO_DEBUG__ = () => ({
  battleId: state.raw?.BattleId || null,
  recordingStatus: state.video?.recordingStatus || "missing",
  expanded: Boolean(state.video?.expanded),
  ready: Boolean(state.video?.ready),
  syncMode: state.video?.syncMode || "unavailable",
  anchors: state.video?.syncAnchors || [],
  anchorCount: state.video?.syncAnchors?.length || 0,
  seekSamples: state.video?.seekSamples || [],
  seekP95Ms: state.video?.seekP95Ms || 0,
  committedCombatMs: state.video?.committedCombatMs || 0,
  previewCombatMs: state.video?.previewCombatMs ?? null,
  mediaMs: state.video?.mediaMs || 0,
});

function fallbackMeta(raw) {
  const ids = new Set(["player:Player", "player:Opponent"]);
  for (const event of raw.Events) {
    if (event.Source) ids.add(event.Source);
    if (event.TriggerSource) ids.add(event.TriggerSource);
    for (const target of event.Targets || []) ids.add(target);
  }
  return {
    battleId: raw.BattleId,
    day: "—",
    player: { id: "player:Player", name: "Player", hero: t("entity.unknownHero") },
    opponent: { id: "player:Opponent", name: "Opponent", hero: t("entity.unknownHero") },
    lanes: [...ids].map((id, order) => ({
      id,
      owner: id === "player:Player" ? "player" : id === "player:Opponent" ? "opponent" : "unknown",
      type: id.startsWith("player:") ? "hero" : id.startsWith("skl_") ? "skill" : "item",
      order,
    })),
  };
}

function hydrateLanes(meta, assets) {
  return meta.lanes.map((lane) => {
    if (lane.type === "hero") {
      const combatant = lane.owner === "player" ? meta.player : meta.opponent;
      return {
        ...lane,
        name: combatant.name,
        detail: combatant.hero,
        asset: lane.owner === "player" ? assets?.heroes?.Player : assets?.heroes?.Opponent,
      };
    }
    const entity = assets?.entities?.[lane.id];
    const hasDisplayName = entity?.name && entity.name !== entity.templateId && !/^[0-9a-f-]{36}$/i.test(entity.name);
    const recordedSize = entity?.size || lane.size;
    const size = lane.type === "item" && ITEM_SIZE_SPANS[recordedSize] ? recordedSize : null;
    return {
      ...lane,
      name: hasDisplayName ? entity.name : lane.type === "skill" ? t("entity.unknownSkill") : t("entity.unknownItem"),
      detail: [lane.type === "skill" ? t("entity.skill") : t("entity.item"), tierLabel(entity?.tier), entity?.enchant, hasDisplayName ? null : shortId(entity?.templateId || lane.id)].filter(Boolean).join(" · "),
      asset: entity?.asset || null,
      templateId: entity?.templateId || null,
      tier: entity?.tier || null,
      enchant: entity?.enchant || null,
      size,
      span: size ? ITEM_SIZE_SPANS[size] : 1,
    };
  }).sort((left, right) => left.order - right.order);
}

function projectEvents(rawEvents) {
  const groups = new Map();
  const rawEventsByFrame = indexRawEventsByFrame(rawEvents);
  for (const raw of rawEvents) {
    if (raw.Kind !== "effect-executed") continue;
    if (STATE_APPLY_ACTIONS.has(raw.Action) && !shouldProjectStateApplyAsSkillTrigger(raw)) continue;
    if (raw.Action === "CardModifyAttribute") continue;
    const key = [raw.AtMs, raw.Source || "system", raw.TriggerSource || "", raw.Action].join("|");
    if (!groups.has(key)) {
      groups.set(key, {
        id: `effect-${key}`,
        atMs: raw.AtMs,
        kind: state.laneById.get(raw.Source)?.type === "skill" ? "skill-trigger" : "effect",
        action: raw.Action,
        source: raw.Source || null,
        triggerSource: raw.TriggerSource || null,
        targets: [],
        raw: [],
      });
    }
    const event = groups.get(key);
    event.targets.push(...(raw.Targets || []));
    event.raw.push(raw);
  }

  const effects = [...groups.values()].map((event) => {
    const count = event.raw.length;
    const targets = [...new Set(event.targets)];
    const stateResult = resolveSkillStateTriggerResult({ ...event, targets }, rawEventsByFrame);
    return { ...event, ...stateResult, targets, count };
  });
  const health = rawEvents.filter((raw) => raw.Kind === "health").map((raw) => ({
    id: raw.Id,
    atMs: raw.AtMs,
    kind: "health",
    action: raw.Action,
    source: null,
    triggerSource: null,
    targets: raw.Targets || [],
    amount: raw.Amount,
    isCrit: Boolean(raw.IsCrit),
    count: 1,
    raw: [raw],
  }));
  const deaths = rawEvents.filter((raw) => raw.Kind === "combatant-died").map((raw) => ({
    id: raw.Id,
    atMs: raw.AtMs,
    kind: "death",
    action: "Died",
    source: null,
    triggerSource: null,
    targets: raw.Targets || [],
    count: 1,
    raw: [raw],
  }));
  const cardAttributes = projectCardAttributeChanges(rawEvents);
  return [...effects, ...cardAttributes, ...health, ...deaths].sort((left, right) => left.atMs - right.atMs || left.id.localeCompare(right.id));
}

function shouldProjectStateApplyAsSkillTrigger(raw) {
  return Boolean(SKILL_STATE_TRIGGER_METRICS[raw.Action] && state.laneById.get(raw.Source)?.type === "skill");
}

function indexRawEventsByFrame(rawEvents) {
  const byFrame = new Map();
  for (const event of rawEvents) {
    if (!byFrame.has(event.Frame)) byFrame.set(event.Frame, []);
    byFrame.get(event.Frame).push(event);
  }
  return byFrame;
}

function eventFrame(event) {
  if (event?.frame != null && Number.isFinite(Number(event.frame))) return Number(event.frame);
  const raw = (event?.raw || []).find((candidate) => Number.isFinite(Number(candidate?.Frame)));
  return raw ? Number(raw.Frame) : null;
}

function buildFrameIndex(rawEvents, projectedEvents) {
  const rawByFrame = indexRawEventsByFrame(rawEvents);
  const projectedByFrame = new Map();
  for (const event of projectedEvents) {
    const frame = eventFrame(event);
    if (frame == null) continue;
    if (!projectedByFrame.has(frame)) projectedByFrame.set(frame, []);
    projectedByFrame.get(frame).push(event);
  }
  const frames = new Set([...rawByFrame.keys(), ...projectedByFrame.keys()]);
  return [...frames].map((frame) => {
    const raw = rawByFrame.get(frame) || [];
    const events = [...new Map((projectedByFrame.get(frame) || []).map((event) => [event.id, event])).values()]
      .sort(compareFrameEvents);
    const fallbackAtMs = Number(frame) * (state.raw?.FrameDurationMs || 50);
    return {
      frame: Number(frame),
      atMs: Number(raw[0]?.AtMs ?? events[0]?.atMs ?? fallbackAtMs),
      raw,
      events,
    };
  }).sort((left, right) => left.atMs - right.atMs || left.frame - right.frame);
}

function compareFrameEvents(left, right) {
  const order = { health: 0, death: 0, effect: 1, "skill-trigger": 1, "card-attribute": 2, "player-state": 3, "status-interval": 4 };
  const leftOrder = order[left.kind] ?? 5;
  const rightOrder = order[right.kind] ?? 5;
  if (leftOrder !== rightOrder) return leftOrder - rightOrder;
  return left.atMs - right.atMs || left.id.localeCompare(right.id);
}

function frameRecordAtTime(atMs) {
  const frames = state.frames;
  if (!frames.length) return null;
  let low = 0;
  let high = frames.length - 1;
  while (low <= high) {
    const middle = (low + high) >> 1;
    if (frames[middle].atMs < atMs) low = middle + 1;
    else high = middle - 1;
  }
  const before = frames[Math.max(0, high)];
  const after = frames[Math.min(frames.length - 1, low)];
  return Math.abs(after.atMs - atMs) < Math.abs(atMs - before.atMs) ? after : before;
}

function frameRecordForEvent(event) {
  const frame = eventFrame(event);
  return frame == null ? frameRecordAtTime(event?.atMs || 0) : state.frameByNumber.get(frame) || frameRecordAtTime(event?.atMs || 0);
}

function resolveSkillStateTriggerResult(event, rawEventsByFrame) {
  const metric = SKILL_STATE_TRIGGER_METRICS[event.action];
  if (!metric || event.kind !== "skill-trigger") return {};
  const frames = new Set(event.raw.map((raw) => raw.Frame));
  const targetIds = new Set(event.targets || []);
  const overlapsTarget = (candidate) => (candidate.Targets || []).some((target) => targetIds.has(target));
  const related = [...frames].flatMap((frame) => rawEventsByFrame.get(frame) || []).filter(overlapsTarget);
  const competingEffects = related.filter((candidate) => candidate.Kind === "effect-executed" && candidate.Action === event.action);
  const stateUpdates = related.filter((candidate) => candidate.Kind === "player-attribute" && candidate.Action === metric);
  const eventRawIds = new Set(event.raw.map((raw) => raw.Id));
  const uniquelyAttributed = competingEffects.length === event.raw.length
    && competingEffects.every((candidate) => eventRawIds.has(candidate.Id));

  if (uniquelyAttributed && stateUpdates.length === 1) {
    const update = stateUpdates[0];
    return {
      stateMetric: metric,
      stateResult: "changed",
      amount: update.Amount,
      previousValue: update.PreviousValue,
      currentValue: update.CurrentValue,
      raw: [...event.raw, update],
    };
  }
  return {
    stateMetric: metric,
    stateResult: stateUpdates.length ? "related" : "no-change-recorded",
    raw: [...event.raw, ...stateUpdates],
  };
}

function projectCardAttributeChanges(rawEvents) {
  const modifiersByFrame = new Map();
  for (const event of rawEvents) {
    if (event.Kind !== "effect-executed" || event.Action !== "CardModifyAttribute") continue;
    if (!modifiersByFrame.has(event.Frame)) modifiersByFrame.set(event.Frame, []);
    modifiersByFrame.get(event.Frame).push(event);
  }
  const groups = new Map();
  for (const raw of rawEvents) {
    if (raw.Kind !== "card-attribute" || ROUTINE_CARD_ATTRIBUTES.has(raw.Action)) continue;
    const candidates = (modifiersByFrame.get(raw.Frame) || [])
      .filter((candidate) => (candidate.Targets || []).includes(raw.Source));
    if (!candidates.length) continue;
    const key = `${raw.Frame}|${raw.Source}`;
    if (!groups.has(key)) groups.set(key, {
      id: `card-attribute-${key}`,
      atMs: raw.AtMs,
      frame: raw.Frame,
      kind: "card-attribute",
      action: "CardModifyAttribute",
      targets: [raw.Source],
      attributeChanges: [],
      modifiers: [],
    });
    const group = groups.get(key);
    group.attributeChanges.push({
      attribute: raw.Action,
      amount: raw.Amount,
      previousValue: raw.PreviousValue,
      currentValue: raw.CurrentValue,
      raw,
    });
    group.modifiers.push(...candidates);
  }
  return [...groups.values()].map((group) => {
    const modifiers = uniqueEvents(group.modifiers);
    const sources = [...new Set(modifiers.map((candidate) => candidate.Source)
      .filter((id) => id && state.laneById.has(id)))];
    const triggerSources = [...new Set(modifiers.map((candidate) => candidate.TriggerSource)
      .filter((id) => id && state.laneById.has(id)))];
    const onlyChange = group.attributeChanges.length === 1 ? group.attributeChanges[0] : null;
    return {
      ...group,
      attribute: onlyChange?.attribute || null,
      source: sources[0] || null,
      sources,
      triggerSource: triggerSources[0] || null,
      triggerSources,
      sourceConfidence: modifiers.length === 1 ? "direct" : "related",
      amount: onlyChange?.amount ?? null,
      previousValue: onlyChange?.previousValue ?? null,
      currentValue: onlyChange?.currentValue ?? null,
      count: modifiers.length + group.attributeChanges.length,
      raw: [...modifiers, ...group.attributeChanges.map((change) => change.raw)],
      modifiers: undefined,
    };
  });
}

function projectPlayerStateMetrics(rawEvents, durationMs) {
  const eventsByFrame = new Map();
  for (const event of rawEvents) {
    if (!eventsByFrame.has(event.Frame)) eventsByFrame.set(event.Frame, []);
    eventsByFrame.get(event.Frame).push(event);
  }

  return PLAYER_STATE_ORDER.map((metric) => {
    const definition = PLAYER_STATE_METRICS[metric];
    const updates = rawEvents.filter((event) => event.Kind === "player-attribute" && event.Action === metric);
    const tracks = ["player", "opponent"].flatMap((owner) => {
      const targetId = owner === "player" ? "player:Player" : "player:Opponent";
      const ownerUpdates = updates
        .filter((event) => (event.Targets || []).includes(targetId))
        .sort((left, right) => left.AtMs - right.AtMs || left.Id.localeCompare(right.Id));
      if (!ownerUpdates.length) {
        return [{ owner, targetId, initialValue: 0, changes: [], points: [{ atMs: 0, value: 0 }, { atMs: durationMs, value: 0 }] }];
      }
      const changes = ownerUpdates.map((raw) => {
        const frameEvents = eventsByFrame.get(raw.Frame) || [];
        const context = resolveStateChangeContext(metric, raw, frameEvents, targetId, definition);
        return {
          id: `state-${raw.Id}`,
          atMs: raw.AtMs,
          frame: raw.Frame,
          kind: "player-state",
          metric,
          action: metric,
          owner,
          source: context.sources[0] || null,
          sources: context.sources,
          triggerSource: context.triggerSources[0] || null,
          triggerSources: context.triggerSources,
          targets: raw.Targets || [],
          amount: raw.Amount,
          previousValue: raw.PreviousValue,
          currentValue: raw.CurrentValue,
          causes: context.causes,
          causeKeys: context.causeKeys,
          sourceConfidence: context.sourceConfidence,
          count: 1 + context.causes.length,
          raw: [raw, ...context.causes],
        };
      });
      const initialValue = Number(ownerUpdates[0].PreviousValue) || 0;
      const points = [{ atMs: 0, value: initialValue }];
      for (const change of changes) points.push({ atMs: change.atMs, value: change.currentValue, eventId: change.id });
      if (points.at(-1).atMs < durationMs) points.push({ atMs: durationMs, value: points.at(-1).value });
      return [{ owner, targetId, initialValue, changes, points }];
    });
    const observedMax = Math.max(0, ...tracks.flatMap((track) => track.points.map((point) => point.value)));
    return { metric, ...definition, ceiling: metric === "Rage" && observedMax <= 100 ? 100 : niceCeiling(observedMax), tracks };
  }).filter((metric) => metric.tracks.length);
}

function resolveStateChangeContext(metric, raw, frameEvents, targetId, definition) {
  const targetEvents = frameEvents.filter((candidate) => candidate.Id !== raw.Id && (candidate.Targets || []).includes(targetId));
  const competingUpdates = frameEvents.filter((candidate) => candidate.Kind === "player-attribute"
    && candidate.Action === metric
    && (candidate.Targets || []).includes(targetId));
  const settlementPrefix = metric === "Health" ? "Health:" : metric === "Shield" ? "Shield:" : null;
  const settlements = settlementPrefix
    ? targetEvents.filter((candidate) => candidate.Kind === "health" && candidate.Action.startsWith(settlementPrefix))
    : [];
  const expectedEffects = new Set(settlements.map((candidate) => STATE_SETTLEMENT_EFFECT_ACTIONS[candidate.Action]).filter(Boolean));
  if (!settlements.length && raw.Amount > 0 && definition.applyAction) expectedEffects.add(definition.applyAction);
  const effectCauses = targetEvents.filter((candidate) => candidate.Kind === "effect-executed" && expectedEffects.has(candidate.Action));
  const attributeCauses = metric === "Rage" && raw.Amount < 0
    ? targetEvents.filter((candidate) => candidate.Kind === "player-attribute" && ["Enraged", "EnragedDuration"].includes(candidate.Action))
    : [];
  const causes = uniqueEvents([...effectCauses, ...settlements, ...attributeCauses]);
  const sources = [...new Set(effectCauses.map((candidate) => candidate.Source).filter(Boolean))];
  const triggerSources = [...new Set(effectCauses.map((candidate) => candidate.TriggerSource).filter(Boolean))];
  const causeKeys = [];
  for (const settlement of settlements) {
    const effectAction = STATE_SETTLEMENT_EFFECT_ACTIONS[settlement.Action];
    if (!effectAction || !effectCauses.some((candidate) => candidate.Action === effectAction)) {
      causeKeys.push(STATE_SETTLEMENT_FALLBACK_KEYS[settlement.Action] || "common.systemSettlement");
    }
  }
  if (!causes.length) {
    if (metric === "Rage" && raw.Amount < 0) causeKeys.push("cause.rageEnded");
    else causeKeys.push("cause.metricUnrecorded");
  } else if (metric === "Rage" && raw.Amount < 0) {
    causeKeys.push("cause.rageEnded");
  }
  const sourceConfidence = competingUpdates.length === 1 && effectCauses.length === 1 && settlements.length <= 1 && causeKeys.length === 0
    ? "direct"
    : effectCauses.length
    ? "related"
    : "settlement";
  return { causes, sources, triggerSources, causeKeys: [...new Set(causeKeys)], sourceConfidence };
}

function uniqueEvents(events) {
  const seen = new Set();
  return events.filter((event) => {
    if (seen.has(event.Id)) return false;
    seen.add(event.Id);
    return true;
  });
}

function niceCeiling(value) {
  if (value <= 0) return 1;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  return nice * magnitude;
}

function projectStatusIntervals(rawEvents, durationMs) {
  const tracked = new Set(["Haste", "Slow", "Freeze"]);
  const groups = new Map();
  const eventsByFrame = new Map();
  for (const event of rawEvents) {
    if (!eventsByFrame.has(event.Frame)) eventsByFrame.set(event.Frame, []);
    eventsByFrame.get(event.Frame).push(event);
  }
  for (const event of rawEvents) {
    if (event.Kind !== "card-attribute" || !tracked.has(event.Action)) continue;
    const key = `${event.Source}|${event.Action}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(event);
  }

  const intervals = [];
  for (const [key, updates] of groups) {
    updates.sort((left, right) => left.AtMs - right.AtMs);
    const splitAt = key.lastIndexOf("|");
    const laneId = key.slice(0, splitAt);
    const status = key.slice(splitAt + 1);
    let active = null;
    for (const update of updates) {
      const causes = (eventsByFrame.get(update.Frame) || []).filter((candidate) => candidate.Kind === "effect-executed"
        && candidate.Action === `Card${status}`
        && (candidate.Targets || []).includes(laneId));
      if (!active && update.CurrentValue > 0) {
        active = {
          id: `status-${laneId}-${status}-${update.AtMs}`,
          atMs: update.AtMs,
          kind: "status-interval",
          action: `Card${status}`,
          laneId,
          status,
          startMs: update.AtMs,
          endMs: durationMs,
          source: null,
          sources: [],
          triggerSource: null,
          triggerSources: [],
          targets: [laneId],
          applications: [],
          raw: [update],
        };
      } else if (active && causes.length) {
        active.raw.push(update);
      }
      if (active && causes.length) {
        active.applications.push({ atMs: update.AtMs, causes });
        active.raw.push(...causes);
      }
      if (active && update.PreviousValue > 0 && update.CurrentValue <= 0) {
        active.endMs = update.AtMs;
        if (!active.raw.some((raw) => raw.Id === update.Id)) active.raw.push(update);
        intervals.push(finalizeStatusInterval(active));
        active = null;
      }
    }
    if (active) intervals.push(finalizeStatusInterval(active));
  }
  return intervals;
}

function finalizeStatusInterval(interval) {
  const causes = interval.applications.flatMap((application) => application.causes);
  interval.sources = [...new Set(causes.map((cause) => cause.Source).filter(Boolean))];
  interval.triggerSources = [...new Set(causes.map((cause) => cause.TriggerSource).filter(Boolean))];
  interval.source = interval.sources[0] || null;
  interval.triggerSource = interval.triggerSources[0] || null;
  interval.durationMs = interval.endMs - interval.startMs;
  interval.count = interval.raw.length;
  return interval;
}

function render() {
  applyLocaleDocument();
  snapshotVideoPlayback();
  teardownVideoRuntime();
  if (laneScaleFrameId) cancelAnimationFrame(laneScaleFrameId);
  laneScaleFrameId = null;
  pendingLaneScaleAnchor = null;
  combatChart?.dispose();
  combatChart = null;
  stateChart?.dispose();
  stateChart = null;
  statsResizeObserver?.disconnect();
  statsResizeObserver = null;
  statsCharts.forEach((chart) => chart.dispose());
  statsCharts = [];

  const app = document.querySelector("#app");
  app.className = "";
  app.innerHTML = `
    <div class="report-shell" id="report-shell">
      ${renderHeader()}
      ${renderToolbar()}
      ${state.view === "stats" ? renderStatsPage() : renderTimelinePage()}
    </div>`;

  bindControls();
  if (state.view === "stats") mountStats();
  else {
    mountTimeline();
    mountVideo();
  }
}

function renderTimelinePage() {
  return `<main class="timeline-workspace">
    ${renderVideoWorkbench()}
    <div class="report-main">
      <section class="timeline-panel" aria-label="${escapeHtml(t("timeline.sectionAria"))}">
      <div class="timeline-heading">
        <strong>${escapeHtml(t("timeline.title"))}</strong>
        <span>${escapeHtml(tp("count.events", state.events.length))} · ${escapeHtml(tp("count.records", state.raw.Events.length))}</span>
        <span class="range-label" id="range-label"></span>
      </div>
      <div class="timeline-grid">
        <div class="timeline-scroll" id="timeline-scroll" tabindex="0" aria-label="${escapeHtml(t("timeline.scrollAria"))}">
          <div class="timeline-content" id="timeline-content">
            <div class="axis-corner"><span>${escapeHtml(t("timeline.axisCorner"))}</span>${renderLaneScaleControls()}</div>
            <div class="time-ruler" id="time-ruler"></div>
            <div class="lane-list" id="lane-list">
              <div class="state-lane-list" id="state-lane-list"></div>
              <div class="entity-lane-list" id="entity-lane-list"></div>
            </div>
            <div class="chart-stage" id="chart-stage">
              <div class="relation-overlay" id="relation-overlay" aria-hidden="true"></div>
              <div class="timeline-crosshair" id="timeline-crosshair" aria-hidden="true"><span></span></div>
              <div id="state-chart" role="img" aria-label="${escapeHtml(t("timeline.stateChartAria"))}"></div>
              <div id="combat-chart" role="img" aria-label="${escapeHtml(t("timeline.combatChartAria"))}"></div>
            </div>
          </div>
        </div>
      </div>
      </section>
      <div class="detail-scrim" id="detail-scrim"></div>
      <aside class="detail-panel" id="detail-panel">${renderDetail()}</aside>
    </div>
  </main>`;
}

function renderLaneScaleControls() {
  const layout = laneLayout();
  return `<div class="lane-zoom" role="group" aria-label="${escapeHtml(t("laneZoom.group"))}">
    <button id="lane-zoom-out" type="button" aria-label="${escapeHtml(t("laneZoom.out"))}" title="${escapeHtml(t("laneZoom.out"))}" ${state.laneScaleIndex === 0 ? "disabled" : ""}>−</button>
    <button id="lane-zoom-reset" type="button" aria-label="${escapeHtml(t("laneZoom.reset"))}" title="${escapeHtml(t("laneZoom.reset"))}">${layout.percent}%</button>
    <button id="lane-zoom-in" type="button" aria-label="${escapeHtml(t("laneZoom.in"))}" title="${escapeHtml(t("laneZoom.in"))}" ${state.laneScaleIndex === LANE_SCALE_LEVELS.length - 1 ? "disabled" : ""}>＋</button>
  </div>`;
}

function renderVideoWorkbench() {
  const video = state.video;
  const hasSource = Boolean(video?.src);
  if (!hasSource) {
    const reasonKey = {
      mismatch: "video.rerecordMismatch",
      legacy: "video.rerecordLegacy",
      invalid: "video.rerecordInvalid",
      missing: "video.rerecordMissing",
    }[video?.recordingStatus] || "video.rerecordMissing";
    return `<section class="video-recording-notice" id="video-workbench" aria-label="${escapeHtml(t("video.sectionAria"))}">
      <span class="video-recording-icon" aria-hidden="true">●</span>
      <div><strong>${escapeHtml(t("video.rerecordTitle"))}</strong><span>${escapeHtml(t(reasonKey))}</span></div>
    </section>`;
  }
  const syncMode = video?.syncMode || "unavailable";
  const anchors = video?.syncAnchors || [];
  return `<section class="video-workbench has-video ${video.expanded ? "is-expanded" : ""}" id="video-workbench" aria-label="${escapeHtml(t("video.sectionAria"))}">
    <div class="video-stage" id="video-stage" ${video.expanded ? "" : "hidden"}>
      <video id="combat-video" src="${escapeHtml(video.src)}" preload="auto" playsinline aria-label="${escapeHtml(t("video.playerAria"))}"></video>
      <span class="video-preview-badge" id="video-preview-badge" hidden>${escapeHtml(t("video.preview"))}</span>
    </div>
    <div class="video-console">
      <header class="video-console-head">
        <div><strong>${escapeHtml(t("video.title"))}</strong><span id="video-mode-hint">${escapeHtml(t("video.hoverHintPaused"))}</span></div>
        <div class="video-console-actions"><span class="video-sync-badge ${escapeHtml(syncMode)}" id="video-sync-badge">${escapeHtml(videoSyncLabel(syncMode))}</span><button class="video-expand-toggle" id="video-expand-toggle" type="button" aria-expanded="${video.expanded}" aria-label="${escapeHtml(t(video.expanded ? "video.collapse" : "video.expand"))}">${escapeHtml(t(video.expanded ? "video.collapseShort" : "video.expandShort"))}</button></div>
      </header>
      <div class="video-time-pair" aria-live="polite">
        <span><small>${escapeHtml(t("video.combatTime"))}</small><strong id="video-combat-time">${formatTime(video?.committedCombatMs || 0)}</strong></span>
        <i aria-hidden="true">↔</i>
        <span><small>${escapeHtml(t("video.mediaTime"))}</small><strong id="video-media-time">${formatTime(video?.mediaMs || 0)}</strong></span>
      </div>
      <div class="video-transport">
        <button class="video-play-button" id="video-play" type="button" ${hasSource ? "" : "disabled"} aria-label="${escapeHtml(t("video.play"))}">▶</button>
        <button class="video-step-button" id="video-step-back" type="button" ${hasSource ? "" : "disabled"} aria-label="${escapeHtml(t("video.stepBack"))}">−0.5</button>
        <label class="video-scrubber-label" for="video-scrubber">${escapeHtml(t("video.scrubber"))}</label>
        <input id="video-scrubber" type="range" min="0" max="${state.raw.DurationMs}" step="${state.raw.FrameDurationMs || 50}" value="${Math.round(video?.committedCombatMs || 0)}" ${hasSource ? "" : "disabled"} />
        <button class="video-step-button" id="video-step-forward" type="button" ${hasSource ? "" : "disabled"} aria-label="${escapeHtml(t("video.stepForward"))}">+0.5</button>
        <button class="video-audio-button" id="video-audio" type="button" ${hasSource ? "" : "disabled"} aria-label="${escapeHtml(t("video.mute"))}">◕</button>
      </div>
      <div class="video-sync-row">
        <span>${escapeHtml(t("video.syncAnchors"))}</span>
        <div class="video-sync-anchors" id="video-sync-anchors">${anchors.length
          ? renderVideoSyncAnchorMarkup(anchors)
          : `<i>${escapeHtml(t("video.syncPending"))}</i>`}</div>
      </div>
    </div>
  </section>`;
}

function renderStatsPage() {
  const { player, opponent } = state.stats.output;
  const totalHealing = player.healing + opponent.healing;
  const totalApplications = [...Object.values(player.tempo), ...Object.values(opponent.tempo)].reduce((sum, value) => sum + value, 0);
  const damageTypes = activeDamageTypes();
  const damageMode = damageTypes.length > 1 ? "multi" : damageTypes.length === 1 ? "single" : "empty";
  return `<main class="stats-page" aria-label="${escapeHtml(t("stats.aria"))}"><div class="stats-dashboard" data-damage-mode="${damageMode}">
    <header class="stats-heading"><div><strong>${escapeHtml(t("stats.title"))}</strong><span>${escapeHtml(t("stats.subtitle"))}</span></div><span>${escapeHtml(tp("count.records", state.raw.Events.length))}</span></header>
    <section class="stats-kpis" aria-label="${escapeHtml(t("stats.kpiAria"))}">
      ${renderStatKpi(t("stats.playerDamage"), player.damage, OWNER_COLORS.player)}
      ${renderStatKpi(t("stats.opponentDamage"), opponent.damage, OWNER_COLORS.opponent)}
      ${renderStatKpi(t("stats.totalHealing"), totalHealing, COLORS.heal)}
      ${renderStatKpi(t("stats.tempoApplications"), totalApplications, COLORS.status, tp("count.applications", totalApplications))}
    </section>
    <section class="stats-grid ${damageMode}-damage">
      <article class="stats-chart-card output-card">
        <header><div><strong>${escapeHtml(t("stats.outputTitle"))}</strong><span>${escapeHtml(t("stats.outputSubtitle"))}</span></div><div class="side-legend">${renderSideLegend()}</div></header>
        <div class="stats-chart" id="output-chart" style="--chart-rows:3" role="img" aria-label="${escapeHtml(t("stats.outputChartAria"))}"></div>
        ${damageMode === "multi" ? "" : renderDirectDamageSummary(damageTypes[0])}
      </article>
      ${damageMode === "multi" ? `<article class="stats-chart-card damage-card">
        <header><div><strong>${escapeHtml(t("stats.damageTitle"))}</strong><span>${escapeHtml(t("stats.damageSubtitle"))}</span></div></header>
        <div class="stats-chart" id="damage-chart" style="--chart-rows:2" role="img" aria-label="${escapeHtml(t("stats.damageChartAria"))}"></div>
        <div class="damage-legend">${damageTypes.map((key) => `<span><i style="--legend-color:${DAMAGE_TYPE_COLORS[key]}"></i>${escapeHtml(t(`damage.${key}`))}</span>`).join("")}</div>
      </article>` : ""}
      <article class="stats-chart-card tempo-card">
        <header><div><strong>${escapeHtml(t("stats.tempoTitle"))}</strong><span>${escapeHtml(t("stats.tempoSubtitle"))}</span></div><div class="side-legend">${renderSideLegend()}</div></header>
        <div class="stats-chart" id="tempo-chart" style="--chart-rows:4" role="img" aria-label="${escapeHtml(t("stats.tempoChartAria"))}"></div>
      </article>
    </section>
  </div></main>`;
}

function activeDamageTypes() {
  return DAMAGE_TYPE_KEYS.filter((type) => ["player", "opponent"]
    .some((owner) => Number(state.stats.output[owner].damageTypes[type]) > 0));
}

function renderDirectDamageSummary(type) {
  if (!type) return `<div class="damage-direct-summary empty"><strong>${escapeHtml(t("stats.damageNone"))}</strong><span>${escapeHtml(t("stats.damageNoneBody"))}</span></div>`;
  const playerValue = state.stats.output.player.damageTypes[type];
  const opponentValue = state.stats.output.opponent.damageTypes[type];
  const playerPercent = state.stats.output.player.damage > 0 ? playerValue / state.stats.output.player.damage * 100 : 0;
  const opponentPercent = state.stats.output.opponent.damage > 0 ? opponentValue / state.stats.output.opponent.damage * 100 : 0;
  return `<div class="damage-direct-summary" style="--damage-color:${DAMAGE_TYPE_COLORS[type]}">
    <strong>${escapeHtml(t("stats.damageSingle", { type: t(`damage.${type}`, {}, type) }))}</strong>
    <span>${escapeHtml(t("stats.damageSideValue", { side: t("toolbar.player"), value: formatMetricValue(playerValue), percent: formatPercent(playerPercent) }))}</span>
    <span>${escapeHtml(t("stats.damageSideValue", { side: t("toolbar.opponent"), value: formatMetricValue(opponentValue), percent: formatPercent(opponentPercent) }))}</span>
  </div>`;
}

function renderStatKpi(label, value, color, displayValue = formatCompactValue(value)) {
  return `<div class="stats-kpi" style="--kpi-color:${color}"><span>${escapeHtml(label)}</span><strong>${escapeHtml(displayValue)}</strong></div>`;
}

function renderSideLegend() {
  return `<span><i class="side-line player"></i>${escapeHtml(t("toolbar.player"))}</span><span><i class="side-line opponent"></i>${escapeHtml(t("toolbar.opponent"))}</span>`;
}

function renderHeader() {
  const stats = buildBattleStats();
  const result = state.raw.Winner === "Player" ? t("result.win") : state.raw.Winner === "Opponent" ? t("result.loss") : t("result.finished");
  const resultClass = state.raw.Winner === "Player" ? "win" : "";
  return `<header class="report-header">
    <div class="matchup">
      ${renderHeroToken("player")}
      <div class="matchup-copy">
        <div class="eyebrow">${escapeHtml(t("header.eyebrow"))}</div>
        <div class="matchup-title">
          <strong>${escapeHtml(state.meta.player.name)}</strong><span class="versus">vs</span><strong class="opponent-name">${escapeHtml(state.meta.opponent.name)}</strong><span class="result-chip ${resultClass}">${result}</span>
        </div>
        <div class="matchup-meta">${escapeHtml(t("header.matchupMeta", { playerHero: state.meta.player.hero, opponentHero: state.meta.opponent.hero, day: state.meta.day, date: formatDate(state.meta.recordedAt) }))}</div>
      </div>
      ${renderHeroToken("opponent")}
    </div>
    <div class="summary-strip">
      <div class="summary-stat"><span>${escapeHtml(t("header.duration"))}</span><strong>${formatTime(state.raw.DurationMs)}</strong></div>
      <div class="summary-stat"><span>${escapeHtml(t("header.damageDealt"))}</span><strong>${formatMetricValue(stats.dealt)}</strong></div>
      <div class="summary-stat"><span>${escapeHtml(t("header.damageTaken"))}</span><strong>${formatMetricValue(stats.taken)}</strong></div>
      <button class="import-button" id="import-button" type="button" title="${escapeHtml(t("header.importTitle"))}">${escapeHtml(t("header.import"))}</button>
      <input class="visually-hidden" id="import-file" type="file" accept=".json,.gz,application/json,application/gzip" />
    </div>
  </header>`;
}

function renderHeroToken(owner) {
  const combatant = owner === "player" ? state.meta.player : state.meta.opponent;
  const asset = owner === "player" ? state.assets?.heroes?.Player : state.assets?.heroes?.Opponent;
  return `<div class="hero-token ${owner}" title="${escapeHtml(combatant.hero)}">${asset ? `<img src="${escapeHtml(asset)}" alt="">` : escapeHtml(combatant.hero.slice(0, 2))}</div>`;
}

function renderToolbar() {
  const tabs = `<div class="report-tabs" data-control="view">
    ${[["timeline","toolbar.timeline"],["stats","toolbar.stats"]].map(([value,key]) => `<button type="button" data-value="${value}" class="${state.view === value ? "active" : ""}">${escapeHtml(t(key))}</button>`).join("")}
  </div>`;
  const localeSwitch = renderLocaleSwitch();
  if (state.view === "stats") return `<nav class="toolbar" aria-label="${escapeHtml(t("toolbar.viewAria"))}">${tabs}<div class="toolbar-spacer"></div><span class="stats-toolbar-note">${escapeHtml(t("toolbar.statsNote"))}</span>${localeSwitch}</nav>`;
  return `<nav class="toolbar" aria-label="${escapeHtml(t("toolbar.timelineAria"))}">
    ${tabs}
    <div class="toolbar-group"><span class="toolbar-label">${escapeHtml(t("toolbar.side"))}</span><div class="segmented" data-control="scope">
      ${[["all","toolbar.all"],["player","toolbar.player"],["opponent","toolbar.opponent"]].map(([value,key]) => `<button type="button" data-value="${value}" class="${state.scope === value ? "active" : ""}">${escapeHtml(t(key))}</button>`).join("")}
    </div></div>
    <div class="toolbar-group"><span class="toolbar-label">${escapeHtml(t("toolbar.events"))}</span><div class="family-filters" data-control="family">
      ${[["all","toolbar.all",COLORS.system],["damage","toolbar.damage",COLORS.damage],["heal","toolbar.heal",COLORS.heal],["status","toolbar.status",COLORS.status],["skill","toolbar.skill",COLORS.skill]].map(([value,key,color]) => `<button type="button" data-value="${value}" class="${state.family === value ? "active" : ""}"><i class="family-dot" style="--dot:${color}"></i>${escapeHtml(t(key))}</button>`).join("")}
    </div></div>
    <div class="zoom-actions">
      <button class="icon-button" id="zoom-out" type="button" title="${escapeHtml(t("toolbar.zoomOut"))}">−</button>
      <button class="icon-button" id="reset-view" type="button" title="${escapeHtml(t("toolbar.resetZoom"))}">↺</button>
      <button class="icon-button" id="zoom-in" type="button" title="${escapeHtml(t("toolbar.zoomIn"))}">＋</button>
    </div>
    <div class="toolbar-group state-scale-group"><span class="toolbar-label">${escapeHtml(t("toolbar.axis"))}</span><div class="segmented" data-control="state-scale">
      ${[["linear","toolbar.linear"],["magnitude","toolbar.magnitude"]].map(([value,key]) => `<button type="button" data-value="${value}" class="${state.stateScale === value ? "active" : ""}">${escapeHtml(t(key))}</button>`).join("")}
    </div></div>
    <div class="toolbar-spacer"></div>
    <div class="role-key" aria-label="${escapeHtml(t("toolbar.roleLegend"))}">
      <span title="${escapeHtml(t("role.appliedTitle"))}"><i class="role-dot applied"></i>${escapeHtml(t("role.appliedShort"))}</span>
      <span title="${escapeHtml(t("role.receivedTitle"))}"><i class="role-dot received"></i>${escapeHtml(t("role.receivedShort"))}</span>
      <span title="${escapeHtml(t("role.triggerTitle"))}"><i class="role-dot trigger"></i>${escapeHtml(t("role.triggerShort"))}</span>
    </div>
    <div class="status-key" aria-label="${escapeHtml(t("toolbar.statusLegend"))}">${["Haste","Slow","Freeze"].map(renderStatusGlyph).join("")}</div>
    ${localeSwitch}
  </nav>`;
}

function renderLocaleSwitch() {
  return `<div class="segmented locale-switch" data-control="locale" aria-label="${escapeHtml(t("toolbar.locale"))}">${I18N.supportedLocales.map((locale) => `<button type="button" data-value="${locale}" class="${state.locale === locale ? "active" : ""}" title="${escapeHtml(locale)}">${escapeHtml(I18N.localeNames[locale] || locale)}</button>`).join("")}</div>`;
}

function renderStatusGlyph(status) {
  const asset = state.assets?.status?.[status] || window.BATTLE_ASSETS?.status?.[status];
  return `<span class="status-glyph" title="${escapeHtml(t(`status.${status}`, {}, status))}">${asset ? `<img src="${escapeHtml(asset)}" alt="">` : status.slice(0, 1)}</span>`;
}

function renderDetail() {
  const event = state.selected;
  if (!event && state.selectedFrame) return renderFrameDetail(state.selectedFrame);
  if (!event) return `<div class="detail-empty"><div class="empty-symbol">◇</div><strong>${escapeHtml(t("detail.emptyTitle"))}</strong><span>${escapeHtml(t("detail.emptyBody"))}</span></div>`;
  const description = describeEvent(event);
  const isStateChange = event.kind === "player-state";
  const isStatusInterval = event.kind === "status-interval";
  const isCardAttribute = event.kind === "card-attribute";
  const isSkillStateTrigger = event.kind === "skill-trigger" && Boolean(event.stateMetric);
  const source = event.source ? getLane(event.source) : null;
  const trigger = event.triggerSource ? getLane(event.triggerSource) : null;
  const role = roleLabel(state.selectedRole);
  const detailKind = isStatusInterval ? t("detail.statusInterval") : isStateChange ? t("detail.numericState") : isCardAttribute ? t("detail.attributeChange") : event.kind === "skill-trigger" ? t("detail.skillEvent") : t("detail.combatEvent");
  const targetNames = formatList(event.targets.map((id) => getLane(id).name)) || t("common.systemSettlement");
  const originIds = [...new Set([...(event.triggerSources || []), event.triggerSource, ...(event.sources || []), event.source].filter(Boolean))];
  const isSettlementOnly = ["health", "death"].includes(event.kind) && originIds.length === 0;
  const originParts = originIds.map((id) => getLane(id).name);
  if (isStateChange) originParts.push(...stateChangeCauseLabels(event));
  const originName = formatList(originParts) || trigger?.name || source?.name || (isStateChange ? stateChangeCauseLabel(event) : isSettlementOnly ? t("common.systemSettlement") : t("common.unknownSource"));
  const originLabel = isStatusInterval
    ? t("detail.applicationSource")
    : isStateChange
    ? t(event.sourceConfidence === "direct" ? "detail.source" : event.sourceConfidence === "related" ? "detail.relatedSource" : "detail.settlement")
    : isCardAttribute
    ? t(event.sourceConfidence === "direct" ? "detail.source" : "detail.relatedSource")
    : isSettlementOnly
    ? t("detail.settlement")
    : t("detail.source");
  const fields = isStatusInterval
    ? `<div class="detail-field"><span>${escapeHtml(t("detail.duration"))}</span><strong>${formatTime(event.durationMs)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.applications"))}</span><strong>${formatMetricValue(event.applications.length)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.sourceCount"))}</span><strong>${formatMetricValue(event.sources.length)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.relatedRecords"))}</span><strong>${formatMetricValue(event.count || 1)}</strong></div>`
    : isStateChange
    ? `<div class="detail-field"><span>${escapeHtml(t("detail.before"))}</span><strong>${formatMetricValue(event.previousValue)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.after"))}</span><strong>${formatMetricValue(event.currentValue)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.delta"))}</span><strong>${formatSigned(event.amount)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.relatedRecords"))}</span><strong>${formatMetricValue(event.count || 1)}</strong></div>`
    : isCardAttribute
    ? `${event.attributeChanges.map((change) => `<div class="detail-field"><span>${escapeHtml(cardAttributeLabel(change.attribute))}</span><strong>${escapeHtml(formatMetricValue(change.previousValue))} → ${escapeHtml(formatMetricValue(change.currentValue))} (${escapeHtml(formatSigned(change.amount))})</strong></div>`).join("")}
      <div class="detail-field"><span>${escapeHtml(t("detail.changeCount"))}</span><strong>${formatMetricValue(event.attributeChanges.length)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.relatedRecords"))}</span><strong>${formatMetricValue(event.count || 1)}</strong></div>`
    : isSkillStateTrigger
    ? `<div class="detail-field"><span>${escapeHtml(t("detail.executingEntity"))}</span><strong>${escapeHtml(source?.name || "—")}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("state.change", { metric: metricLabel(event.stateMetric) }))}</span><strong>${escapeHtml(skillStateResultLabel(event))}</strong></div>
      ${event.stateResult === "changed" ? `<div class="detail-field"><span>${escapeHtml(t("tooltip.beforeAfter"))}</span><strong>${escapeHtml(formatMetricValue(event.previousValue))} → ${escapeHtml(formatMetricValue(event.currentValue))}</strong></div>` : ""}
      <div class="detail-field"><span>${escapeHtml(t("detail.relatedRecords"))}</span><strong>${formatMetricValue(event.raw.length)}</strong></div>`
    : `<div class="detail-field"><span>${escapeHtml(t("detail.executingEntity"))}</span><strong>${escapeHtml(source?.name || "—")}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.mergedRecords"))}</span><strong>${formatMetricValue(event.count || 1)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.value"))}</span><strong>${event.amount == null ? "—" : formatSigned(event.amount)}</strong></div>
      <div class="detail-field"><span>${escapeHtml(t("detail.critical"))}</span><strong>${escapeHtml(event.isCrit ? t("common.yes") : t("common.no"))}</strong></div>`;
  return `${state.selectedFrame ? `<button class="detail-back" id="detail-back" type="button">← ${escapeHtml(t("detail.backToFrame", { time: formatTime(state.selectedFrame.atMs), count: state.selectedFrame.events.length }))}</button>` : ""}
    <button class="detail-close" id="detail-close" type="button" aria-label="${escapeHtml(t("detail.close"))}">×</button>
    <div class="detail-kicker">${role ? `${role} · ` : ""}${detailKind}</div>
    <div class="detail-time">${formatTime(event.atMs)}</div>
    <h2 class="detail-title" style="color:${COLORS[description.family]}">${escapeHtml(description.label)}</h2>
    <div class="detail-summary">${escapeHtml(description.summary)}</div>
    <div class="causal-chain">
      <div class="causal-node"><span>${escapeHtml(originLabel)}</span><strong title="${escapeHtml(originName)}">${escapeHtml(originName)}</strong></div>
      <div class="causal-arrow">→</div>
      <div class="causal-node"><span>${escapeHtml(t("detail.affected"))}</span><strong>${escapeHtml(targetNames)}</strong></div>
    </div>
    <div class="detail-fields">${fields}</div>
    <details class="raw-details"><summary>${escapeHtml(t("detail.rawData"))}</summary><pre>${escapeHtml(JSON.stringify(event.raw, null, 2))}</pre></details>`;
}

function renderFrameDetail(frame) {
  const sections = [
    ["combat", t("detail.frameCombat"), frame.events.filter((event) => !["player-state", "status-interval"].includes(event.kind))],
    ["state", t("detail.frameState"), frame.events.filter((event) => event.kind === "player-state")],
    ["status", t("detail.frameStatus"), frame.events.filter((event) => event.kind === "status-interval")],
  ].filter(([, , events]) => events.length);
  return `<button class="detail-close" id="detail-close" type="button" aria-label="${escapeHtml(t("detail.close"))}">×</button>
    <div class="detail-kicker">${escapeHtml(t("detail.frameKicker", { frame: formatMetricValue(frame.frame) }))}</div>
    <div class="detail-time">${formatTime(frame.atMs)}</div>
    <h2 class="detail-title">${escapeHtml(tp("count.frameEvents", frame.events.length))}</h2>
    <div class="detail-summary">${escapeHtml(t("detail.frameSummary"))}</div>
    <div class="frame-overview-counts"><span>${escapeHtml(tp("count.reportEvents", frame.events.length))}</span><span>${escapeHtml(tp("count.rawFrameRecords", frame.raw.length))}</span></div>
    <div class="frame-event-groups">${sections.map(([key, label, events]) => `<section class="frame-event-group ${key}">
      <header><strong>${escapeHtml(label)}</strong><span>${formatMetricValue(events.length)}</span></header>
      <div class="frame-event-list">${events.map(renderFrameEventRow).join("")}</div>
    </section>`).join("")}</div>`;
}

function renderFrameEventRow(event) {
  const description = describeEvent(event);
  const icon = frameEventIcon(event);
  const path = frameEventPath(event);
  const value = frameEventValue(event);
  const role = frameEventRole(event);
  return `<button class="frame-event-row" type="button" data-frame-event-id="${escapeHtml(event.id)}" data-frame-event-role="${escapeHtml(role || "")}" style="--event-color:${COLORS[description.family] || COLORS.system}">
    <span class="frame-event-icon">${icon ? `<img src="${escapeHtml(icon)}" alt="">` : `<i>${escapeHtml(eventMarkerGlyph(event))}</i>`}</span>
    <span class="frame-event-copy"><strong>${escapeHtml(description.label)}</strong><small>${escapeHtml(path)}</small></span>
    ${value ? `<span class="frame-event-value">${escapeHtml(value)}</span>` : ""}
    <span class="frame-event-chevron" aria-hidden="true">›</span>
  </button>`;
}

function frameEventIcon(event) {
  if (event.kind === "status-interval") return state.assets?.status?.[event.status] || null;
  if (event.kind === "player-state") return sharedEventIcon(PLAYER_STATE_METRICS[event.metric]?.iconKey);
  return eventNativeIcon(event);
}

function eventMarkerGlyph(event) {
  return event.kind === "skill-trigger" ? "◆" : event.kind === "card-attribute" ? "↕" : "•";
}

function frameEventPath(event) {
  const originIds = [...new Set([...(event.triggerSources || []), event.triggerSource, ...(event.sources || []), event.source].filter(Boolean))];
  const origins = originIds.map((id) => getLane(id).name);
  if (event.kind === "player-state") origins.push(...stateChangeCauseLabels(event));
  const origin = formatList(origins) || t("common.systemSettlement");
  const target = formatList((event.targets || []).map((id) => getLane(id).name)) || t("common.systemState");
  return `${origin} → ${target}`;
}

function frameEventValue(event) {
  if (event.kind === "health" || event.kind === "player-state") return formatSigned(event.amount);
  if (event.kind === "status-interval") return formatTime(event.durationMs);
  if (event.kind === "card-attribute") return tp("count.changes", event.attributeChanges.length);
  if (event.kind === "skill-trigger" && event.stateMetric) return skillStateResultLabel(event);
  if ((event.count || 1) > 1) return `×${formatMetricValue(event.count)}`;
  return "";
}

function frameEventRole(event) {
  if (event.kind === "skill-trigger") return "trigger";
  if (event.source || (event.sources || []).length) return "applied";
  if ((event.targets || []).length) return "received";
  return null;
}

function stateChangeCauseLabels(event) {
  const metric = metricLabel(event.metric);
  return (event.causeKeys || []).map((key) => t(key, { metric }));
}

function stateChangeCauseLabel(event) {
  const labels = stateChangeCauseLabels(event);
  if (labels.length) return formatList(labels);
  if (event.metric === "Rage" && event.amount < 0 && event.causes.some((cause) => cause.Action === "Enraged")) return t("cause.rageEnded");
  if (event.metric === "Shield" && event.amount < 0) return t("cause.shieldDamage");
  return t("cause.battleState");
}

function buildBattleStats() {
  return { dealt: state.stats.output.player.damage, taken: state.stats.output.opponent.damage };
}

function buildBattleStatistics(rawEvents) {
  const emptySide = () => ({
    damage: 0,
    healing: 0,
    shield: 0,
    damageTypes: { Damage: 0, Burn: 0, Poison: 0, Other: 0 },
    tempo: { Charge: 0, Haste: 0, Slow: 0, Freeze: 0 },
  });
  const output = { player: emptySide(), opponent: emptySide() };
  const opposite = (owner) => owner === "player" ? "opponent" : "player";
  const ownerOfTarget = (id) => id === "player:Player" ? "player" : id === "player:Opponent" ? "opponent" : null;

  for (const event of rawEvents) {
    if (event.Kind === "health") {
      const owner = ownerOfTarget((event.Targets || [])[0]);
      const amount = Number(event.Amount) || 0;
      if (!owner || !amount) continue;
      const subtype = event.Action.split(":")[1] || "Other";
      if (amount < 0) {
        const dealer = opposite(owner);
        const damageType = DAMAGE_TYPE_KEYS.includes(subtype) ? subtype : "Other";
        output[dealer].damage += Math.abs(amount);
        output[dealer].damageTypes[damageType] += Math.abs(amount);
      } else if (subtype === "Heal" || subtype === "Regen") {
        output[owner].healing += amount;
      } else if (subtype === "Shield") {
        output[owner].shield += amount;
      }
      continue;
    }
    const tempo = event.Kind === "effect-executed" ? TEMPO_ACTIONS[event.Action] : null;
    const sourceOwner = tempo ? state.laneById.get(event.Source)?.owner : null;
    if (tempo && (sourceOwner === "player" || sourceOwner === "opponent")) output[sourceOwner].tempo[tempo] += 1;
  }

  return { output };
}

function bindControls() {
  document.querySelectorAll("[data-control='locale'] button").forEach((button) => {
    button.addEventListener("click", () => setLocale(button.dataset.value));
  });
  document.querySelectorAll("[data-control='view'] button").forEach((button) => {
    button.addEventListener("click", () => {
      if (state.view === button.dataset.value) return;
      state.view = button.dataset.value;
      state.selected = null;
      state.selectedFrame = null;
      state.selectedRole = null;
      render();
    });
  });
  document.querySelectorAll("[data-control='state-scale'] button").forEach((button) => {
    button.addEventListener("click", () => {
      if (state.stateScale === button.dataset.value) return;
      state.stateScale = button.dataset.value;
      render();
    });
  });
  document.querySelectorAll("[data-control='scope'] button").forEach((button) => {
    button.addEventListener("click", () => {
      if (state.scope === button.dataset.value) return;
      state.scope = button.dataset.value;
      render();
    });
  });
  document.querySelectorAll("[data-control='family'] button").forEach((button) => {
    button.addEventListener("click", () => {
      if (state.family === button.dataset.value) return;
      state.family = button.dataset.value;
      render();
    });
  });
  document.querySelector("#zoom-in")?.addEventListener("click", () => changeZoom(1.25));
  document.querySelector("#zoom-out")?.addEventListener("click", () => changeZoom(0.8));
  document.querySelector("#reset-view")?.addEventListener("click", resetView);
  document.querySelector("#lane-zoom-in")?.addEventListener("click", () => changeLaneScale(1));
  document.querySelector("#lane-zoom-out")?.addEventListener("click", () => changeLaneScale(-1));
  document.querySelector("#lane-zoom-reset")?.addEventListener("click", () => setLaneScaleIndex(DEFAULT_LANE_SCALE_INDEX));
  document.querySelector("#detail-scrim")?.addEventListener("click", closeDetail);
  bindDetailInteractions();

  const importInput = document.querySelector("#import-file");
  document.querySelector("#import-button")?.addEventListener("click", () => importInput?.click());
  importInput?.addEventListener("change", () => {
    const file = importInput.files?.[0];
    if (file) importBattleFile(file);
  });
}

function visibleLanes() {
  return state.lanes.filter((lane) => state.scope === "all" || lane.owner === state.scope);
}

function visibleStateMetrics() {
  return state.stateMetrics.map((metric) => ({
    ...metric,
    tracks: metric.tracks.filter((track) => state.scope === "all" || track.owner === state.scope),
  })).filter((metric) => metric.tracks.length);
}

function visibleEvents() {
  return state.events.filter((event) => {
    if (STATE_APPLY_ACTIONS.has(event.action) && !event.stateMetric) return false;
    if (state.scope !== "all") {
      const involved = [event.source, event.triggerSource, ...event.targets].filter(Boolean);
      if (!involved.some((id) => getLane(id).owner === state.scope)) return false;
    }
    const family = describeEvent(event).family;
    if (state.family === "all") return true;
    if (state.family === "damage") return family === "damage" || family === "burn";
    if (state.family === "heal") return family === "heal" || family === "charge";
    if (state.family === "status") return family === "status" || family === "buff";
    if (state.family === "skill") return event.kind === "skill-trigger";
    return true;
  });
}

function renderLaneList(lanes) {
  const list = document.querySelector("#entity-lane-list");
  if (!list) return;
  list.innerHTML = lanes.map((lane, index) => {
    const previous = lanes[index - 1];
    const ownerBreak = previous && previous.owner !== lane.owner ? "owner-break" : "";
    const initials = lane.type === "hero" ? lane.detail.slice(0, 2) : lane.type === "skill" ? "SK" : lane.name.slice(0, 2);
    const sizeLabel = lane.size ? t(`size.${lane.size}`, {}, lane.size) : null;
    const itemPreviewUnavailable = lane.type === "item" && state.assets?.cardPreviewMode !== "native-final";
    const artTitle = [lane.name, sizeLabel, itemPreviewUnavailable ? t("entity.previewUnavailable") : null].filter(Boolean).join(" · ");
    const art = itemPreviewUnavailable
      ? '<span class="native-preview-placeholder" aria-hidden="true"><i></i></span>'
      : lane.asset ? `<img src="${escapeHtml(lane.asset)}" alt="">` : escapeHtml(initials);
    const ownerColor = lane.owner === "player" ? "var(--player)" : "var(--opponent)";
    const artAccent = TIER_COLORS[lane.tier] || ownerColor;
    return `<div class="lane-row ${lane.owner} ${lane.type} ${ownerBreak}" data-lane-id="${escapeHtml(lane.id)}" data-lane-index="${index}" data-entity-type="${escapeHtml(lane.type)}" data-item-size="${escapeHtml(lane.size || "")}" data-tier="${escapeHtml(lane.tier || "")}" style="--owner-color:${ownerColor};--art-accent:${artAccent}">
      <div class="lane-art${itemPreviewUnavailable ? " native-preview-pending" : ""}" style="--item-span:${lane.span}" title="${escapeHtml(artTitle)}">${art}</div>
      <div class="lane-copy"><strong>${escapeHtml(lane.name)}</strong><span>${escapeHtml(lane.detail)}</span></div>
      <i class="owner-mark"></i>
    </div>`;
  }).join("");
}

function renderStateLaneList(metrics) {
  const list = document.querySelector("#state-lane-list");
  if (!list) return;
  list.innerHTML = `<div class="state-overview">
    <div class="state-overview-head"><strong>${escapeHtml(t("state.title"))}</strong><span>${escapeHtml(t(state.stateScale === "magnitude" ? "state.magnitudeAxis" : "state.linearAxis"))}</span></div>
    <div class="state-side-key" aria-label="${escapeHtml(t("state.sideLegend"))}"><span><i class="side-line player"></i>${escapeHtml(t("toolbar.player"))}</span><span><i class="side-line opponent"></i>${escapeHtml(t("toolbar.opponent"))}</span></div>
    <div class="state-metric-list">${metrics.map((metric) => `<div class="state-metric" data-state-metric="${metric.metric}" aria-label="${escapeHtml(t("state.metricAria", { metric: metricLabel(metric) }))}" style="--metric-color:${COLORS[metric.family]}">
      <span class="state-glyph">${renderStateMetricGlyph(metric)}</span>
      <strong>${escapeHtml(metricLabel(metric))}</strong>
      <span class="state-readout">${renderStateMetricValues(metric)}</span>
    </div>`).join("")}</div>
  </div>`;
}

function renderStateMetricGlyph(metric) {
  const asset = sharedEventIcon(metric.iconKey);
  return asset ? `<img src="${escapeHtml(asset)}" alt="">` : escapeHtml(metric.glyph);
}

function renderStateMetricValues(metric, atMs = null) {
  return ["player", "opponent"].map((owner) => {
    const track = metric.tracks.find((candidate) => candidate.owner === owner);
    const value = track ? valueAtTime(track, atMs ?? state.viewStartMs) : 0;
    return `<span class="${owner}">${escapeHtml(t(owner === "player" ? "state.playerShort" : "state.opponentShort"))} ${formatCompactValue(value)}</span>`;
  }).join("");
}

function buildEventPlacements(events, lanes) {
  const laneIds = new Set(lanes.map((lane) => lane.id));
  const placements = [];
  const seen = new Set();
  const add = (laneId, event, role) => {
    if (!laneIds.has(laneId)) return;
    const key = `${laneId}|${event.id}`;
    if (seen.has(key)) return;
    seen.add(key);
    placements.push({ laneId, eventId: event.id, atMs: event.atMs, role });
  };
  for (const event of events) {
    if (event.kind === "card-attribute") {
      for (const target of event.targets || []) add(target, event, "received");
      continue;
    }
    const source = event.source && laneIds.has(event.source) ? event.source : null;
    const target = (event.targets || []).find((id) => laneIds.has(id));
    const primary = source || target;
    if (primary) add(primary, event, eventRoleForLane(event, primary));
  }
  return spreadCollidingPlacements(placements);
}

function spreadCollidingPlacements(placements) {
  const layout = laneLayout();
  const groups = new Map();
  for (const placement of placements) {
    const key = `${placement.laneId}|${placement.atMs}`;
    if (!groups.has(key)) groups.set(key, []);
    groups.get(key).push(placement);
  }
  for (const group of groups.values()) {
    const count = group.length;
    const step = count <= 1 ? 0 : Math.min(layout.collisionStep, Math.max(2, (layout.rowHeight - 12) / (count - 1)));
    group.forEach((placement, index) => {
      placement.collisionCount = count;
      placement.collisionIndex = index;
      placement.offsetY = (index - (count - 1) / 2) * step;
    });
  }
  return placements;
}

function eventRoleForLane(event, laneId) {
  if (!laneId) return null;
  if ((event.targets || []).includes(laneId)) return "received";
  if (event.kind === "skill-trigger") return "trigger";
  if (event.source === laneId || (event.sources || []).includes(laneId)) return "applied";
  if (event.triggerSource === laneId || (event.triggerSources || []).includes(laneId)) return "trigger";
  return null;
}

function mountTimeline() {
  const container = document.querySelector("#combat-chart");
  const stateContainer = document.querySelector("#state-chart");
  const scroll = document.querySelector("#timeline-scroll");
  const content = document.querySelector("#timeline-content");
  const chartStage = document.querySelector("#chart-stage");
  if (!container || !stateContainer || !scroll || !content || !chartStage || !window.echarts?.init) return;
  const lanes = visibleLanes();
  const stateMetrics = visibleStateMetrics();
  const events = visibleEvents();
  const layout = laneLayout();
  applyLaneLayoutVariables(layout);
  const laneIds = new Set(lanes.map((lane) => lane.id));
  const intervals = state.intervals.filter((interval) => laneIds.has(interval.laneId));
  const placements = buildEventPlacements(events, lanes);
  const rowHeight = layout.rowHeight;
  const chartHeight = Math.max(rowHeight, lanes.length * rowHeight);
  const stateHeight = stateMetrics.length ? STATE_CHART_HEIGHT : 0;
  const timelineWidth = timelineWidthPx();
  const tickStep = chooseTimeTickStep();
  content.style.setProperty("--timeline-width", `${timelineWidth}px`);
  content.style.setProperty("--state-height", `${stateHeight}px`);
  stateContainer.style.height = `${stateHeight}px`;
  stateContainer.hidden = stateHeight === 0;
  container.style.height = `${chartHeight}px`;
  document.querySelector("#lane-list").style.height = `${stateHeight + chartHeight}px`;
  document.querySelector("#entity-lane-list").style.height = `${chartHeight}px`;
  renderStateLaneList(stateMetrics);
  renderLaneList(lanes);
  renderTimeRuler(timelineWidth, tickStep);
  bindTimelineScroll(scroll);
  bindTimelineCursor(chartStage);

  if (stateHeight > 0) {
    stateChart = window.echarts.init(stateContainer, null, {
      renderer: "canvas",
      useDirtyRect: true,
      devicePixelRatio: Math.min(devicePixelRatio || 1, 2),
    });
  }
  combatChart = window.echarts.init(container, null, {
    renderer: "canvas",
    useDirtyRect: true,
    devicePixelRatio: Math.min(devicePixelRatio || 1, 2),
  });
  if (stateChart) stateChart.setOption(buildStateChartOption(stateMetrics, tickStep), { notMerge: true, lazyUpdate: false });

  const option = {
    animation: false,
    backgroundColor: "transparent",
    grid: { left: 0, right: 0, top: 0, bottom: 0 },
    tooltip: {
      trigger: "item",
      renderMode: "html",
      confine: true,
      backgroundColor: "transparent",
      borderWidth: 0,
      padding: 0,
      extraCssText: "box-shadow:none;pointer-events:none;",
      position: positionCombatTooltip,
      formatter: formatTooltip,
    },
    xAxis: {
      type: "value",
      min: 0,
      max: state.raw.DurationMs,
      axisLabel: { show: false },
      axisLine: { show: false },
      axisTick: { show: false },
      interval: tickStep,
      splitLine: { show: true, lineStyle: { color: "rgba(184,202,228,.075)" } },
    },
    yAxis: {
      type: "category",
      inverse: true,
      data: lanes.map((lane) => lane.id),
      axisLabel: { show: false },
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: true, lineStyle: { color: "rgba(184,202,228,.10)" } },
      splitArea: { show: true, areaStyle: { color: ["rgba(255,255,255,.006)", "rgba(255,255,255,.018)"] } },
    },
    series: [buildStatusSeries(intervals), buildEventSeries(placements, "rich"), buildEventIconSeries(placements, "rich"), buildEventHitSeries(placements)],
  };
  combatChart.setOption(option, { notMerge: true, lazyUpdate: false });

  bindChartEventInteractions(combatChart);
  if (stateChart) bindChartEventInteractions(stateChart);

  scroll.scrollLeft = timeToPixel(state.viewStartMs);
  updateVisibleRange(scroll);
  if (state.selected) applyEventFocus(state.selected, true);
  else if (state.selectedFrame) updateTimelineCursor(state.selectedFrame.atMs);
}

function mountStats() {
  if (!window.echarts?.init) return;
  const mount = (id, option) => {
    const element = document.querySelector(`#${id}`);
    if (!element) return;
    const chart = window.echarts.init(element, null, {
      renderer: "canvas",
      useDirtyRect: true,
      devicePixelRatio: Math.min(devicePixelRatio || 1, 2),
    });
    chart.setOption(option, { notMerge: true, lazyUpdate: false });
    statsCharts.push(chart);
  };
  mount("output-chart", buildOutputStatsOption());
  if (activeDamageTypes().length > 1) mount("damage-chart", buildDamageStatsOption());
  mount("tempo-chart", buildTempoStatsOption());
  const dashboard = document.querySelector(".stats-dashboard");
  if (dashboard && typeof ResizeObserver === "function") {
    statsResizeObserver = new ResizeObserver(() => {
      requestAnimationFrame(() => statsCharts.forEach((chart) => chart.resize()));
    });
    statsResizeObserver.observe(dashboard);
  }
}

function buildOutputStatsOption() {
  const categories = [t("toolbar.damage"), t("toolbar.heal"), t("metric.Shield")];
  const valueKeys = ["damage", "healing", "shield"];
  const sideSeries = ["player", "opponent"].map((owner) => ({
    name: t(owner === "player" ? "toolbar.player" : "toolbar.opponent"),
    type: "bar",
    barMaxWidth: 24,
    data: valueKeys.map((key) => magnitudeBarDatum(state.stats.output[owner][key], owner)),
    itemStyle: { color: OWNER_COLORS[owner], borderRadius: [0, 4, 4, 0], opacity: owner === "player" ? .9 : .72 },
    label: { show: true, position: "right", color: "#aebbd0", fontSize: 11, formatter: (params) => formatCompactValue(params.data.rawValue) },
  }));
  return buildGroupedBarOption(categories, sideSeries, formatOutputStatsTooltip);
}

function buildDamageStatsOption() {
  const types = activeDamageTypes();
  const owners = ["player", "opponent"];
  const totals = Object.fromEntries(owners.map((owner) => [owner, types
    .reduce((sum, type) => sum + state.stats.output[owner].damageTypes[type], 0)]));
  return {
    animation: false,
    backgroundColor: "transparent",
    grid: { left: 72, right: 20, top: 14, bottom: 30 },
    tooltip: commonStatsTooltip((params) => {
      const entries = Array.isArray(params) ? params : [params];
      return `${entries[0]?.axisValue || ""}\n${entries.map((entry) => `${entry.seriesName}  ${formatMetricValue(entry.data.rawValue)} · ${formatPercent(entry.value)}%`).join("\n")}`;
    }, "axis"),
    xAxis: {
      type: "value",
      min: 0,
      max: 100,
      axisLabel: { color: "#8190a6", fontSize: 11, formatter: (value) => `${value}%` },
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: true, lineStyle: { color: "rgba(184,202,228,.09)" } },
    },
    yAxis: {
      type: "category",
      inverse: true,
      data: owners.map((owner) => t(owner === "player" ? "toolbar.player" : "toolbar.opponent")),
      axisLabel: { color: "#aebbd0", fontSize: 11 },
      axisLine: { show: false },
      axisTick: { show: false },
    },
    series: types.map((type) => ({
      name: t(`damage.${type}`, {}, type),
      type: "bar",
      stack: "damage-total",
      barMaxWidth: 32,
      data: owners.map((owner) => {
        const rawValue = state.stats.output[owner].damageTypes[type];
        return { value: totals[owner] > 0 ? rawValue / totals[owner] * 100 : 0, rawValue, owner };
      }),
      itemStyle: { color: DAMAGE_TYPE_COLORS[type] },
      label: {
        show: true,
        position: "inside",
        color: "#07101a",
        fontSize: 11,
        fontWeight: 700,
        formatter: (params) => params.value >= 8 ? `${formatPercent(params.value)}%` : "",
      },
    })),
  };
}

function buildTempoStatsOption() {
  const tempoKeys = TEMPO_KEYS;
  const sideSeries = ["player", "opponent"].map((owner) => ({
    name: t(owner === "player" ? "toolbar.player" : "toolbar.opponent"),
    type: "bar",
    barMaxWidth: 18,
    data: tempoKeys.map((key) => rawBarDatum(state.stats.output[owner].tempo[key], owner)),
    itemStyle: { color: OWNER_COLORS[owner], borderRadius: [0, 4, 4, 0], opacity: owner === "player" ? .9 : .72 },
    label: { show: true, position: "right", color: "#aebbd0", fontSize: 11, formatter: (params) => params.data.rawValue ? formatCompactValue(params.data.rawValue) : "" },
  }));
  return buildGroupedBarOption(tempoKeys.map((key) => t(`tempo.${key}`, {}, key)), sideSeries, formatTempoStatsTooltip, { magnitude: false });
}

function buildGroupedBarOption(categories, series, tooltipFormatter, { magnitude = true } = {}) {
  return {
    animation: false,
    backgroundColor: "transparent",
    grid: { left: 44, right: 44, top: 12, bottom: 28 },
    tooltip: commonStatsTooltip(tooltipFormatter, "axis"),
    xAxis: {
      type: "value",
      min: 0,
      ...(magnitude ? { interval: 1, max: (range) => Math.max(1, Math.ceil(range.max)) } : { minInterval: 1 }),
      axisLabel: { color: "#8190a6", fontSize: 11, formatter: (value) => formatCompactValue(magnitude ? signedMagnitudeInverse(value) : value) },
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: true, lineStyle: { color: "rgba(184,202,228,.09)" } },
    },
    yAxis: {
      type: "category",
      inverse: true,
      data: categories,
      axisLabel: { color: "#aebbd0", fontSize: 11 },
      axisLine: { show: false },
      axisTick: { show: false },
    },
    series,
  };
}

function magnitudeBarDatum(rawValue, owner) {
  return { value: signedMagnitude(rawValue), rawValue, owner };
}

function rawBarDatum(rawValue, owner) {
  return { value: rawValue, rawValue, owner };
}

function formatOutputStatsTooltip(params) {
  const entries = Array.isArray(params) ? params : [params];
  if (!entries.length) return "";
  return `${entries[0].axisValue}\n${entries.map((entry) => `${entry.seriesName}  ${formatMetricValue(entry.data.rawValue)}`).join("\n")}`;
}

function formatTempoStatsTooltip(params) {
  const entries = Array.isArray(params) ? params : [params];
  if (!entries.length) return "";
  return `${entries[0].axisValue}\n${entries.map((entry) => `${entry.seriesName}  ${tp("count.applications", entry.data.rawValue)}`).join("\n")}`;
}

function commonStatsTooltip(formatter, trigger = "item") {
  return {
    trigger,
    renderMode: "richText",
    confine: true,
    backgroundColor: "#111a28",
    borderColor: "rgba(202,218,240,.28)",
    padding: 9,
    textStyle: { color: "#eef4fc", fontSize: 11 },
    formatter,
  };
}

function buildStateChartOption(metrics, tickStep) {
  const timestamps = [...new Set(metrics.flatMap((metric) => metric.tracks.flatMap((track) => track.points.map((point) => point.atMs))))].sort((left, right) => left - right);
  const series = metrics.flatMap((metric) => metric.tracks.flatMap((track) => {
    const color = COLORS[metric.family];
    const lineId = `state-line-${metric.metric}-${track.owner}`;
    const hitId = `state-hit-${metric.metric}-${track.owner}`;
    return [{
      id: lineId,
      name: `${metricLabel(metric)} · ${t(track.owner === "player" ? "toolbar.player" : "toolbar.opponent")}`,
      type: "line",
      data: timestamps.map((atMs) => {
        const rawValue = valueAtTime(track, atMs);
        return { value: [atMs, stateDisplayValue(rawValue)], rawValue, metric: metric.metric, owner: track.owner, isStateLine: true };
      }),
      showSymbol: false,
      step: "end",
      animation: false,
      silent: true,
      lineStyle: { color, width: track.owner === "player" ? 2 : 1.7, type: track.owner === "player" ? "solid" : "dashed", opacity: track.owner === "player" ? .95 : .72 },
      itemStyle: { color },
      z: 3,
    }, {
      id: hitId,
      type: "scatter",
      data: track.changes.map((change) => ({
        value: [change.atMs, stateDisplayValue(change.currentValue)],
        rawValue: change.currentValue,
        amount: change.amount,
        previousValue: change.previousValue,
        currentValue: change.currentValue,
        metric: change.metric,
        owner: change.owner,
        eventId: change.id,
        role: "received",
      })),
      symbol: "circle",
      symbolSize: 20,
      cursor: "pointer",
      animation: false,
      clip: true,
      tooltip: { show: true, trigger: "item", formatter: formatStateChangeTooltip },
      itemStyle: { color: "rgba(255,255,255,.001)" },
      emphasis: {
        scale: false,
        itemStyle: { color, borderColor: "rgba(255,255,255,.92)", borderWidth: 2, opacity: 1 },
        label: {
          show: true,
          position: track.owner === "player" ? "top" : "bottom",
          distance: 6,
          formatter: (params) => formatSigned(params.data.amount),
          padding: [3, 5],
          borderRadius: 4,
          backgroundColor: "rgba(10,16,26,.96)",
          color: "#eef4fc",
          fontSize: 8,
          fontWeight: 800,
        },
      },
      z: 8,
    }];
  }));
  return {
    animation: false,
    backgroundColor: "transparent",
    grid: { left: 0, right: 0, top: 4, bottom: 4 },
    tooltip: {
      trigger: "axis",
      renderMode: "html",
      confine: true,
      backgroundColor: "transparent",
      borderWidth: 0,
      padding: 0,
      extraCssText: "box-shadow:none;pointer-events:none;",
      axisPointer: { type: "line", lineStyle: { color: "rgba(238,244,252,.36)", width: 1 } },
      position: positionStateTooltip,
      formatter: formatStateTooltip,
    },
    xAxis: {
      type: "value",
      min: 0,
      max: state.raw.DurationMs,
      interval: tickStep,
      axisLabel: { show: false },
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: true, lineStyle: { color: "rgba(184,202,228,.075)" } },
    },
    yAxis: {
      type: "value",
      ...(state.stateScale === "magnitude" ? {
        interval: 1,
        min: (range) => Math.min(0, Math.floor(range.min)),
        max: (range) => Math.max(1, Math.ceil(range.max)),
      } : {
        min: (range) => Math.min(0, range.min),
        max: (range) => Math.max(1, range.max),
      }),
      axisLabel: { inside: true, margin: 4, align: "left", color: "rgba(168,183,204,.56)", fontSize: 7, formatter: formatStateAxisValue },
      axisLine: { show: false },
      axisTick: { show: false },
      splitLine: { show: true, lineStyle: { color: "rgba(184,202,228,.08)" } },
    },
    series,
  };
}

function stateDisplayValue(value) {
  return state.stateScale === "magnitude" ? signedMagnitude(value) : Number(value) || 0;
}

function signedMagnitude(value) {
  const numeric = Number(value) || 0;
  const absolute = Math.abs(numeric);
  return absolute < 1 ? numeric : Math.sign(numeric) * (1 + Math.log10(absolute));
}

function signedMagnitudeInverse(value) {
  const numeric = Number(value) || 0;
  const absolute = Math.abs(numeric);
  return absolute < 1 ? numeric : Math.sign(numeric) * 10 ** (absolute - 1);
}

function formatStateAxisValue(value) {
  return formatCompactValue(state.stateScale === "magnitude" ? signedMagnitudeInverse(value) : value);
}

function formatStateTooltip(params) {
  const lines = (Array.isArray(params) ? params : [params]).filter((param) => param.data?.isStateLine);
  if (!lines.length) return "";
  const atMs = Number(lines[0].value?.[0]) || 0;
  const byMetric = new Map();
  for (const line of lines) {
    if (!byMetric.has(line.data.metric)) byMetric.set(line.data.metric, {});
    byMetric.get(line.data.metric)[line.data.owner] = line.data.rawValue;
  }
  const values = metricsInDisplayOrder(byMetric).map(([metric, owners]) => {
    const label = metricLabel(metric);
    return `<span><i>${escapeHtml(label)}</i><b class="player">${escapeHtml(t("state.playerShort"))} ${escapeHtml(formatMetricValue(owners.player ?? 0))}</b><b class="opponent">${escapeHtml(t("state.opponentShort"))} ${escapeHtml(formatMetricValue(owners.opponent ?? 0))}</b></span>`;
  });
  return `<div class="state-overview-tooltip"><strong>${formatTime(atMs)}</strong>${values.join("")}</div>`;
}

function formatStateChangeTooltip(params) {
  const event = state.eventById.get(params.data?.eventId);
  if (!event || event.kind !== "player-state") return "";
  const metric = PLAYER_STATE_METRICS[event.metric] || { metric: event.metric, family: "system" };
  const label = metricLabel(event.metric);
  const side = t(event.owner === "player" ? "toolbar.player" : "toolbar.opponent");
  const sideClass = event.owner === "player" ? "player" : "opponent";
  const deltaClass = Number(event.amount) >= 0 ? "positive" : "negative";
  return `<div class="combat-tooltip state-change-tooltip" style="--tooltip-accent:${COLORS[metric.family] || COLORS.system}">
    <div class="combat-tooltip-head"><span class="state-tooltip-side ${sideClass}">${escapeHtml(side)}</span><strong>${escapeHtml(t("state.change", { metric: label }))}</strong></div>
    <div class="combat-tooltip-facts">
      <div class="combat-tooltip-fact primary"><span>${escapeHtml(t("tooltip.delta"))}</span><strong class="${deltaClass}">${escapeHtml(formatSigned(event.amount))}</strong></div>
      <div class="combat-tooltip-fact"><span>${escapeHtml(t("tooltip.beforeAfter"))}</span><strong>${escapeHtml(formatMetricValue(event.previousValue))} → ${escapeHtml(formatMetricValue(event.currentValue))}</strong></div>
      <div class="combat-tooltip-fact"><span>${escapeHtml(t("tooltip.time"))}</span><strong>${formatTime(event.atMs)}</strong></div>
    </div>
    ${renderStateChangeTooltipPath(event)}
    ${renderFrameTooltipHint(event)}
  </div>`;
}

function positionStateTooltip(point, params, dom, rect, size) {
  return positionTimelineTooltip(point, size, "#state-chart");
}

function metricsInDisplayOrder(values) {
  return PLAYER_STATE_ORDER.filter((metric) => values.has(metric)).map((metric) => [metric, values.get(metric)]);
}

function buildStatusSeries(intervals) {
  return {
    id: "status-intervals",
    type: "custom",
    coordinateSystem: "cartesian2d",
    silent: false,
    cursor: "pointer",
    clip: true,
    encode: { x: [0, 1], y: 2 },
    data: intervals.map((interval) => ({ value: [interval.startMs, interval.endMs, interval.laneId], status: interval.status, eventId: interval.id, role: "received" })),
    renderItem(params, api) {
      const interval = intervals[params.dataIndex];
      const start = api.coord([api.value(0), api.value(2)]);
      const end = api.coord([api.value(1), api.value(2)]);
      const height = Math.max(12, api.size([0, 1])[1] * .66);
      const shape = window.echarts.graphic.clipRectByRect(
        { x: start[0], y: start[1] - height / 2, width: Math.max(2, end[0] - start[0]), height },
        { x: params.coordSys.x, y: params.coordSys.y, width: params.coordSys.width, height: params.coordSys.height },
      );
      if (!shape) return null;
      const style = STATUS_STYLES[interval.status] || STATUS_STYLES.Freeze;
      const children = [
        { type: "rect", shape, style: { fill: style.fill }, emphasis: { style: { fill: style.hover } } },
        { type: "line", shape: { x1: shape.x, y1: shape.y, x2: shape.x + shape.width, y2: shape.y }, style: { stroke: style.line, lineWidth: 1.5 } },
        { type: "line", shape: { x1: shape.x, y1: shape.y + shape.height, x2: shape.x + shape.width, y2: shape.y + shape.height }, style: { stroke: style.line, lineWidth: 1 } },
      ];
      const icon = state.assets?.status?.[interval.status] || window.BATTLE_ASSETS?.status?.[interval.status];
      if (icon && shape.width >= 20) children.push({ type: "image", style: { image: icon, x: shape.x + 3, y: shape.y + 3, width: height - 6, height: height - 6, opacity: .9 } });
      return { type: "group", children };
    },
  };
}

function buildEventSeries(placements, mode) {
  const markerScale = laneLayout().markerScale;
  return {
    id: "combat-events",
    type: "scatter",
    z: 3,
    clip: true,
    animation: false,
    progressive: 0,
    silent: true,
    symbolKeepAspect: true,
    emphasis: { scale: 1.5, focus: "self" },
    data: placements.map((placement) => {
      const event = state.eventById.get(placement.eventId);
      const description = describeEvent(event);
      const status = statusFromAction(event.action);
      const nativeIcon = eventNativeIcon(event);
      const useImage = mode === "rich" && Boolean(nativeIcon);
      const markerColor = status ? STATUS_STYLES[status].line : COLORS[description.family];
      const isReceived = placement.role === "received";
      const isTrigger = placement.role === "trigger";
      const colliding = placement.collisionCount > 1;
      const unscaledSize = useImage ? (event.kind === "skill-trigger" ? 19 : 17) : event.kind === "skill-trigger" ? 13 : event.kind === "health" ? 10 : status ? 9 : 8;
      const naturalSize = Math.round(unscaledSize * markerScale);
      const baseSize = colliding ? Math.min(naturalSize, 12) : naturalSize;
      return {
        id: `${placement.laneId}|${placement.eventId}`,
        value: [placement.atMs, placement.laneId],
        eventId: placement.eventId,
        role: placement.role,
        symbol: useImage ? (event.kind === "skill-trigger" ? "diamond" : "circle") : event.kind === "skill-trigger" ? "diamond" : event.kind === "health" ? (event.amount < 0 ? "triangle" : "pin") : status ? "roundRect" : "circle",
        symbolRotate: !useImage && event.kind === "health" && event.amount < 0 ? 180 : 0,
        symbolOffset: [0, placement.offsetY || 0],
        symbolSize: isReceived ? baseSize + 2 : baseSize,
        label: (event.count || 1) > 1 ? {
          show: true,
          formatter: `×${event.count}`,
          position: "right",
          distance: 2,
          color: "#eef4fc",
          fontSize: 8,
          fontWeight: 800,
          backgroundColor: "rgba(9,16,26,.92)",
          borderRadius: 4,
          padding: [2, 3],
        } : { show: false },
        itemStyle: {
          color: isReceived ? "#09101a" : isTrigger || !useImage ? markerColor : "rgba(9,16,26,.001)",
          borderColor: isReceived ? markerColor : isTrigger || !useImage ? "rgba(255,255,255,.34)" : "rgba(255,255,255,.001)",
          borderWidth: isReceived ? 2 : isTrigger || !useImage ? .7 : 0,
          opacity: description.family === "system" ? .45 : placement.role === "trigger" ? .72 : .92,
        },
      };
    }),
    markLine: buildPlayhead(state.selected?.atMs ?? state.selectedFrame?.atMs),
  };
}

function buildEventIconSeries(placements, mode) {
  const markerScale = laneLayout().markerScale;
  const icons = mode === "rich" ? placements.flatMap((placement) => {
    const event = state.eventById.get(placement.eventId);
    const icon = eventNativeIcon(event);
    if (!icon) return [];
    const square = event.kind === "skill-trigger";
    const compact = placement.collisionCount > 1;
    const width = Math.round((compact ? 10 : 14) * markerScale);
    const height = Math.round((compact ? (square ? 10 : 12) : square ? 14 : 16) * markerScale);
    return [{ placement, event, icon, width, height }];
  }) : [];
  return {
    id: "combat-event-icons",
    type: "custom",
    coordinateSystem: "cartesian2d",
    z: 4,
    silent: true,
    clip: true,
    encode: { x: 0, y: 1 },
    tooltip: { show: false },
    data: icons.map(({ placement }) => ({ value: [placement.atMs, placement.laneId], eventId: placement.eventId, role: placement.role })),
    renderItem(params, api) {
      const marker = icons[params.dataIndex];
      const point = api.coord([api.value(0), api.value(1)]);
      return {
        type: "image",
        style: {
          image: marker.icon,
          x: point[0] - marker.width / 2,
          y: point[1] - marker.height / 2 + (marker.placement.offsetY || 0),
          width: marker.width,
          height: marker.height,
          opacity: .96,
        },
      };
    },
  };
}

function eventNativeIcon(event) {
  if (!event) return null;
  if (event.kind === "skill-trigger") return getLane(event.source).asset;
  const key = event.kind === "health" ? HEALTH_EVENT_ICON_KEYS[event.action] : EVENT_ACTION_ICON_KEYS[event.action];
  return sharedEventIcon(key);
}

function sharedEventIcon(key) {
  return key ? window.BATTLE_ASSETS?.eventIcons?.[key] : null;
}

function buildEventHitSeries(placements) {
  const hitSize = Math.max(24, Math.round(24 * laneLayout().markerScale));
  return {
    id: "combat-event-hitareas",
    type: "scatter",
    z: 6,
    clip: true,
    animation: false,
    progressive: 0,
    symbol: "circle",
    symbolSize: hitSize,
    cursor: "pointer",
    itemStyle: { color: "rgba(255,255,255,.001)" },
    emphasis: { scale: false, itemStyle: { color: "rgba(255,255,255,.001)" } },
    data: placements.map((placement, index) => ({
      id: `hit-${placement.laneId}|${placement.eventId}`,
      value: [placement.atMs, placement.laneId],
      eventId: placement.eventId,
      role: placement.role,
      visibleDataIndex: index,
      symbolOffset: [0, placement.offsetY || 0],
      symbolSize: [hitSize, hitSize],
    })),
    z: 20,
  };
}

function buildPlayhead(atMs) {
  return {
    silent: true,
    symbol: ["none", "none"],
    label: { show: false },
    lineStyle: { color: atMs == null ? "transparent" : "rgba(238,244,252,.78)", width: 1, type: "dashed" },
    data: [{ xAxis: atMs ?? -1 }],
  };
}

function bindChartEventInteractions(chart) {
  chart.on("mouseover", (params) => {
    const event = state.eventById.get(params.data?.eventId);
    if (!event) return;
    hoveredEventId = event.id;
    clearTimeout(focusClearTimer);
    (chart === combatChart ? stateChart : combatChart)?.dispatchAction({ type: "hideTip" });
    if (chart === combatChart && params.seriesId === "combat-event-hitareas") {
      combatChart.dispatchAction({ type: "highlight", seriesId: "combat-events", dataIndex: params.data.visibleDataIndex });
    }
    applyEventFocus(event, false);
  });
  chart.on("mouseout", (params) => {
    if (!params.data?.eventId) return;
    if (chart === combatChart && params.seriesId === "combat-event-hitareas") {
      combatChart.dispatchAction({ type: "downplay", seriesId: "combat-events", dataIndex: params.data.visibleDataIndex });
    }
    scheduleFocusRestore(params.data.eventId);
  });
  chart.on("globalout", () => scheduleFocusRestore());
  chart.on("click", (params) => {
    const event = state.eventById.get(params.data?.eventId);
    if (!event) return;
    suppressTimelineStageClickUntil = performance.now() + 80;
    const frame = frameRecordForEvent(event);
    if (frame?.events.length > 1) selectFrame(frame);
    else selectEvent(event, params.data?.role || null);
  });
}

function bindTimelineCursor(chartStage) {
  let pendingX = null;
  let scheduled = false;
  document.querySelector("#report-shell")?.addEventListener("pointermove", (event) => {
    const rect = chartStage.getBoundingClientRect();
    const outside = event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom;
    if (!outside) return;
    if (state.video?.timelineHoverActive) {
      state.video.timelineHoverActive = false;
      restoreCommittedVideoFrame();
    }
    if (hoveredEventId) scheduleFocusRestore(hoveredEventId);
  }, { capture: true, passive: true });
  chartStage.addEventListener("pointermove", (event) => {
    if (state.video) state.video.timelineHoverActive = true;
    const rect = chartStage.getBoundingClientRect();
    pendingX = clamp(event.clientX - rect.left, 0, timelineWidthPx());
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      const hoveredEvent = state.eventById.get(hoveredEventId);
      updateTimelineCursor(
        hoveredEvent?.atMs ?? clamp(pendingX / state.pixelsPerSecond * 1000, 0, state.raw.DurationMs),
        { previewVideo: true },
      );
    });
  }, { passive: true });
  chartStage.addEventListener("pointerleave", () => {
    if (state.video) state.video.timelineHoverActive = false;
    restoreCommittedVideoFrame();
    scheduleFocusRestore();
  }, { passive: true });
  chartStage.addEventListener("click", (event) => {
    if (performance.now() < suppressTimelineStageClickUntil) return;
    const rect = chartStage.getBoundingClientRect();
    const hoveredEvent = state.eventById.get(hoveredEventId);
    const combatMs = hoveredEvent?.atMs
      ?? clamp((event.clientX - rect.left) / state.pixelsPerSecond * 1000, 0, state.raw.DurationMs);
    const frame = frameRecordAtTime(combatMs);
    if (frame?.events.length) selectFrame(frame);
    else commitVideoAtCombatTime(combatMs);
  });
}

function scheduleFocusRestore(eventId = null) {
  if (!eventId || hoveredEventId === eventId) hoveredEventId = null;
  combatChart?.dispatchAction({ type: "hideTip" });
  stateChart?.dispatchAction({ type: "hideTip" });
  clearTimeout(focusClearTimer);
  focusClearTimer = setTimeout(restorePinnedFocus, 45);
}

function updateTimelineCursor(atMs, { previewVideo = false } = {}) {
  const crosshair = document.querySelector("#timeline-crosshair");
  if (!crosshair) return;
  crosshair.hidden = false;
  crosshair.style.transform = `translate3d(${timeToPixel(atMs)}px,0,0)`;
  crosshair.classList.toggle("near-end", atMs > state.raw.DurationMs * .88);
  const label = crosshair.querySelector("span");
  if (label) {
    const frame = frameRecordAtTime(atMs);
    label.textContent = frame?.events.length ? `${formatTime(frame.atMs)} · ${tp("count.frameEvents", frame.events.length)}` : formatTime(atMs);
  }
  if (Math.abs(atMs - lastStateReadoutAtMs) >= (state.raw.FrameDurationMs || 50)) {
    lastStateReadoutAtMs = atMs;
    updateStateReadouts(atMs);
  }
  if (previewVideo) requestVideoPreview(atMs);
}

function updateStateReadouts(atMs = null) {
  for (const metric of visibleStateMetrics()) {
    const row = [...document.querySelectorAll(".state-metric")].find((candidate) => candidate.dataset.stateMetric === metric.metric);
    const readout = row?.querySelector(".state-readout");
    if (!readout) continue;
    readout.innerHTML = renderStateMetricValues(metric, atMs ?? state.viewStartMs);
  }
}

function valueAtTime(track, atMs) {
  let low = 0;
  let high = track.points.length - 1;
  while (low <= high) {
    const middle = (low + high) >> 1;
    if (track.points[middle].atMs <= atMs) low = middle + 1;
    else high = middle - 1;
  }
  return track.points[Math.max(0, high)]?.value ?? track.initialValue;
}

function relationRoles(event) {
  const roles = new Map();
  const targets = new Set(event.targets || []);
  const sources = new Set([...(event.sources || []), event.source].filter(Boolean));
  const triggers = new Set([...(event.triggerSources || []), event.triggerSource].filter(Boolean));
  for (const target of targets) roles.set(target, "target");
  for (const source of sources) roles.set(source, targets.has(source) ? "target" : "source");
  for (const trigger of triggers) {
    if (!roles.has(trigger)) roles.set(trigger, "trigger");
  }
  return roles;
}

function applyEventFocus(event, pinned) {
  const roles = relationRoles(event);
  const laneList = document.querySelector("#lane-list");
  const overlay = document.querySelector("#relation-overlay");
  laneList?.classList.toggle("relations-pinned", pinned);
  document.querySelectorAll("[data-lane-id]").forEach((row) => {
    row.classList.remove("relation-trigger", "relation-source", "relation-target");
    const role = roles.get(row.dataset.laneId);
    const ownerMark = row.querySelector(".owner-mark");
    if (role) {
      row.classList.add(`relation-${role}`);
      if (ownerMark) ownerMark.dataset.roleShort = t(role === "source" ? "role.appliedShort" : role === "target" ? "role.receivedShort" : "role.triggerShort");
    } else if (ownerMark) {
      delete ownerMark.dataset.roleShort;
    }
  });
  document.querySelectorAll(".state-metric").forEach((row) => row.classList.toggle("relation-active", row.dataset.stateMetric === event.metric));
  if (overlay) {
    const stateHeight = document.querySelector("#state-chart")?.getBoundingClientRect().height || 0;
    const laneRows = [...document.querySelectorAll(".lane-row")];
    overlay.innerHTML = laneRows.flatMap((row, index) => {
      const role = roles.get(row.dataset.laneId);
      if (!role) return [];
      const height = row.getBoundingClientRect().height;
      return `<i class="relation-band ${role}${pinned ? " pinned" : ""}" style="top:${stateHeight + index * height}px;height:${height}px"></i>`;
    }).join("");
  }
  updateTimelineCursor(event.atMs, { previewVideo: !pinned });
}

function clearEventFocus() {
  hoveredEventId = null;
  document.querySelector("#lane-list")?.classList.remove("relations-pinned");
  document.querySelectorAll("[data-lane-id]").forEach((row) => {
    row.classList.remove("relation-trigger", "relation-source", "relation-target");
    const ownerMark = row.querySelector(".owner-mark");
    if (ownerMark) delete ownerMark.dataset.roleShort;
  });
  document.querySelectorAll(".state-metric").forEach((row) => row.classList.remove("relation-active"));
  const overlay = document.querySelector("#relation-overlay");
  if (overlay) overlay.innerHTML = "";
}

function restorePinnedFocus() {
  clearEventFocus();
  restoreCommittedVideoFrame();
  if (state.selected) {
    applyEventFocus(state.selected, true);
    return;
  }
  if (state.selectedFrame) {
    updateTimelineCursor(state.selectedFrame.atMs);
    return;
  }
  if (state.video?.src) {
    updateTimelineCursor(state.video.committedCombatMs);
    return;
  }
  const crosshair = document.querySelector("#timeline-crosshair");
  if (crosshair) crosshair.hidden = true;
  updateStateReadouts();
}

function selectFrame(frame) {
  if (!frame) return;
  state.selected = null;
  state.selectedFrame = frame;
  state.selectedRole = null;
  clearEventFocus();
  const panel = document.querySelector("#detail-panel");
  if (panel) panel.innerHTML = renderDetail();
  bindDetailInteractions();
  document.querySelector("#report-shell")?.classList.add("details-open");
  commitVideoAtCombatTime(frame.atMs);
  combatChart?.setOption({ series: [{ id: "combat-events", markLine: buildPlayhead(frame.atMs) }] });
  combatChart?.dispatchAction({ type: "downplay", seriesId: "combat-events" });
  stateChart?.dispatchAction({ type: "downplay" });
  updateTimelineCursor(frame.atMs);
}

function selectEvent(event, role = null, { preserveFrame = false } = {}) {
  state.selected = event;
  if (!preserveFrame) state.selectedFrame = null;
  state.selectedRole = role;
  const panel = document.querySelector("#detail-panel");
  if (panel) panel.innerHTML = renderDetail();
  bindDetailInteractions();
  document.querySelector("#report-shell")?.classList.add("details-open");
  commitVideoAtCombatTime(event.atMs);
  combatChart?.setOption({ series: [{ id: "combat-events", markLine: buildPlayhead(event.atMs) }] });
  combatChart?.dispatchAction({ type: "downplay", seriesId: "combat-events" });
  const placementIndex = findPlacementIndex(event.id);
  if (placementIndex >= 0) combatChart?.dispatchAction({ type: "highlight", seriesId: "combat-events", dataIndex: placementIndex });
  highlightStateEvent(event.id);
  applyEventFocus(event, true);
}

function bindDetailInteractions() {
  document.querySelector("#detail-close")?.addEventListener("click", closeDetail);
  document.querySelector("#detail-back")?.addEventListener("click", () => selectFrame(state.selectedFrame));
  document.querySelectorAll("[data-frame-event-id]").forEach((button) => button.addEventListener("click", () => {
    const event = state.eventById.get(button.dataset.frameEventId);
    if (event) selectEvent(event, button.dataset.frameEventRole || null, { preserveFrame: true });
  }));
}

function findPlacementIndex(eventId) {
  const data = combatChart?.getOption()?.series?.find((series) => series.id === "combat-events")?.data || [];
  return data.findIndex((item) => item.eventId === eventId);
}

function highlightStateEvent(eventId) {
  if (!stateChart) return;
  stateChart.dispatchAction({ type: "downplay" });
  for (const series of stateChart.getOption().series || []) {
    if (!String(series.id).startsWith("state-hit-")) continue;
    const dataIndex = (series.data || []).findIndex((item) => item.eventId === eventId);
    if (dataIndex >= 0) stateChart.dispatchAction({ type: "highlight", seriesId: series.id, dataIndex });
  }
}

function closeDetail() {
  state.selected = null;
  state.selectedFrame = null;
  state.selectedRole = null;
  document.querySelector("#report-shell")?.classList.remove("details-open");
  combatChart?.setOption({ series: [{ id: "combat-events", markLine: buildPlayhead(null) }] });
  combatChart?.dispatchAction({ type: "downplay", seriesId: "combat-events" });
  stateChart?.dispatchAction({ type: "downplay" });
  restorePinnedFocus();
}

function timelineWidthPx() {
  return Math.ceil(state.raw.DurationMs / 1000 * state.pixelsPerSecond);
}

function timeToPixel(timeMs) {
  return timeMs / 1000 * state.pixelsPerSecond;
}

function chooseTimeTickStep() {
  return TIME_TICK_STEPS_MS.find((step) => step / 1000 * state.pixelsPerSecond >= 64)
    || TIME_TICK_STEPS_MS.at(-1);
}

function renderTimeRuler(timelineWidth = timelineWidthPx(), tickStep = chooseTimeTickStep()) {
  const ruler = document.querySelector("#time-ruler");
  if (!ruler) return;
  const ticks = [];
  for (let atMs = 0; atMs < state.raw.DurationMs; atMs += tickStep) ticks.push(atMs);
  if (ticks.at(-1) !== state.raw.DurationMs) ticks.push(state.raw.DurationMs);
  ruler.innerHTML = ticks.map((atMs, index) => {
    const edge = index === 0 ? " first" : index === ticks.length - 1 ? " last" : "";
    return `<span class="time-tick${edge}" style="left:${Math.min(timelineWidth, timeToPixel(atMs))}px"><b>${formatTime(atMs)}</b></span>`;
  }).join("");
}

function visibleTimelineWidthPx(scroll) {
  const laneWidth = document.querySelector("#lane-list")?.getBoundingClientRect().width || 0;
  return Math.max(0, scroll.clientWidth - laneWidth);
}

function updateVisibleRange(scroll = document.querySelector("#timeline-scroll")) {
  if (!scroll || !state.raw) return;
  const duration = state.raw.DurationMs;
  const visibleWidth = visibleTimelineWidthPx(scroll);
  state.viewStartMs = clamp(scroll.scrollLeft / state.pixelsPerSecond * 1000, 0, duration);
  state.viewEndMs = clamp((scroll.scrollLeft + visibleWidth) / state.pixelsPerSecond * 1000, state.viewStartMs, duration);
  const label = document.querySelector("#range-label");
  if (label) label.textContent = `${formatTime(state.viewStartMs)} – ${formatTime(state.viewEndMs)}`;
}

function bindTimelineScroll(scroll) {
  let scheduled = false;
  scroll.addEventListener("scroll", () => {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      updateVisibleRange(scroll);
    });
  }, { passive: true });
}

function bindViewportRangeSync() {
  let scheduled = false;
  addEventListener("resize", () => {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      if (state.video?.src && !state.video.expandedTouched) {
        const preferredExpanded = matchMedia("(min-height: 1001px)").matches;
        if (preferredExpanded !== state.video.expanded) setVideoExpanded(preferredExpanded, false);
      }
      applyLaneLayoutVariables();
      updateVisibleRange();
      statsCharts.forEach((chart) => chart.resize());
    });
  }, { passive: true });
}

function changeZoom(factor) {
  applyTimelineScale(state.pixelsPerSecond * factor, false);
}

function changeLaneScale(delta) {
  setLaneScaleIndex(state.laneScaleIndex + delta);
}

function captureLaneScaleAnchor(scroll) {
  if (!scroll) return null;
  const rows = [...document.querySelectorAll("#entity-lane-list .lane-row")];
  if (!rows.length) return null;
  const scrollRect = scroll.getBoundingClientRect();
  const viewportY = scrollRect.top + scroll.clientHeight / 2;
  let best = null;
  let bestDistance = Number.POSITIVE_INFINITY;
  for (const row of rows) {
    const rect = row.getBoundingClientRect();
    const distance = viewportY < rect.top ? rect.top - viewportY : viewportY > rect.bottom ? viewportY - rect.bottom : 0;
    if (distance >= bestDistance) continue;
    bestDistance = distance;
    best = {
      laneId: row.dataset.laneId,
      fraction: clamp((viewportY - rect.top) / Math.max(1, rect.height), 0, 1),
      viewportY,
    };
  }
  return best;
}

function restoreLaneScaleAnchor(scroll, anchor) {
  if (!scroll || !anchor) return;
  const row = [...document.querySelectorAll("#entity-lane-list .lane-row")]
    .find((candidate) => candidate.dataset.laneId === anchor.laneId);
  if (!row) return;
  const rect = row.getBoundingClientRect();
  const targetY = rect.top + rect.height * anchor.fraction;
  scroll.scrollTop += targetY - anchor.viewportY;
}

function updateLaneScaleControls() {
  const layout = laneLayout();
  const out = document.querySelector("#lane-zoom-out");
  const reset = document.querySelector("#lane-zoom-reset");
  const zoomIn = document.querySelector("#lane-zoom-in");
  if (out) out.disabled = state.laneScaleIndex === 0;
  if (reset) reset.textContent = `${layout.percent}%`;
  if (zoomIn) zoomIn.disabled = state.laneScaleIndex === LANE_SCALE_LEVELS.length - 1;
}

function setLaneScaleIndex(nextIndex) {
  const normalized = clamp(Math.round(nextIndex), 0, LANE_SCALE_LEVELS.length - 1);
  if (normalized === state.laneScaleIndex && !laneScaleFrameId) return;
  const scroll = document.querySelector("#timeline-scroll");
  if (!pendingLaneScaleAnchor) pendingLaneScaleAnchor = captureLaneScaleAnchor(scroll);
  state.laneScaleIndex = normalized;
  updateLaneScaleControls();
  if (laneScaleFrameId) return;
  laneScaleFrameId = requestAnimationFrame(() => {
    laneScaleFrameId = null;
    const layout = laneLayout();
    applyLaneLayoutVariables(layout);
    const lanes = visibleLanes();
    const events = visibleEvents();
    const laneIds = new Set(lanes.map((lane) => lane.id));
    const intervals = state.intervals.filter((interval) => laneIds.has(interval.laneId));
    const placements = buildEventPlacements(events, lanes);
    const chartHeight = Math.max(layout.rowHeight, lanes.length * layout.rowHeight);
    const stateHeight = visibleStateMetrics().length ? STATE_CHART_HEIGHT : 0;
    const combatContainer = document.querySelector("#combat-chart");
    if (combatContainer) combatContainer.style.height = `${chartHeight}px`;
    const laneList = document.querySelector("#lane-list");
    const entityList = document.querySelector("#entity-lane-list");
    if (laneList) laneList.style.height = `${stateHeight + chartHeight}px`;
    if (entityList) entityList.style.height = `${chartHeight}px`;
    combatChart?.resize({ height: chartHeight, animation: { duration: 0 } });
    combatChart?.setOption({
      series: [buildStatusSeries(intervals), buildEventSeries(placements, "rich"), buildEventIconSeries(placements, "rich"), buildEventHitSeries(placements)],
    }, { replaceMerge: ["series"], lazyUpdate: false });
    restoreLaneScaleAnchor(scroll, pendingLaneScaleAnchor);
    pendingLaneScaleAnchor = null;
    updateVisibleRange(scroll);
    if (state.selected) applyEventFocus(state.selected, true);
    else if (state.selectedFrame) updateTimelineCursor(state.selectedFrame.atMs);
  });
}

function resetView() {
  applyTimelineScale(DEFAULT_TIME_PIXELS_PER_SECOND, true);
}

function applyTimelineScale(nextPixelsPerSecond, resetToStart) {
  const scroll = document.querySelector("#timeline-scroll");
  const content = document.querySelector("#timeline-content");
  if (!scroll || !content || !combatChart) return;
  const visibleWidth = visibleTimelineWidthPx(scroll);
  const centerMs = (scroll.scrollLeft + visibleWidth / 2) / state.pixelsPerSecond * 1000;
  state.pixelsPerSecond = clamp(nextPixelsPerSecond, MIN_TIME_PIXELS_PER_SECOND, MAX_TIME_PIXELS_PER_SECOND);
  const timelineWidth = timelineWidthPx();
  const tickStep = chooseTimeTickStep();
  content.style.setProperty("--timeline-width", `${timelineWidth}px`);
  renderTimeRuler(timelineWidth, tickStep);
  combatChart.setOption({ xAxis: { interval: tickStep } });
  combatChart.resize({ width: timelineWidth, animation: { duration: 0 } });
  if (stateChart) {
    stateChart.setOption({ xAxis: { interval: tickStep } });
    stateChart.resize({ width: timelineWidth, animation: { duration: 0 } });
  }
  const desiredLeft = resetToStart ? 0 : timeToPixel(centerMs) - visibleWidth / 2;
  scroll.scrollLeft = clamp(desiredLeft, 0, Math.max(0, scroll.scrollWidth - scroll.clientWidth));
  updateVisibleRange(scroll);
  if (state.selected) updateTimelineCursor(state.selected.atMs);
  else if (state.selectedFrame) updateTimelineCursor(state.selectedFrame.atMs);
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

function formatTooltip(params) {
  const event = state.eventById.get(params.data?.eventId);
  if (!event) return "";
  const description = describeEvent(event);
  const role = roleLabel(params.data?.role) || t("common.event");
  const roleClass = ["applied", "received", "trigger"].includes(params.data?.role) ? params.data.role : "neutral";
  const facts = [{ label: t("tooltip.time"), value: formatTime(event.atMs), className: "time" }];
  if (event.kind === "health") {
    const health = healthEventPresentation(event);
    facts.push({ label: health.valueLabel, value: formatMetricValue(Math.abs(Number(event.amount) || 0)), className: "primary" });
  } else if (event.kind === "status-interval") {
    facts.push({ label: t("tooltip.duration"), value: formatTime(event.durationMs), className: "primary" });
    facts.push({ label: t("tooltip.applications"), value: tp("count.applications", event.applications.length), className: "" });
  } else if (event.kind === "card-attribute") {
    facts.push({ label: t("tooltip.changeContent"), value: tp("count.changes", event.attributeChanges.length), className: "primary" });
  } else if (event.kind === "skill-trigger" && event.stateMetric) {
    facts.push({ label: t("state.change", { metric: metricLabel(event.stateMetric) }), value: skillStateResultLabel(event), className: "primary" });
    if (event.stateResult === "changed") {
      facts.push({ label: t("tooltip.beforeAfter"), value: `${formatMetricValue(event.previousValue)} → ${formatMetricValue(event.currentValue)}`, className: "" });
    }
  } else if ((event.count || 1) > 1) {
    facts.push({ label: t("tooltip.mergedRecords"), value: tp("count.mergedRecords", event.count), className: "" });
  }
  const flags = [event.isCrit ? t("tooltip.critical") : null].filter(Boolean);
  const tooltipClass = event.kind === "card-attribute" ? " card-attribute-tooltip" : "";
  const attributeChanges = event.kind === "card-attribute" ? renderCardAttributeTooltipChanges(event) : "";
  return `<div class="combat-tooltip${tooltipClass}" style="--tooltip-accent:${COLORS[description.family] || COLORS.system}">
    <div class="combat-tooltip-head"><span class="combat-tooltip-role ${roleClass}">${escapeHtml(role)}</span><strong>${escapeHtml(description.label)}</strong></div>
    <div class="combat-tooltip-facts">${facts.map((fact) => `<div class="combat-tooltip-fact ${fact.className}"><span>${escapeHtml(fact.label)}</span><strong>${escapeHtml(fact.value)}</strong></div>`).join("")}</div>
    ${attributeChanges}
    ${renderCombatTooltipPath(event)}
    ${flags.length ? `<div class="combat-tooltip-flags">${flags.map((flag) => `<span>${escapeHtml(flag)}</span>`).join("")}</div>` : ""}
    ${renderFrameTooltipHint(event)}
  </div>`;
}

function renderFrameTooltipHint(event) {
  const frame = frameRecordForEvent(event);
  return frame?.events.length > 1
    ? `<div class="combat-tooltip-frame-hint"><i aria-hidden="true">≡</i><span>${escapeHtml(t("tooltip.sameFrame"))}</span><strong>${escapeHtml(t("tooltip.viewFrameEvents", { count: formatMetricValue(frame.events.length) }))}</strong></div>`
    : "";
}

function renderCardAttributeTooltipChanges(event) {
  return `<div class="attribute-change-list">${event.attributeChanges.map((change) => `<div class="attribute-change-row">
    <i>${escapeHtml(cardAttributeLabel(change.attribute))}</i>
    <span>${escapeHtml(formatMetricValue(change.previousValue))} → ${escapeHtml(formatMetricValue(change.currentValue))}</span>
    <strong class="${Number(change.amount) >= 0 ? "positive" : "negative"}">${escapeHtml(formatSigned(change.amount))}</strong>
  </div>`).join("")}</div>`;
}

function positionCombatTooltip(point, params, dom, rect, size) {
  return positionTimelineTooltip(point, size, "#combat-chart");
}

function positionTimelineTooltip(point, size, chartSelector) {
  const contentWidth = size?.contentSize?.[0] || 248;
  const contentHeight = size?.contentSize?.[1] || 132;
  const chart = document.querySelector(chartSelector);
  const scroll = document.querySelector("#timeline-scroll");
  const laneList = document.querySelector("#lane-list");
  const chartRect = chart?.getBoundingClientRect();
  const scrollRect = scroll?.getBoundingClientRect();
  const laneWidth = laneList?.getBoundingClientRect().width || 0;
  const fallbackHeight = chartSelector === "#state-chart" ? STATE_CHART_HEIGHT : chartRect?.height || contentHeight + 8;
  const visibleLeft = chartRect && scrollRect ? Math.max(0, scrollRect.left + laneWidth - chartRect.left) : 0;
  const visibleRight = chartRect && scrollRect ? Math.min(chartRect.width, scrollRect.right - chartRect.left) : chartRect?.width || point[0] + contentWidth + 16;
  const visibleTop = chartRect && scrollRect ? Math.max(0, scrollRect.top - chartRect.top) : 0;
  const visibleBottom = chartRect && scrollRect ? Math.min(chartRect.height, scrollRect.bottom - chartRect.top) : fallbackHeight;
  const gap = 12;
  const spaceRight = visibleRight - point[0];
  const preferredLeft = spaceRight >= contentWidth + gap * 2 ? point[0] + gap : point[0] - contentWidth - gap;
  const maxLeft = Math.max(visibleLeft + 4, visibleRight - contentWidth - 4);
  const maxTop = Math.max(visibleTop + 4, visibleBottom - contentHeight - 4);
  return [
    clamp(preferredLeft, visibleLeft + 4, maxLeft),
    clamp(point[1] - contentHeight / 2, visibleTop + 4, maxTop),
  ];
}

function renderCombatTooltipPath(event) {
  const uniqueIds = (ids) => [...new Set(ids.filter(Boolean))];
  const sourceIds = uniqueIds([...(event.sources || []), event.source]);
  const triggerIds = uniqueIds([...(event.triggerSources || []), event.triggerSource]).filter((id) => !sourceIds.includes(id));
  const targetIds = uniqueIds(event.targets || []);
  const nodes = [];
  const pushNode = (label, ids, fallback = null) => {
    if (!ids.length && !fallback) return;
    const names = ids.length ? formatList(ids.map((id) => getLane(id).name)) : fallback;
    nodes.push(`<span class="combat-tooltip-node"><i>${escapeHtml(label)}</i><b title="${escapeHtml(names)}">${escapeHtml(names)}</b></span>`);
  };
  pushNode(t("tooltip.trigger"), triggerIds);
  pushNode(t(event.kind === "card-attribute" && event.sourceConfidence === "related" ? "tooltip.relatedSource" : "tooltip.source"), sourceIds, event.kind === "card-attribute" ? t("common.unmappedSource") : null);
  pushNode(t("tooltip.target"), targetIds, t("common.systemSettlement"));
  return `<div class="combat-tooltip-path">${nodes.join('<em aria-hidden="true">→</em>')}</div>`;
}

function renderStateChangeTooltipPath(event) {
  const uniqueIds = (ids) => [...new Set(ids.filter(Boolean))];
  const sourceIds = uniqueIds([...(event.sources || []), event.source]);
  const triggerIds = uniqueIds([...(event.triggerSources || []), event.triggerSource]).filter((id) => !sourceIds.includes(id));
  const sourceNames = sourceIds.map((id) => getLane(id).name);
  sourceNames.push(...stateChangeCauseLabels(event));
  const originNames = [...new Set(sourceNames)];
  const targetNames = uniqueIds(event.targets || []).map((id) => getLane(id).name);
  const nodes = [];
  if (triggerIds.length) nodes.push({ label: t("tooltip.trigger"), names: triggerIds.map((id) => getLane(id).name) });
  const originLabel = event.sourceConfidence === "direct"
    ? t("tooltip.source")
    : event.sourceConfidence === "related"
    ? t("tooltip.relatedSource")
    : t("tooltip.settlement");
  nodes.push({ label: originLabel, names: originNames.length ? originNames : [stateChangeCauseLabel(event)] });
  nodes.push({ label: t("tooltip.target"), names: targetNames.length ? targetNames : [t("common.systemState")] });
  return `<div class="combat-tooltip-path">${nodes.map((node, index) => {
    const names = formatList(node.names);
    return `${index ? '<em aria-hidden="true">→</em>' : ""}<span class="combat-tooltip-node"><i>${escapeHtml(node.label)}</i><b title="${escapeHtml(names)}">${escapeHtml(names)}</b></span>`;
  }).join("")}</div>`;
}

function roleLabel(role) {
  const key = { applied: "role.applied", received: "role.received", trigger: "role.trigger" }[role];
  return key ? t(key) : "";
}

function describeEvent(event) {
  if (event.kind === "status-interval") {
    const label = t(`status.${event.status}Interval`, {}, t("status.interval", { status: event.status }));
    const sources = formatList((event.sources || []).map((id) => getLane(id).name)) || t("common.unknownSource");
    const target = formatList(event.targets.map((id) => getLane(id).name)) || t("common.systemState");
    return { label, family: "status", summary: t("description.status", { sources, target, duration: formatTime(event.durationMs) }) };
  }
  if (event.kind === "player-state") {
    const metric = PLAYER_STATE_METRICS[event.metric] || { metric: event.metric, family: "system" };
    const label = metricLabel(event.metric);
    const target = formatList(event.targets.map((id) => getLane(id).name)) || t("common.systemState");
    return {
      label: t("state.change", { metric: label }),
      family: metric.family,
      summary: t("description.state", { target, before: formatMetricValue(event.previousValue), after: formatMetricValue(event.currentValue), delta: formatSigned(event.amount) }),
    };
  }
  if (event.kind === "card-attribute") {
    const changes = event.attributeChanges || [];
    const attribute = changes.length === 1 ? cardAttributeLabel(changes[0].attribute) : "";
    const target = formatList(event.targets.map((id) => getLane(id).name)) || t("common.systemState");
    const families = [...new Set(changes.map((change) => cardAttributeFamily(change.attribute)))];
    return {
      label: tp("description.attributeChange", changes.length, { attribute }),
      family: families.length === 1 ? families[0] : "system",
      summary: `${target} · ${formatList(changes.map((change) => `${cardAttributeLabel(change.attribute)} ${formatSigned(change.amount)}`))}`,
    };
  }
  if (event.kind === "health") {
    const presentation = healthEventPresentation(event);
    const target = formatList(event.targets.map((id) => getLane(id).name)) || t("common.systemState");
    return {
      label: presentation.label,
      family: presentation.family,
      summary: t("description.health", { target, valueLabel: presentation.valueLabel, value: formatMetricValue(Math.abs(Number(event.amount) || 0)), critical: event.isCrit ? t("description.criticalSuffix") : "" }),
    };
  }
  const [labelKey, family] = ACTIONS[event.action] || [null, "system"];
  const label = labelKey ? t(labelKey, {}, event.action) : event.action || t("common.event");
  const source = event.source ? getLane(event.source).name : t("common.system");
  const targets = event.targets.length ? formatList(event.targets.map((id) => getLane(id).name)) : t("common.noExplicitTarget");
  if (event.kind === "skill-trigger") {
    const trigger = event.triggerSource ? getLane(event.triggerSource).name : t("common.unknownSource");
    const path = `${trigger} → ${source} → ${label} → ${targets}`;
    const summary = event.stateMetric
      ? t("description.skillStateResult", { path, metric: metricLabel(event.stateMetric), result: skillStateResultLabel(event) })
      : path;
    return { label, family: "skill", summary };
  }
  return { label, family, summary: `${source} → ${label} → ${targets}` };
}

function skillStateResultLabel(event) {
  if (event.stateResult === "changed") return formatSigned(event.amount);
  return t(event.stateResult === "related" ? "common.seeStateChart" : "common.noNumericChangeRecorded");
}

function cardAttributeLabel(attribute) {
  if (CARD_ATTRIBUTE_LABEL_KEYS[attribute]) return t(CARD_ATTRIBUTE_LABEL_KEYS[attribute], {}, attribute);
  const custom = /^Custom_(\d+)$/.exec(attribute || "");
  return custom ? t("attribute.custom", { number: custom[1] }) : attribute || t("attribute.fallback");
}

function cardAttributeFamily(attribute) {
  if (/Burn/i.test(attribute || "")) return "burn";
  if (/Damage/i.test(attribute || "")) return "damage";
  if (/Heal|Regen/i.test(attribute || "")) return "heal";
  if (/Shield|Charge/i.test(attribute || "")) return "charge";
  return "system";
}

function healthEventPresentation(event) {
  const presentation = HEALTH_EVENT_PRESENTATIONS[event.action];
  if (!presentation) return {
    label: event.action || t("health.fallback.label"),
    valueLabel: t(Number(event.amount) < 0 ? "health.fallback.loss" : "health.fallback.gain"),
    family: "system",
  };
  return { label: t(presentation.labelKey), valueLabel: t(presentation.valueLabelKey), family: presentation.family };
}

function statusFromAction(action) {
  return { CardHaste: "Haste", CardSlow: "Slow", CardFreeze: "Freeze" }[action] || null;
}

function getLane(id) {
  return state.laneById.get(id) || { id, name: shortId(id), detail: t("entity.unmapped"), owner: "unknown", type: "unknown", asset: null };
}

async function importBattleFile(file) {
  try {
    const text = file.name.toLowerCase().endsWith(".gz")
      ? await readGzipFile(file)
      : await file.text();
    const parsed = JSON.parse(text);
    const raw = parsed.battle || parsed.Battle || parsed;
    loadBattle(raw, file.name);
  } catch (error) {
    alert(t("app.importFailed", { message: error instanceof Error ? error.message : String(error) }));
  }
}

async function readGzipFile(file) {
  if (typeof DecompressionStream === "undefined") throw new Error(t("app.gzipUnsupported"));
  return new Response(file.stream().pipeThrough(new DecompressionStream("gzip"))).text();
}

function bindDropTarget() {
  let depth = 0;
  document.addEventListener("dragenter", (event) => {
    if (!Array.from(event.dataTransfer?.types || []).includes("Files")) return;
    event.preventDefault();
    depth += 1;
    document.body.classList.add("drag-active");
  });
  document.addEventListener("dragover", (event) => {
    if (!Array.from(event.dataTransfer?.types || []).includes("Files")) return;
    event.preventDefault();
    event.dataTransfer.dropEffect = "copy";
  });
  document.addEventListener("dragleave", () => {
    depth = Math.max(0, depth - 1);
    if (!depth) document.body.classList.remove("drag-active");
  });
  document.addEventListener("drop", (event) => {
    event.preventDefault();
    depth = 0;
    document.body.classList.remove("drag-active");
    const file = event.dataTransfer?.files?.[0];
    if (file) importBattleFile(file);
  });
}

function shortId(id) {
  if (id?.startsWith("player:")) return id.slice(7);
  return id?.length > 12 ? `${id.slice(0, 7)}…` : id || t("common.unknown");
}

function formatTime(ms) {
  const fractionDigits = ms >= 10000 ? 1 : 2;
  return `${new Intl.NumberFormat(state.locale, { minimumFractionDigits: fractionDigits, maximumFractionDigits: fractionDigits }).format(ms / 1000)}${t("unit.secondsShort")}`;
}
function formatMetricValue(value) {
  const numeric = Number(value) || 0;
  const absolute = Math.abs(numeric);
  if (!Number.isFinite(numeric)) return numeric < 0 ? "−∞" : "∞";
  if (absolute >= 1e15) return numeric.toExponential(2).replace("e+", "e");
  return numeric.toLocaleString(state.locale, { maximumFractionDigits: 2 });
}
function formatPercent(value) {
  return new Intl.NumberFormat(state.locale, { maximumFractionDigits: 1 }).format(Number(value) || 0);
}
function formatCompactValue(value) {
  const numeric = Number(value) || 0;
  const absolute = Math.abs(numeric);
  if (!Number.isFinite(numeric)) return numeric < 0 ? "−∞" : "∞";
  if (absolute < 10000) return formatMetricValue(numeric);
  if (absolute >= 1e15) return numeric.toExponential(1).replace("e+", "e");
  return new Intl.NumberFormat(state.locale, { notation: "compact", maximumFractionDigits: 1 }).format(numeric);
}
function formatSigned(value) { return value == null ? "" : `${value > 0 ? "+" : ""}${formatMetricValue(value)}`; }
function formatDate(value) {
  if (!value) return t("common.timeUnknown");
  return new Intl.DateTimeFormat(state.locale, { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false }).format(new Date(value));
}
function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>'"]/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[character]);
}
