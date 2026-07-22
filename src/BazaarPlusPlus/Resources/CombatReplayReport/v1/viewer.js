(function () {
  "use strict";

  const VIEWER_SCHEMA_VERSION = 1;
  const PIXELS_PER_SECOND = 80;
  const AXIS_HEIGHT = 40;
  const LANE_HEIGHT = 54;
  const MIN_TIMELINE_WIDTH = 720;
  const MAX_TIMELINE_WIDTH = 32000;
  const INSPECTOR_PAGE_SIZE = 80;
  const TIME_ZOOM_STEPS = [0.5, 0.75, 1, 1.5, 2, 3, 4];
  const LANE_ZOOM_STEPS = [0.65, 0.8, 1, 1.25, 1.5];
  const METRIC_ORDER = ["health", "rage", "healthRegen", "shield"];
  const METRIC_COLORS = {
    health: "#ff607d",
    rage: "#edc461",
    healthRegen: "#62dda5",
    shield: "#69b8ff",
  };
  const iconImageCache = new Map();

  const COPY = {
    "zh-CN": {
      product: "BAZAARPLUSPLUS · 终局战况分析",
      unknownPlayer: "我方",
      unknownOpponent: "对手",
      versus: "对阵",
      win: "胜利",
      loss: "失败",
      draw: "平局",
      unknownOutcome: "战斗结束",
      duration: "战斗时长",
      events: "战斗事件",
      recording: "录像同步",
      exact: "精确同步",
      unsynced: "录像可用 · 尚未同步",
      noRecording: "没有关联录像",
      recordingLoadError: "录像无法加载，但战斗事件仍可查看。",
      recordingReady: "录像已加载",
      recordingTitle: "录像对照",
      expandRecording: "展开录像",
      collapseRecording: "收起录像",
      recordingHintExact: "点击时间轴事件可定位到录像中的对应时刻。",
      recordingHintUnsynced: "这份录像缺少经过验证的帧级同步信息，因此不会自动跳转。",
      noRecordingHint: "本报告只包含战斗事件，没有生成录像。",
      timelineTitle: "事件时间轴",
      timelineHint: "横向滚动查看战斗；悬停高亮聚合事件，点击展开同一时刻的完整事件。",
      entity: "实体 / 状态",
      emptyTimeline: "这份报告没有可显示的战斗事件。",
      frameEvents: "本帧事件",
      event: "个事件",
      moreEvents: "继续显示",
      source: "来源",
      triggerSource: "触发来源",
      target: "目标",
      removedTarget: "移除目标",
      attribution: "归因依据",
      attributionExact: "原始记录提供精确来源",
      attributionTargetOnly: "原始记录只确认受到方，未提供施加来源",
      attributionUnknown: "原始记录未提供可验证的来源",
      applied: "施加",
      received: "受到",
      unknown: "未知",
      close: "关闭",
      reportError: "无法打开战斗报告",
      errorHint: "报告数据缺失、损坏或版本不受支持。录像文件不会受到影响。",
      invalidData: "报告数据无效",
      duplicateData: "页面必须只包含一份战斗报告数据",
      unsupportedSchema: "不支持的报告版本",
      unsafeVideoPath: "录像路径未通过本地安全校验",
      identityMismatch: "战斗与录像身份不一致，已关闭时间联动",
      invalidExactSync: "精确同步元数据不完整，已按未同步录像显示",
      generalLane: "战斗事件",
      time: "时间",
      frame: "帧",
      damage: "伤害",
      heal: "恢复",
      shield: "护盾",
      charge: "充能",
      haste: "加速",
      slow: "减速",
      freeze: "冰冻",
      skill: "技能触发",
      status: "状态变化",
      trigger: "触发",
      timelineTab: "时间轴",
      statisticsTab: "统计",
      metricsTitle: "双方战斗状态",
      metricsHint: "生命、怒气、再生与护盾共用一个数量级坐标；实线为我方，虚线为对手。",
      signedLogScale: "数量级轴：sign(x) · log10(1 + |x|)，保留正负号并压缩极端数值。",
      player: "我方",
      opponent: "对手",
      health: "生命",
      rage: "怒气",
      healthRegen: "再生",
      metricsEmpty: "这份报告没有可绘制的战斗状态采样。",
      timeZoom: "时间缩放",
      laneZoom: "泳道缩放",
      zoomOut: "缩小",
      zoomIn: "放大",
      zoomReset: "重置缩放",
      statisticsTitle: "战斗统计",
      statisticsHint: "伤害按三种口径分别展示：精确来源的造成、按目标统计的受到、以及其中未归因部分；三个口径不可相加。",
      outputTotals: "伤害归因 / 恢复 / 护盾",
      statusCounts: "充能 / 加速 / 减速 / 冰冻",
      effectCount: "状态影响次数",
      statisticsEmpty: "这份报告没有可统计的战斗事件。",
      exactDamageDealt: "造成伤害（来源已确认）",
      damageReceived: "受到伤害（按受到方）",
      unattributedDamage: "未归因伤害（按受到方）",
      healingReceived: "获得恢复（按受到方）",
      shieldReceived: "获得护盾（按受到方）",
      shieldLost: "损失护盾（按受到方）",
      totalDamage: "受到伤害总量",
      totalExactDamage: "已确认来源伤害",
      totalUnattributedDamage: "未归因伤害",
      totalHealing: "获得恢复总量",
      totalShield: "获得护盾总量",
      totalShieldLost: "损失护盾总量",
    },
    en: {
      product: "BAZAARPLUSPLUS · POST-COMBAT REPORT",
      unknownPlayer: "Player",
      unknownOpponent: "Opponent",
      versus: "versus",
      win: "Victory",
      loss: "Defeat",
      draw: "Draw",
      unknownOutcome: "Battle complete",
      duration: "Duration",
      events: "Events",
      recording: "Recording sync",
      exact: "Exact sync",
      unsynced: "Video ready · not synchronized",
      noRecording: "No linked recording",
      recordingLoadError: "The recording could not be loaded. Battle events remain available.",
      recordingReady: "Recording loaded",
      recordingTitle: "Recording",
      expandRecording: "Expand recording",
      collapseRecording: "Collapse recording",
      recordingHintExact: "Select a timeline event to seek to the matching point in the recording.",
      recordingHintUnsynced: "This recording has no verified frame-level sync data, so automatic seeking is disabled.",
      noRecordingHint: "This report contains battle events only; no recording was produced.",
      timelineTitle: "Event timeline",
      timelineHint: "Scroll horizontally through the battle. Hover to highlight a cluster; select it to inspect every event at that point.",
      entity: "Entity / state",
      emptyTimeline: "This report has no battle events to display.",
      frameEvents: "Events at this frame",
      event: "events",
      moreEvents: "Show more",
      source: "Source",
      triggerSource: "Trigger source",
      target: "Target",
      removedTarget: "Removed target",
      attribution: "Attribution",
      attributionExact: "Exact source supplied by the raw record",
      attributionTargetOnly: "The raw record identifies the recipient but not the source",
      attributionUnknown: "No verifiable source was supplied by the raw record",
      applied: "Applied",
      received: "Received",
      unknown: "Unknown",
      close: "Close",
      reportError: "Unable to open combat report",
      errorHint: "The report data is missing, damaged, or uses an unsupported version. The recording file is unaffected.",
      invalidData: "Invalid report data",
      duplicateData: "The page must contain exactly one combat report payload",
      unsupportedSchema: "Unsupported report version",
      unsafeVideoPath: "The recording path failed local safety validation",
      identityMismatch: "Battle and recording identities differ; timeline synchronization is disabled",
      invalidExactSync: "Exact sync metadata is incomplete; showing the recording as unsynchronized",
      generalLane: "Battle events",
      time: "Time",
      frame: "Frame",
      damage: "Damage",
      heal: "Healing",
      shield: "Shield",
      charge: "Charge",
      haste: "Haste",
      slow: "Slow",
      freeze: "Freeze",
      skill: "Skill trigger",
      status: "Status change",
      trigger: "Trigger",
      timelineTab: "Timeline",
      statisticsTab: "Statistics",
      metricsTitle: "Combatant state",
      metricsHint: "Health, rage, regeneration, and shield share one order-of-magnitude axis. Solid lines are the player; dashed lines are the opponent.",
      signedLogScale: "Order-of-magnitude axis: sign(x) · log10(1 + |x|), preserving sign while compressing extreme values.",
      player: "Player",
      opponent: "Opponent",
      health: "Health",
      rage: "Rage",
      healthRegen: "Regeneration",
      metricsEmpty: "This report has no combat-state samples to chart.",
      timeZoom: "Time zoom",
      laneZoom: "Lane zoom",
      zoomOut: "Zoom out",
      zoomIn: "Zoom in",
      zoomReset: "Reset zoom",
      statisticsTitle: "Combat statistics",
      statisticsHint: "Damage uses three separate views: dealt by a confirmed source, received by target, and the unattributed subset. Do not add these views together.",
      outputTotals: "Damage attribution / healing / shield",
      statusCounts: "Charge / haste / slow / freeze",
      effectCount: "Status effects",
      statisticsEmpty: "This report has no combat events to summarize.",
      exactDamageDealt: "Damage dealt (confirmed source)",
      damageReceived: "Damage received (by recipient)",
      unattributedDamage: "Unattributed damage (by recipient)",
      healingReceived: "Healing received (by recipient)",
      shieldReceived: "Shield received (by recipient)",
      shieldLost: "Shield lost (by recipient)",
      totalDamage: "Total damage received",
      totalExactDamage: "Confirmed-source damage",
      totalUnattributedDamage: "Unattributed damage",
      totalHealing: "Total healing received",
      totalShield: "Total shield received",
      totalShieldLost: "Total shield lost",
    },
  };

  COPY["zh-Hant"] = Object.assign({}, COPY["zh-CN"], {
    product: "BAZAARPLUSPLUS · 終局戰況分析",
    unknownOpponent: "對手",
    versus: "對陣",
    win: "勝利",
    loss: "失敗",
    draw: "平局",
    unknownOutcome: "戰鬥結束",
    duration: "戰鬥時長",
    events: "戰鬥事件",
    recording: "錄影同步",
    exact: "精確同步",
    unsynced: "錄影可用 · 尚未同步",
    noRecording: "沒有關聯錄影",
    recordingLoadError: "錄影無法載入，但戰鬥事件仍可查看。",
    recordingReady: "錄影已載入",
    recordingTitle: "錄影對照",
    expandRecording: "展開錄影",
    collapseRecording: "收起錄影",
    recordingHintExact: "點選時間軸事件可定位到錄影中的對應時刻。",
    recordingHintUnsynced: "這份錄影缺少經過驗證的影格級同步資訊，因此不會自動跳轉。",
    noRecordingHint: "本報告只包含戰鬥事件，沒有產生錄影。",
    timelineTitle: "事件時間軸",
    timelineHint: "橫向捲動查看戰鬥；懸停醒目顯示聚合事件，點選展開同一時刻的完整事件。",
    entity: "實體 / 狀態",
    emptyTimeline: "這份報告沒有可顯示的戰鬥事件。",
    frameEvents: "本影格事件",
    event: "個事件",
    moreEvents: "繼續顯示",
    source: "來源",
    triggerSource: "觸發來源",
    target: "目標",
    removedTarget: "移除目標",
    attribution: "歸因依據",
    attributionExact: "原始記錄提供精確來源",
    attributionTargetOnly: "原始記錄只確認受到方，未提供施加來源",
    attributionUnknown: "原始記錄未提供可驗證的來源",
    applied: "施加",
    received: "受到",
    unknown: "未知",
    close: "關閉",
    reportError: "無法開啟戰鬥報告",
    errorHint: "報告資料缺失、損壞或版本不受支援。錄影檔案不會受到影響。",
    invalidData: "報告資料無效",
    duplicateData: "頁面必須只包含一份戰鬥報告資料",
    unsupportedSchema: "不支援的報告版本",
    unsafeVideoPath: "錄影路徑未通過本機安全驗證",
    identityMismatch: "戰鬥與錄影身分不一致，已關閉時間連動",
    invalidExactSync: "精確同步中繼資料不完整，已按未同步錄影顯示",
    generalLane: "戰鬥事件",
    time: "時間",
    frame: "影格",
    damage: "傷害",
    heal: "恢復",
    shield: "護盾",
    charge: "充能",
    haste: "加速",
    slow: "減速",
    freeze: "冰凍",
    skill: "技能觸發",
    status: "狀態變化",
    trigger: "觸發",
    timelineTab: "時間軸",
    statisticsTab: "統計",
    metricsTitle: "雙方戰鬥狀態",
    metricsHint: "生命、怒氣、再生與護盾共用一個數量級座標；實線為我方，虛線為對手。",
    signedLogScale: "數量級軸：sign(x) · log10(1 + |x|)，保留正負號並壓縮極端數值。",
    player: "我方",
    opponent: "對手",
    health: "生命",
    rage: "怒氣",
    healthRegen: "再生",
    metricsEmpty: "這份報告沒有可繪製的戰鬥狀態取樣。",
    timeZoom: "時間縮放",
    laneZoom: "泳道縮放",
    zoomOut: "縮小",
    zoomIn: "放大",
    zoomReset: "重設縮放",
    statisticsTitle: "戰鬥統計",
    statisticsHint: "傷害按三種口徑分別顯示：精確來源的造成、按目標統計的受到、以及其中未歸因部分；三個口徑不可相加。",
    outputTotals: "傷害歸因 / 恢復 / 護盾",
    statusCounts: "充能 / 加速 / 減速 / 冰凍",
    effectCount: "狀態影響次數",
    statisticsEmpty: "這份報告沒有可統計的戰鬥事件。",
    exactDamageDealt: "造成傷害（來源已確認）",
    damageReceived: "受到傷害（按受到方）",
    unattributedDamage: "未歸因傷害（按受到方）",
    healingReceived: "獲得恢復（按受到方）",
    shieldReceived: "獲得護盾（按受到方）",
    shieldLost: "損失護盾（按受到方）",
    totalDamage: "受到傷害總量",
    totalExactDamage: "已確認來源傷害",
    totalUnattributedDamage: "未歸因傷害",
    totalHealing: "獲得恢復總量",
    totalShield: "獲得護盾總量",
    totalShieldLost: "損失護盾總量",
  });

  class ReportError extends Error {
    constructor(code, details) {
      super(code);
      this.name = "ReportError";
      this.code = code;
      this.details = details || "";
    }
  }

  function isRecord(value) {
    return value !== null && typeof value === "object" && !Array.isArray(value);
  }

  function asArray(value) {
    return Array.isArray(value) ? value : [];
  }

  function asString(value, fallback) {
    return typeof value === "string" && value.trim().length > 0 ? value.trim() : fallback;
  }

  function asFiniteNumber(value, fallback) {
    const number = typeof value === "number" ? value : Number(value);
    return Number.isFinite(number) ? number : fallback;
  }

  function pick(record, names, fallback) {
    if (!isRecord(record)) {
      return fallback;
    }

    for (const name of names) {
      if (Object.prototype.hasOwnProperty.call(record, name) && record[name] !== null && record[name] !== undefined) {
        return record[name];
      }
    }

    return fallback;
  }

  function selectLocale(envelope) {
    const battle = isRecord(envelope.battleDocument) ? envelope.battleDocument : {};
    const requested = asString(
      pick(envelope, ["locale", "language"], pick(battle, ["locale", "language"], navigator.language || "en")),
      "en"
    );
    const normalized = requested.toLowerCase().replace(/_/g, "-");
    if (
      normalized.startsWith("zh-hant") ||
      normalized.startsWith("zh-tw") ||
      normalized.startsWith("zh-hk") ||
      normalized.startsWith("zh-mo")
    ) {
      return "zh-Hant";
    }
    return normalized.startsWith("zh") ? "zh-CN" : "en";
  }

  function translate(copy, key) {
    return copy[key] || COPY.en[key] || key;
  }

  function setText(element, value) {
    element.replaceChildren(document.createTextNode(String(value)));
    return element;
  }

  function createElement(tagName, className, testId) {
    const element = document.createElement(tagName);
    if (className) {
      element.className = className;
    }
    if (testId) {
      element.setAttribute("data-bpp-test-id", testId);
    }
    return element;
  }

  function createTextElement(tagName, className, value, testId) {
    return setText(createElement(tagName, className, testId), value);
  }

  function readEnvelope() {
    const payloadNodes = document.querySelectorAll("#bpp-report-data");
    if (payloadNodes.length !== 1) {
      throw new ReportError("duplicateData", String(payloadNodes.length));
    }

    const payload = payloadNodes[0];
    if (payload.tagName !== "SCRIPT" || payload.getAttribute("type") !== "application/json") {
      throw new ReportError("invalidData", "payload-element");
    }

    const raw = payload.textContent;
    if (!raw || raw.trim().length === 0) {
      throw new ReportError("invalidData", "empty-payload");
    }

    let envelope;
    try {
      envelope = JSON.parse(raw);
    } catch (error) {
      throw new ReportError("invalidData", error instanceof Error ? error.message : "json");
    }

    if (!isRecord(envelope)) {
      throw new ReportError("invalidData", "envelope");
    }

    const schemaVersion = asFiniteNumber(envelope.schemaVersion, NaN);
    if (schemaVersion !== VIEWER_SCHEMA_VERSION) {
      throw new ReportError("unsupportedSchema", String(envelope.schemaVersion));
    }

    if (!isRecord(envelope.battleDocument)) {
      throw new ReportError("invalidData", "battleDocument");
    }

    return envelope;
  }

  function getRoot() {
    const roots = document.querySelectorAll("[data-bpp-test-id='report-root']");
    let root = roots.length > 0 ? roots[0] : null;
    if (!root) {
      root = createElement("main", "bpp-report-root", "report-root");
      document.body.append(root);
    }
    root.replaceChildren();
    root.classList.add("bpp-report-root");
    return root;
  }

  function normalizeEntity(raw, index) {
    const visual = isRecord(raw.visual) ? raw.visual : {};
    const id = asString(pick(raw, ["entityId", "instanceId", "id"], "entity-" + index), "entity-" + index);
    const name = asString(
      pick(raw, ["capturedName", "name", "displayName", "templateName"], id),
      id
    );
    const type = asString(pick(raw, ["type", "entityType", "semanticType"], "entity"), "entity");
    const side = asString(pick(raw, ["owner", "side", "team"], "neutral"), "neutral").toLowerCase();
    const span = Math.max(1, Math.min(3, Math.round(asFiniteNumber(pick(raw, ["span", "slotSpan", "size"], 1), 1))));
    const asset = asString(
      pick(raw, ["assetRelativeUrl", "iconRelativeUrl"], pick(visual, ["assetRelativeUrl", "relativeUrl"], "")),
      ""
    );
    return { id, name, type, side, span, asset, raw };
  }

  function nestedEntityId(value) {
    if (typeof value === "string" || typeof value === "number") {
      return String(value);
    }
    if (isRecord(value)) {
      return asString(pick(value, ["entityId", "instanceId", "id"], ""), "");
    }
    return "";
  }

  function eventTimeMs(raw) {
    const direct = asFiniteNumber(pick(raw, ["combatMs", "combatTimeMs", "timeMs", "timestampMs"], NaN), NaN);
    if (Number.isFinite(direct)) {
      return Math.max(0, direct);
    }
    const seconds = asFiniteNumber(pick(raw, ["combatTimeSeconds", "timeSeconds"], NaN), NaN);
    if (Number.isFinite(seconds)) {
      return Math.max(0, seconds * 1000);
    }
    const frame = asFiniteNumber(pick(raw, ["frame", "combatFrame"], NaN), NaN);
    return Number.isFinite(frame) ? Math.max(0, frame * 50) : 0;
  }

  function normalizeEvent(raw, index) {
    const sourceValue = pick(raw, ["sourceEntityId", "source"], "");
    const triggerSourceValue = pick(raw, ["triggerSourceEntityId", "triggerSource"], "");
    const targetValues = pick(raw, ["targetEntityIds", "targets", "targetEntities"], []);
    const removedTargetValues = pick(raw, ["removedTargetEntityIds", "removedTargets"], []);
    const singularTarget = pick(raw, ["targetEntityId", "target"], null);
    const targets = asArray(targetValues).map(nestedEntityId).filter(Boolean);
    const removedTargets = asArray(removedTargetValues).map(nestedEntityId).filter(Boolean);
    const singularTargetId = nestedEntityId(singularTarget);
    if (singularTargetId && !targets.includes(singularTargetId)) {
      targets.push(singularTargetId);
    }

    return {
      id: asString(pick(raw, ["eventId", "id"], "event-" + index), "event-" + index),
      frame: Math.max(0, Math.round(asFiniteNumber(pick(raw, ["frame", "combatFrame"], 0), 0))),
      sequence: Math.max(0, Math.round(asFiniteNumber(pick(raw, ["frameSequence", "sequence", "order"], index), index))),
      combatMs: eventTimeMs(raw),
      kind: asString(pick(raw, ["semanticKind", "kind", "type"], "status"), "status"),
      action: asString(pick(raw, ["action", "effectAction"], ""), ""),
      value: pick(raw, ["value", "amount"], null),
      previousValue: pick(raw, ["previousValue"], null),
      currentValue: pick(raw, ["currentValue"], null),
      unit: asString(pick(raw, ["unit", "valueUnit"], ""), ""),
      sourceId: nestedEntityId(sourceValue),
      triggerSourceId: nestedEntityId(triggerSourceValue),
      targetIds: targets,
      removedTargetIds: removedTargets,
      role: asString(pick(raw, ["role"], ""), ""),
      attributionConfidence: asString(
        pick(raw, ["attributionConfidence", "attribution"], "unknown"),
        "unknown"
      ).toLowerCase(),
      icon: safeAssetUrl(asString(pick(raw, ["iconAssetRelativeUrl", "iconRelativeUrl"], ""), "")),
      raw,
    };
  }

  function normalizeMetricName(value) {
    const normalized = asString(value, "").replace(/[\s_-]/gu, "").toLowerCase();
    if (normalized === "health" || normalized === "hp") return "health";
    if (normalized === "rage") return "rage";
    if (normalized === "healthregen" || normalized === "regen" || normalized === "regeneration") return "healthRegen";
    if (normalized === "shield" || normalized === "armor") return "shield";
    return "";
  }

  function normalizeMetric(raw) {
    const metric = normalizeMetricName(pick(raw, ["metric", "name"], ""));
    const side = normalizeSide(asString(pick(raw, ["combatant", "side", "owner"], "neutral"), "neutral").toLowerCase());
    const value = asFiniteNumber(pick(raw, ["value", "currentValue"], NaN), NaN);
    if (!metric || side === "neutral" || !Number.isFinite(value)) {
      return null;
    }
    return {
      frame: Math.max(0, Math.round(asFiniteNumber(pick(raw, ["frame", "combatFrame"], 0), 0))),
      combatMs: Math.max(0, asFiniteNumber(pick(raw, ["combatTimeMs", "combatMs", "timeMs"], 0), 0)),
      side,
      metric,
      value,
      unit: asString(pick(raw, ["unit"], "points"), "points"),
    };
  }

  function frameZeroMetricSamples(battle) {
    const frameZero = pick(battle, ["frameZeroState"], null);
    if (!isRecord(frameZero)) return [];
    const samples = [];
    for (const side of ["player", "opponent"]) {
      const state = pick(frameZero, [side], null);
      if (!isRecord(state)) continue;
      for (const metric of METRIC_ORDER) {
        const value = asFiniteNumber(pick(state, [metric], NaN), NaN);
        if (!Number.isFinite(value)) continue;
        samples.push({ frame: 0, combatMs: 0, side, metric, value, unit: "points" });
      }
    }
    return samples;
  }

  function normalizeAnchors(manifest) {
    const rawAnchors = asArray(pick(manifest, ["syncAnchors", "anchors"], []));
    const anchors = [];
    let priorCombat = -Infinity;
    let priorMedia = -Infinity;
    for (const raw of rawAnchors) {
      const combatMs = asFiniteNumber(pick(raw, ["combatMs", "combatTimeMs"], NaN), NaN);
      const mediaPtsMs = asFiniteNumber(pick(raw, ["mediaPtsMs", "mediaTimeMs"], NaN), NaN);
      if (!Number.isFinite(combatMs) || !Number.isFinite(mediaPtsMs) || combatMs < priorCombat || mediaPtsMs < priorMedia) {
        return [];
      }
      anchors.push({ combatMs, mediaPtsMs });
      priorCombat = combatMs;
      priorMedia = mediaPtsMs;
    }
    return anchors;
  }

  function hasUnsafeUrlSyntax(candidate) {
    return (
      !candidate ||
      candidate.length > 2048 ||
      candidate.startsWith("/") ||
      candidate.startsWith("\\") ||
      /[:?#\\\u0000-\u001f]/u.test(candidate)
    );
  }

  function safeDecodedPathSegment(rawSegment) {
    if (!rawSegment || rawSegment === "." || rawSegment === "..") {
      return "";
    }

    // Match the producer's Uri.EscapeDataString output exactly: unreserved bytes stay raw,
    // every other UTF-8 byte is represented by an uppercase percent triplet. This keeps the
    // C# and browser allowlists identical instead of merely checking the decoded value.
    if (!/^(?:[A-Za-z0-9._~-]|%[0-9A-F]{2})+$/u.test(rawSegment)) {
      return "";
    }

    let decoded;
    try {
      decoded = decodeURIComponent(rawSegment);
    } catch (_error) {
      return "";
    }

    // A percent remaining after one decode permits a second interpretation such as
    // %252e%252e. Producer paths encode each segment exactly once, so reject it entirely.
    if (
      !decoded ||
      decoded === "." ||
      decoded === ".." ||
      decoded.includes("%") ||
      /[/:?#\\\u0000-\u001f]/u.test(decoded)
    ) {
      return "";
    }
    const canonical = encodeURIComponent(decoded).replace(
      /[!'()*]/gu,
      (character) =>
        `%${character.charCodeAt(0).toString(16).toUpperCase().padStart(2, "0")}`,
    );
    if (canonical !== rawSegment) {
      return "";
    }
    return decoded;
  }

  function safeAssetUrl(value) {
    const candidate = asString(value, "");
    if (hasUnsafeUrlSyntax(candidate)) {
      return "";
    }

    const parts = candidate.split("/");
    if (
      parts.length !== 5 ||
      parts[0] !== ".." ||
      parts[1] !== "report-assets" ||
      parts[2] !== "objects" ||
      !/^[0-9a-f]{2}$/u.test(parts[3]) ||
      !/^[0-9a-f]{64}\.png$/u.test(parts[4]) ||
      parts[4].slice(0, 2) !== parts[3]
    ) {
      return "";
    }

    return candidate;
  }

  function safeVideoUrl(value) {
    const candidate = asString(value, "");
    if (hasUnsafeUrlSyntax(candidate)) {
      return "";
    }

    const parts = candidate.split("/");
    if (
      parts.length < 3 ||
      parts[0] !== ".." ||
      parts[1] !== "CombatReplayVideos"
    ) {
      return "";
    }

    for (let index = 2; index < parts.length; index += 1) {
      const decoded = safeDecodedPathSegment(parts[index]);
      if (!decoded) {
        return "";
      }
      if (index === parts.length - 1 && !decoded.toLowerCase().endsWith(".mp4")) {
        return "";
      }
    }

    return candidate;
  }

  function normalizeSyncState(manifest, battleId) {
    if (!isRecord(manifest)) {
      return { status: "NotRequested", anchors: [], identityMatches: true, issues: [] };
    }

    const issues = [];
    const manifestBattleId = asString(pick(manifest, ["battleId"], ""), "");
    const recordingId = asString(pick(manifest, ["recordingId"], ""), "");
    const identityMatches = !battleId || !manifestBattleId || battleId === manifestBattleId;
    if (!identityMatches) {
      issues.push("identityMismatch");
    }

    const requested = asString(
      pick(manifest, ["syncMetadataStatus", "syncStatus", "videoArtifactStatus", "status"], "ReadyUnsynced"),
      "ReadyUnsynced"
    ).toLowerCase();
    const anchors = normalizeAnchors(manifest);
    const exactRequested = requested === "readyexact" || requested === "exact";
    const exactValid = identityMatches && recordingId.length > 0 && anchors.length >= 2;
    if (exactRequested && !exactValid) {
      issues.push("invalidExactSync");
    }

    return {
      status: exactRequested && exactValid ? "ReadyExact" : "ReadyUnsynced",
      anchors: exactValid ? anchors : [],
      identityMatches,
      issues,
    };
  }

  function participantName(documentModel, side, fallback) {
    const summary = isRecord(documentModel.summary) ? documentModel.summary : {};
    const summaryKey = side === "player" ? "playerName" : "opponentName";
    const summaryName = asString(summary[summaryKey], "");
    if (summaryName) {
      return summaryName;
    }

    const participant = asArray(documentModel.participants).find((entry) => {
      return isRecord(entry) && asString(pick(entry, ["side", "owner", "team"], ""), "").toLowerCase() === side;
    });
    return participant ? asString(pick(participant, ["name", "displayName"], fallback), fallback) : fallback;
  }

  function buildViewModel(envelope, copy) {
    const battle = envelope.battleDocument;
    const manifest = isRecord(envelope.recordingManifest) ? envelope.recordingManifest : null;
    const battleId = asString(pick(battle, ["battleId"], ""), "");
    const entities = asArray(pick(battle, ["entities", "entityTable"], [])).filter(isRecord).map(normalizeEntity);
    const events = asArray(pick(battle, ["events", "combatEvents"], [])).filter(isRecord).map(normalizeEvent);
    events.sort((left, right) => left.combatMs - right.combatMs || left.frame - right.frame || left.sequence - right.sequence);
    const metrics = frameZeroMetricSamples(battle).concat(asArray(pick(battle, ["metrics", "metricSamples"], []))
      .filter(isRecord)
      .map(normalizeMetric)
      .filter(Boolean));
    metrics.sort((left, right) => left.combatMs - right.combatMs || left.frame - right.frame);

    const eventDuration = events.reduce((maximum, event) => Math.max(maximum, event.combatMs), 0);
    const metricDuration = metrics.reduce((maximum, metric) => Math.max(maximum, metric.combatMs), 0);
    const inferredDuration = Math.max(eventDuration, metricDuration);
    const durationMs = Math.max(
      inferredDuration,
      asFiniteNumber(pick(battle, ["durationMs", "combatDurationMs"], inferredDuration), inferredDuration)
    );
    const summary = isRecord(battle.summary) ? battle.summary : {};
    const outcome = asString(pick(summary, ["outcome", "result"], pick(battle, ["outcome", "result"], "")), "").toLowerCase();
    const videoValue = manifest
      ? pick(manifest, ["videoRelativeUrl", "videoRelativePath"], isRecord(manifest.video) ? pick(manifest.video, ["relativeUrl", "relativePath"], "") : "")
      : "";
    const videoUrl = safeVideoUrl(videoValue);
    const sync = normalizeSyncState(manifest, battleId);
    if (videoValue && !videoUrl) {
      sync.issues.push("unsafeVideoPath");
    }

    return {
      battleId,
      playerName: participantName(battle, "player", translate(copy, "unknownPlayer")),
      opponentName: participantName(battle, "opponent", translate(copy, "unknownOpponent")),
      outcome,
      durationMs,
      entities,
      events,
      metrics,
      videoUrl,
      sync,
    };
  }

  function formatDuration(milliseconds) {
    const seconds = Math.max(0, milliseconds) / 1000;
    return seconds < 10 ? seconds.toFixed(2) + "s" : seconds.toFixed(1) + "s";
  }

  function formatNumber(value) {
    const number = asFiniteNumber(value, NaN);
    return Number.isFinite(number) ? new Intl.NumberFormat().format(number) : "";
  }

  function formatCompactNumber(value) {
    const number = asFiniteNumber(value, NaN);
    if (!Number.isFinite(number)) return "";
    const absolute = Math.abs(number);
    if (absolute < 10000) return new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 }).format(number);
    if (absolute < 1e15) {
      return new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 2 }).format(number);
    }
    return number.toExponential(2).replace("e+", "e");
  }

  function signedOrder(value) {
    const number = asFiniteNumber(value, 0);
    return Math.sign(number) * Math.log10(1 + Math.abs(number));
  }

  function restoreSignedOrder(value) {
    const number = asFiniteNumber(value, 0);
    return Math.sign(number) * (Math.pow(10, Math.abs(number)) - 1);
  }

  function orderAxisLabel(value) {
    const restored = restoreSignedOrder(value);
    if (Math.abs(restored) < 0.5) return "0";
    const exponent = Math.floor(Math.log10(Math.max(1, Math.abs(restored))));
    if (exponent >= 3) return (restored < 0 ? "−" : "") + "1e" + exponent;
    return formatCompactNumber(restored);
  }

  function createChart(element) {
    if (!window.echarts || typeof window.echarts.init !== "function") {
      return null;
    }
    return window.echarts.init(element, null, { renderer: "canvas", useDirtyRect: true });
  }

  function metricSeries(model, copy) {
    const grouped = new Map();
    for (const sample of model.metrics) {
      const key = sample.side + ":" + sample.metric;
      if (!grouped.has(key)) grouped.set(key, []);
      grouped.get(key).push([sample.combatMs, signedOrder(sample.value), sample.value]);
    }

    const series = [];
    for (const side of ["player", "opponent"]) {
      for (const metric of METRIC_ORDER) {
        const values = grouped.get(side + ":" + metric);
        if (!values || values.length === 0) continue;
        series.push({
          name: translate(copy, side) + " · " + translate(copy, metric),
          type: "line",
          step: "end",
          symbol: "none",
          sampling: "lttb",
          animation: false,
          data: values,
          lineStyle: {
            color: METRIC_COLORS[metric],
            width: side === "player" ? 2.2 : 1.8,
            type: side === "player" ? "solid" : "dashed",
            opacity: side === "player" ? 1 : 0.82,
          },
          itemStyle: { color: METRIC_COLORS[metric] },
          emphasis: { focus: "series" },
        });
      }
    }
    return series;
  }

  function renderMetricsChart(parent, model, copy) {
    const shell = createElement("section", "bpp-metrics-shell", "metrics-section");
    const heading = createElement("div", "bpp-subsection-heading");
    const title = createElement("div");
    title.append(createTextElement("h3", "bpp-subsection-title", translate(copy, "metricsTitle"), "metrics-title"));
    title.append(createTextElement("p", "bpp-subsection-copy", translate(copy, "metricsHint")));
    heading.append(title);
    shell.append(heading);
    shell.append(createTextElement("p", "bpp-scale-note", translate(copy, "signedLogScale"), "metrics-scale-note"));

    if (model.metrics.length === 0) {
      shell.append(createTextElement("p", "bpp-inline-empty", translate(copy, "metricsEmpty"), "metrics-empty-state"));
      parent.append(shell);
      return function () {};
    }

    const chartElement = createElement("div", "bpp-metrics-chart", "metrics-chart");
    chartElement.setAttribute("role", "img");
    chartElement.setAttribute("aria-label", translate(copy, "metricsTitle"));
    shell.append(chartElement);
    parent.append(shell);
    const chart = createChart(chartElement);
    if (!chart) {
      chartElement.replaceChildren(createTextElement("p", "bpp-inline-empty", translate(copy, "metricsEmpty")));
      return function () {};
    }

    const series = metricSeries(model, copy);
    chart.setOption({
      animation: false,
      backgroundColor: "transparent",
      color: METRIC_ORDER.map((metric) => METRIC_COLORS[metric]),
      grid: { left: 74, right: 24, top: 64, bottom: 42 },
      legend: {
        type: "scroll",
        top: 12,
        left: 18,
        right: 18,
        textStyle: { color: "#aab7ca", fontSize: 11 },
        pageTextStyle: { color: "#91a0b7" },
      },
      tooltip: {
        trigger: "axis",
        confine: true,
        backgroundColor: "rgba(7, 12, 19, 0.96)",
        borderColor: "#40536c",
        textStyle: { color: "#edf3fb", fontSize: 12 },
        formatter: function (parameters) {
          if (!parameters || parameters.length === 0) return "";
          const rows = [formatDuration(parameters[0].value[0])];
          for (const parameter of parameters) {
            rows.push(parameter.marker + parameter.seriesName + ": <strong>" + formatCompactNumber(parameter.value[2]) + "</strong>");
          }
          return rows.join("<br>");
        },
      },
      xAxis: {
        type: "value",
        min: 0,
        max: Math.max(1, model.durationMs),
        axisLine: { lineStyle: { color: "#40536c" } },
        axisLabel: { color: "#8190a7", formatter: function (value) { return formatDuration(value); } },
        splitLine: { lineStyle: { color: "rgba(111, 137, 170, 0.14)" } },
      },
      yAxis: {
        type: "value",
        scale: true,
        axisLine: { show: true, lineStyle: { color: "#40536c" } },
        axisLabel: { color: "#8190a7", formatter: orderAxisLabel },
        splitLine: { lineStyle: { color: "rgba(111, 137, 170, 0.14)" } },
      },
      series,
      aria: { enabled: true, label: { description: translate(copy, "metricsHint") } },
    });
    const resize = function () { chart.resize(); };
    window.addEventListener("resize", resize, { passive: true });
    return resize;
  }

  function outcomeLabel(copy, outcome) {
    if (outcome.includes("win") || outcome.includes("victory")) {
      return translate(copy, "win");
    }
    if (outcome.includes("loss") || outcome.includes("defeat") || outcome.includes("fail")) {
      return translate(copy, "loss");
    }
    if (outcome.includes("draw") || outcome.includes("tie")) {
      return translate(copy, "draw");
    }
    return translate(copy, "unknownOutcome");
  }

  function outcomeClass(outcome) {
    if (outcome.includes("win") || outcome.includes("victory")) {
      return "is-win";
    }
    if (outcome.includes("loss") || outcome.includes("defeat") || outcome.includes("fail")) {
      return "is-loss";
    }
    return "is-neutral";
  }

  function renderFact(label, value, testId) {
    const fact = createElement("div", "bpp-fact", testId);
    fact.append(createTextElement("span", "bpp-fact-label", label));
    fact.append(createTextElement("strong", "bpp-fact-value", value));
    return fact;
  }

  function renderHeader(root, model, copy) {
    const header = createElement("header", "bpp-report-header", "report-header");
    const identity = createElement("div", "bpp-match-identity");
    identity.append(createTextElement("p", "bpp-eyebrow", translate(copy, "product")));

    const titleRow = createElement("div", "bpp-title-row");
    const title = createTextElement("h1", "bpp-match-title", model.playerName + " vs " + model.opponentName, "match-title");
    const result = createTextElement("span", "bpp-outcome " + outcomeClass(model.outcome), outcomeLabel(copy, model.outcome), "match-outcome");
    titleRow.append(title, result);
    identity.append(titleRow);
    if (model.battleId) {
      identity.append(createTextElement("p", "bpp-battle-id", model.battleId, "battle-id"));
    }

    const facts = createElement("div", "bpp-summary-facts", "summary-facts");
    facts.append(renderFact(translate(copy, "duration"), formatDuration(model.durationMs), "summary-duration"));
    facts.append(renderFact(translate(copy, "events"), formatNumber(model.events.length), "summary-event-count"));
    const syncLabel = model.videoUrl
      ? model.sync.status === "ReadyExact"
        ? translate(copy, "exact")
        : translate(copy, "unsynced")
      : translate(copy, "noRecording");
    facts.append(renderFact(translate(copy, "recording"), syncLabel, "summary-sync-state"));

    header.append(identity, facts);
    root.append(header);
  }

  function renderNotice(parent, message, tone, testId) {
    const notice = createElement("p", "bpp-notice " + tone, testId);
    notice.setAttribute("role", tone === "is-error" ? "alert" : "status");
    setText(notice, message);
    parent.append(notice);
  }

  function renderVideo(root, model, copy) {
    const section = createElement("section", "bpp-section bpp-video-section", "video-section");
    section.id = "recording";
    const heading = createElement("div", "bpp-section-heading");
    const headingText = createElement("div");
    headingText.append(createTextElement("h2", "bpp-section-title", translate(copy, "recordingTitle")));
    const hintKey = model.videoUrl
      ? model.sync.status === "ReadyExact"
        ? "recordingHintExact"
        : "recordingHintUnsynced"
      : "noRecordingHint";
    headingText.append(createTextElement("p", "bpp-section-subtitle", translate(copy, hintKey)));
    heading.append(headingText);

    const stateClass = model.sync.status === "ReadyExact" ? "is-exact" : model.videoUrl ? "is-unsynced" : "is-missing";
    const stateLabel = model.sync.status === "ReadyExact" ? translate(copy, "exact") : model.videoUrl ? translate(copy, "unsynced") : translate(copy, "noRecording");
    const syncPill = createTextElement("span", "bpp-sync-pill " + stateClass, stateLabel, "video-sync-state");
    syncPill.setAttribute("data-sync-state", model.videoUrl ? model.sync.status : "NotRequested");
    heading.append(syncPill);
    section.append(heading);

    let video = null;
    if (model.videoUrl) {
      const frame = createElement("div", "bpp-video-frame", "recording-video-frame");
      frame.id = "bpp-recording-video-frame";
      video = createElement("video", "bpp-recording-video", "recording-video");
      video.controls = true;
      video.preload = "metadata";
      video.playsInline = true;
      video.setAttribute("aria-label", translate(copy, "recordingTitle"));
      video.src = model.videoUrl;
      frame.append(video);
      section.append(frame);

      const toggle = createTextElement("button", "bpp-secondary-button bpp-video-toggle", "", "recording-video-toggle");
      toggle.type = "button";
      toggle.setAttribute("aria-controls", frame.id);
      const shortViewport = window.matchMedia && window.matchMedia("(max-height: 900px)").matches;
      let expanded = !shortViewport;
      function applyExpandedState() {
        frame.hidden = !expanded;
        section.classList.toggle("is-video-collapsed", !expanded);
        toggle.setAttribute("aria-expanded", expanded ? "true" : "false");
        setText(toggle, translate(copy, expanded ? "collapseRecording" : "expandRecording"));
      }
      toggle.addEventListener("click", function () {
        expanded = !expanded;
        if (!expanded && video && !video.paused) video.pause();
        applyExpandedState();
      });
      heading.append(toggle);
      applyExpandedState();

      const mediaStatus = createTextElement("p", "bpp-media-status", stateLabel, "video-load-state");
      mediaStatus.setAttribute("aria-live", "polite");
      video.addEventListener("loadedmetadata", function () {
        setText(mediaStatus, translate(copy, "recordingReady") + " · " + formatDuration(video.duration * 1000));
      });
      video.addEventListener("error", function () {
        mediaStatus.classList.add("is-error");
        setText(mediaStatus, translate(copy, "recordingLoadError"));
      });
      section.append(mediaStatus);
    } else {
      const empty = createElement("div", "bpp-empty-state bpp-video-empty", "video-empty-state");
      empty.append(createTextElement("strong", "bpp-empty-title", translate(copy, "noRecording")));
      empty.append(createTextElement("p", "bpp-empty-copy", translate(copy, "noRecordingHint")));
      section.append(empty);
    }

    for (const issue of model.sync.issues) {
      renderNotice(section, translate(copy, issue), "is-warning", "video-issue-" + issue);
    }

    root.append(section);
    return video;
  }

  function safeEntityAsset(entity) {
    return safeAssetUrl(entity.asset);
  }

  function cachedIconImage(url, onChange) {
    if (!url) return null;
    let entry = iconImageCache.get(url);
    if (!entry) {
      const image = new Image();
      entry = { image, state: "loading", listeners: [] };
      iconImageCache.set(url, entry);
      image.addEventListener("load", function () {
        entry.state = "ready";
        const listeners = entry.listeners.splice(0);
        for (const listener of listeners) listener();
      }, { once: true });
      image.addEventListener("error", function () {
        entry.state = "failed";
        entry.listeners.length = 0;
      }, { once: true });
      image.src = url;
    }
    if (entry.state === "loading" && onChange && !entry.listeners.includes(onChange)) {
      entry.listeners.push(onChange);
    }
    return entry.state === "ready" ? entry.image : null;
  }

  function renderLaneLabels(entities, copy, laneHeight) {
    const labels = createElement("div", "bpp-lane-labels", "timeline-lane-labels");
    labels.style.setProperty("--bpp-lane-scale", String(laneHeight / LANE_HEIGHT));
    labels.append(createTextElement("div", "bpp-lane-label-header", translate(copy, "entity")));
    for (let index = 0; index < entities.length; index += 1) {
      const entity = entities[index];
      const row = createElement("div", "bpp-lane-label bpp-side-" + normalizeSide(entity.side), "timeline-lane-" + index);
      row.style.height = laneHeight + "px";
      row.setAttribute("data-entity-id", entity.id);
      const assetUrl = safeEntityAsset(entity);
      if (assetUrl) {
        const assetFrame = createElement("span", "bpp-lane-art bpp-span-" + entity.span);
        const image = createElement("img", "bpp-lane-image", "timeline-lane-icon-" + index);
        image.alt = "";
        image.loading = "lazy";
        image.src = assetUrl;
        image.addEventListener("error", function () {
          assetFrame.classList.add("is-missing");
          image.remove();
        });
        assetFrame.append(image);
        row.append(assetFrame);
      } else {
        row.append(createTextElement("span", "bpp-lane-art bpp-art-placeholder", entity.name.slice(0, 1)));
      }
      const label = createElement("span", "bpp-lane-copy");
      label.append(createTextElement("strong", "bpp-lane-name", entity.name));
      label.append(createTextElement("small", "bpp-lane-type", entity.type));
      row.append(label);
      labels.append(row);
    }
    return labels;
  }

  function applyLaneHeight(labels, laneHeight) {
    labels.style.setProperty("--bpp-lane-scale", String(laneHeight / LANE_HEIGHT));
    for (const row of labels.querySelectorAll(".bpp-lane-label")) {
      row.style.height = laneHeight + "px";
    }
  }

  function applyRelatedLaneHighlights(labels, cluster, entities) {
    const rows = labels.querySelectorAll(".bpp-lane-label");
    for (const row of rows) {
      row.classList.remove(
        "is-event-related",
        "is-related-source",
        "is-related-trigger",
        "is-related-target",
        "is-related-removed"
      );
    }
    const related = relatedLaneRoles(cluster, entities);
    for (const [lane, roles] of related) {
      const row = rows[lane];
      if (!row) continue;
      row.classList.add("is-event-related");
      for (const role of roles) row.classList.add("is-related-" + role);
    }
  }

  function normalizeSide(side) {
    if (side.includes("player") || side.includes("friendly") || side === "self") {
      return "player";
    }
    if (side.includes("opponent") || side.includes("enemy")) {
      return "opponent";
    }
    return "neutral";
  }

  function kindToken(kind) {
    const normalized = kind.toLowerCase();
    if (normalized.includes("damage") || normalized.includes("burn") || normalized.includes("poison")) return "damage";
    if (normalized.includes("heal") || normalized.includes("regen") || normalized.includes("restore")) return "heal";
    if (normalized.includes("shield")) return "shield";
    if (normalized.includes("charge")) return "charge";
    if (normalized.includes("haste") || normalized.includes("speedup")) return "haste";
    if (normalized.includes("slow")) return "slow";
    if (normalized.includes("freeze")) return "freeze";
    if (normalized.includes("skill")) return "skill";
    if (normalized.includes("trigger")) return "trigger";
    return "status";
  }

  function eventKindToken(event) {
    return kindToken(event.kind + " " + event.action);
  }

  function isMetricOnlyEvent(event) {
    return event.kind.toLowerCase() === "player-attribute" && Boolean(normalizeMetricName(event.action));
  }

  function kindLabel(copy, event) {
    const token = eventKindToken(event);
    return translate(copy, token) || event.kind;
  }

  function markerColor(token) {
    switch (token) {
      case "damage": return "#ff607d";
      case "heal": return "#62dda5";
      case "shield": return "#69b8ff";
      case "charge": return "#48dfd0";
      case "haste": return "#48dfd0";
      case "slow": return "#f2b966";
      case "freeze": return "#8ea0ff";
      case "skill": return "#d77bf1";
      case "trigger": return "#f4cf6c";
      default: return "#9aabc2";
    }
  }

  function eventLaneEndpoints(event, entityIndex) {
    const endpoints = [];
    function append(entityId, role) {
      if (!entityId || !entityIndex.has(entityId)) return;
      const lane = entityIndex.get(entityId);
      const existing = endpoints.find((endpoint) => endpoint.lane === lane);
      if (!existing) endpoints.push({ lane, role });
      else if (existing.role !== role) existing.role = "both";
    }
    append(event.sourceId, "source");
    append(event.triggerSourceId, "trigger");
    for (const targetId of event.targetIds) append(targetId, "target");
    for (const removedTargetId of event.removedTargetIds) append(removedTargetId, "removed");
    if (endpoints.length === 0) {
      endpoints.push({ lane: entityIndex.get("__bpp-general") || 0, role: "neutral" });
    }
    return endpoints;
  }

  function relatedLaneRoles(cluster, entities) {
    const entityIndex = new Map(entities.map((entity, index) => [entity.id, index]));
    const related = new Map();
    if (!cluster) return related;
    function append(entityId, role) {
      if (!entityId || !entityIndex.has(entityId)) return;
      const lane = entityIndex.get(entityId);
      if (!related.has(lane)) related.set(lane, new Set());
      related.get(lane).add(role);
    }
    for (const event of cluster.events) {
      append(event.sourceId, "source");
      append(event.triggerSourceId, "trigger");
      for (const targetId of event.targetIds) append(targetId, "target");
      for (const removedTargetId of event.removedTargetIds) append(removedTargetId, "removed");
    }
    return related;
  }

  function buildClusters(model, events, entities, timelineWidth, laneHeight) {
    const entityIndex = new Map(entities.map((entity, index) => [entity.id, index]));
    const duration = Math.max(1, model.durationMs);
    const usableWidth = Math.max(1, timelineWidth - 28);
    const clusterMap = new Map();

    for (const event of events) {
      const x = 14 + Math.min(1, event.combatMs / duration) * usableWidth;
      for (const endpoint of eventLaneEndpoints(event, entityIndex)) {
        const pixel = Math.round(x);
        const key = event.frame + ":" + endpoint.lane + ":" + pixel + ":" + endpoint.role;
        let cluster = clusterMap.get(key);
        if (!cluster) {
          cluster = {
            lane: endpoint.lane,
            x,
            y: AXIS_HEIGHT + endpoint.lane * laneHeight + laneHeight / 2,
            role: endpoint.role,
            events: [],
            token: eventKindToken(event),
            icon: event.icon,
          };
          clusterMap.set(key, cluster);
        }
        cluster.events.push(event);
        if (!cluster.icon && event.icon) cluster.icon = event.icon;
      }
    }

    return Array.from(clusterMap.values()).sort((left, right) => left.x - right.x || left.lane - right.lane);
  }

  function drawMarker(context, cluster, selected, requestDraw) {
    const radius = cluster.events.length > 1 ? 11 : 9;
    const color = markerColor(cluster.token);
    context.save();
    context.translate(cluster.x, cluster.y);
    context.lineWidth = selected ? 3 : 2;
    context.strokeStyle = selected ? "#ffffff" : color;
    context.fillStyle = cluster.role === "target" || cluster.role === "both" ? color : "#0b1320";

    context.beginPath();
    if (cluster.role === "source" || cluster.role === "trigger") {
      context.moveTo(0, -radius);
      context.lineTo(radius, 0);
      context.lineTo(0, radius);
      context.lineTo(-radius, 0);
      context.closePath();
    } else if (cluster.role === "neutral") {
      context.rect(-radius, -radius, radius * 2, radius * 2);
    } else {
      context.arc(0, 0, radius, 0, Math.PI * 2);
    }
    context.fill();
    context.stroke();

    if (cluster.role === "trigger") {
      context.beginPath();
      context.arc(0, 0, 3, 0, Math.PI * 2);
      context.fillStyle = color;
      context.fill();
    } else if (cluster.role === "removed") {
      context.beginPath();
      context.moveTo(-4, -4);
      context.lineTo(4, 4);
      context.moveTo(4, -4);
      context.lineTo(-4, 4);
      context.strokeStyle = color;
      context.stroke();
    }

    const icon = cachedIconImage(cluster.icon, requestDraw);
    if (icon) {
      const iconRadius = Math.max(6, radius - 2);
      context.save();
      context.beginPath();
      context.arc(0, 0, iconRadius, 0, Math.PI * 2);
      context.clip();
      context.drawImage(icon, -iconRadius, -iconRadius, iconRadius * 2, iconRadius * 2);
      context.restore();
    }

    if (cluster.role === "both") {
      context.beginPath();
      context.moveTo(0, -4);
      context.lineTo(4, 0);
      context.lineTo(0, 4);
      context.lineTo(-4, 0);
      context.closePath();
      context.fillStyle = "#0b1320";
      context.fill();
    }

    if (cluster.events.length > 1) {
      const label = cluster.events.length > 99 ? "99+" : String(cluster.events.length);
      const badgeX = icon ? radius * 0.7 : 0;
      const badgeY = icon ? -radius * 0.7 : 0;
      if (icon) {
        context.beginPath();
        context.arc(badgeX, badgeY, 6.5, 0, Math.PI * 2);
        context.fillStyle = "#07101b";
        context.fill();
        context.strokeStyle = color;
        context.lineWidth = 1.5;
        context.stroke();
      }
      context.font = "700 9px system-ui, sans-serif";
      context.textAlign = "center";
      context.textBaseline = "middle";
      context.fillStyle = icon ? "#edf3fb" : cluster.role === "target" || cluster.role === "both" ? "#07101b" : color;
      context.fillText(label, badgeX, badgeY);
    }
    context.restore();
  }

  function drawTimeline(canvas, model, entities, clusters, selectedCluster, laneHeight, requestDraw) {
    const context = canvas.getContext("2d");
    if (!context) {
      return;
    }
    const width = canvas.width;
    const height = canvas.height;
    context.clearRect(0, 0, width, height);

    context.fillStyle = "#0a111c";
    context.fillRect(0, 0, width, height);
    context.fillStyle = "#0d1724";
    context.fillRect(0, 0, width, AXIS_HEIGHT);

    const related = relatedLaneRoles(selectedCluster, entities);
    for (const lane of related.keys()) {
      const y = AXIS_HEIGHT + lane * laneHeight;
      context.fillStyle = "rgba(83, 181, 211, 0.13)";
      context.fillRect(0, y, width, laneHeight);
      context.fillStyle = "rgba(98, 221, 165, 0.78)";
      context.fillRect(0, y, 3, laneHeight);
    }

    context.strokeStyle = "#243245";
    context.lineWidth = 1;
    context.font = "600 11px ui-monospace, SFMono-Regular, Menlo, monospace";
    context.fillStyle = "#8190a7";
    context.textAlign = "left";
    context.textBaseline = "middle";
    const durationSeconds = Math.max(0, model.durationMs / 1000);
    const tickStep = durationSeconds > 180 ? 10 : durationSeconds > 60 ? 5 : 1;
    for (let second = 0; second <= durationSeconds + 0.001; second += tickStep) {
      const x = 14 + Math.min(1, second / Math.max(0.001, durationSeconds)) * Math.max(1, width - 28);
      context.beginPath();
      context.moveTo(x, AXIS_HEIGHT - 8);
      context.lineTo(x, height);
      context.stroke();
      context.fillText(second.toFixed(0) + ".0s", x + 4, 15);
    }

    for (let lane = 0; lane <= entities.length; lane += 1) {
      const y = AXIS_HEIGHT + lane * laneHeight;
      context.beginPath();
      context.moveTo(0, y + 0.5);
      context.lineTo(width, y + 0.5);
      context.stroke();
    }

    for (const cluster of clusters) {
      drawMarker(context, cluster, cluster === selectedCluster, requestDraw);
    }
  }

  function createHitIndex(clusters) {
    const index = new Map();
    for (const cluster of clusters) {
      const key = Math.floor(cluster.x / 24);
      if (!index.has(key)) {
        index.set(key, []);
      }
      index.get(key).push(cluster);
    }
    return index;
  }

  function nearestCluster(canvas, clientX, clientY, hitIndex) {
    const bounds = canvas.getBoundingClientRect();
    if (bounds.width <= 0 || bounds.height <= 0) {
      return null;
    }
    const x = (clientX - bounds.left) * (canvas.width / bounds.width);
    const y = (clientY - bounds.top) * (canvas.height / bounds.height);
    const bucket = Math.floor(x / 24);
    let nearest = null;
    let nearestDistance = Infinity;
    for (let offset = -1; offset <= 1; offset += 1) {
      for (const cluster of hitIndex.get(bucket + offset) || []) {
        const distance = Math.hypot(cluster.x - x, cluster.y - y);
        if (distance <= 20 && distance < nearestDistance) {
          nearest = cluster;
          nearestDistance = distance;
        }
      }
    }
    return nearest;
  }

  function mapCombatToMedia(combatMs, anchors) {
    if (anchors.length < 2) {
      return null;
    }
    if (combatMs <= anchors[0].combatMs) {
      return anchors[0].mediaPtsMs;
    }
    for (let index = 1; index < anchors.length; index += 1) {
      const right = anchors[index];
      const left = anchors[index - 1];
      if (combatMs <= right.combatMs) {
        const combatSpan = right.combatMs - left.combatMs;
        if (combatSpan <= 0) {
          return right.mediaPtsMs;
        }
        const progress = (combatMs - left.combatMs) / combatSpan;
        return left.mediaPtsMs + progress * (right.mediaPtsMs - left.mediaPtsMs);
      }
    }
    return anchors[anchors.length - 1].mediaPtsMs;
  }

  function entityName(entityMap, id, copy) {
    return id && entityMap.has(id) ? entityMap.get(id).name : translate(copy, "unknown");
  }

  function attributionLabel(copy, event) {
    if (event.sourceId && event.attributionConfidence === "exact") {
      return translate(copy, "attributionExact");
    }
    if (event.attributionConfidence.includes("source-unknown") || event.attributionConfidence.includes("target-exact")) {
      return translate(copy, "attributionTargetOnly");
    }
    return translate(copy, "attributionUnknown");
  }

  function eventsAtFrame(events, frame) {
    return events
      .filter((event) => event.frame === frame)
      .sort((left, right) => left.sequence - right.sequence || left.id.localeCompare(right.id));
  }

  function formatEventValue(event) {
    const value = formatNumber(event.value);
    if (!value) {
      return "";
    }
    return event.unit ? value + " " + event.unit : value;
  }

  function renderInspectorEvents(container, events, entityMap, copy, limit) {
    container.replaceChildren();
    const list = createElement("ol", "bpp-event-list", "frame-event-list");
    const visible = events.slice(0, limit);
    for (let index = 0; index < visible.length; index += 1) {
      const event = visible[index];
      const token = eventKindToken(event);
      const item = createElement("li", "bpp-event-item", "frame-event-item-" + index);
      const header = createElement("div", "bpp-event-item-header");
      const glyph = createTextElement("span", "bpp-event-glyph bpp-kind-" + token, "●", "frame-event-icon-" + index);
      if (event.icon) {
        const image = createElement("img", "bpp-event-native-icon");
        image.alt = "";
        image.src = event.icon;
        image.addEventListener("load", function () {
          glyph.classList.add("has-native-icon");
        }, { once: true });
        image.addEventListener("error", function () {
          image.remove();
        }, { once: true });
        glyph.append(image);
      }
      header.append(glyph);
      header.append(createTextElement("strong", "bpp-event-kind bpp-kind-" + token, kindLabel(copy, event)));
      const value = formatEventValue(event);
      if (value) {
        header.append(createTextElement("span", "bpp-event-value", value));
      }
      header.append(createTextElement("time", "bpp-event-time", formatDuration(event.combatMs)));
      item.append(header);

      const relation = createElement("dl", "bpp-event-relation", "frame-event-relations-" + index);
      function appendRelation(labelKey, value, relationKey) {
        relation.append(createTextElement("dt", "bpp-event-relation-label", translate(copy, labelKey)));
        relation.append(createTextElement("dd", "bpp-event-relation-value", value, "frame-event-" + relationKey + "-" + index));
      }
      appendRelation("source", entityName(entityMap, event.sourceId, copy), "source");
      if (event.triggerSourceId) {
        appendRelation("triggerSource", entityName(entityMap, event.triggerSourceId, copy), "trigger-source");
      }
      appendRelation(
        "target",
        event.targetIds.length > 0
          ? event.targetIds.map((id) => entityName(entityMap, id, copy)).join(", ")
          : translate(copy, "unknown"),
        "targets"
      );
      if (event.removedTargetIds.length > 0) {
        appendRelation(
          "removedTarget",
          event.removedTargetIds.map((id) => entityName(entityMap, id, copy)).join(", "),
          "removed-targets"
        );
      }
      appendRelation("attribution", attributionLabel(copy, event), "attribution");
      item.append(relation);
      list.append(item);
    }
    container.append(list);

    if (limit < events.length) {
      const nextLimit = Math.min(events.length, limit + INSPECTOR_PAGE_SIZE);
      const more = createTextElement(
        "button",
        "bpp-secondary-button",
        translate(copy, "moreEvents") + " (" + (events.length - limit) + ")",
        "frame-events-more"
      );
      more.type = "button";
      more.addEventListener("click", function () {
        renderInspectorEvents(container, events, entityMap, copy, nextLimit);
      });
      container.append(more);
    }
  }

  function renderInspector(section, model, copy, video, timelineEvents) {
    const inspector = createElement("aside", "bpp-frame-inspector", "frame-inspector");
    inspector.hidden = true;
    const header = createElement("div", "bpp-inspector-header");
    const titleGroup = createElement("div");
    const title = createTextElement("h3", "bpp-inspector-title", translate(copy, "frameEvents"), "frame-inspector-title");
    const summary = createTextElement("p", "bpp-inspector-summary", "", "frame-inspector-summary");
    const total = createTextElement("span", "bpp-count-pill bpp-frame-event-total", "", "frame-event-total");
    titleGroup.append(title, summary, total);
    const close = createTextElement("button", "bpp-icon-button", translate(copy, "close"), "frame-inspector-close");
    close.type = "button";
    close.addEventListener("click", function () {
      inspector.hidden = true;
    });
    header.append(titleGroup, close);
    inspector.append(header);
    const content = createElement("div", "bpp-inspector-content");
    inspector.append(content);
    section.append(inspector);

    const entityMap = new Map(model.entities.map((entity) => [entity.id, entity]));
    return function show(cluster, seekVideo) {
      if (!cluster) {
        return;
      }
      const first = cluster.events[0];
      const frameEvents = eventsAtFrame(timelineEvents, first.frame);
      setText(
        summary,
        formatDuration(first.combatMs) + " · " + translate(copy, "frame") + " " + first.frame
      );
      setText(total, frameEvents.length + " " + translate(copy, "event"));
      total.setAttribute("data-frame", String(first.frame));
      renderInspectorEvents(content, frameEvents, entityMap, copy, INSPECTOR_PAGE_SIZE);
      inspector.hidden = false;

      if (seekVideo && video && model.sync.status === "ReadyExact") {
        const mediaMs = mapCombatToMedia(first.combatMs, model.sync.anchors);
        if (Number.isFinite(mediaMs)) {
          video.currentTime = Math.max(0, mediaMs / 1000);
        }
      }
    };
  }

  function renderTimeline(root, model, copy, video) {
    const section = createElement("section", "bpp-section bpp-timeline-section", "timeline-section");
    section.id = "timeline";
    const timelineEvents = model.events.filter((event) => !isMetricOnlyEvent(event));
    const heading = createElement("div", "bpp-section-heading");
    const headingText = createElement("div");
    headingText.append(createTextElement("h2", "bpp-section-title", translate(copy, "timelineTitle")));
    headingText.append(createTextElement("p", "bpp-section-subtitle", translate(copy, "timelineHint")));
    heading.append(headingText);
    const timelineMeta = createElement("div", "bpp-timeline-meta");
    const roleLegend = createElement("span", "bpp-role-legend", "timeline-role-legend");
    const applied = createElement("span", "bpp-role-key");
    applied.append(createTextElement("span", "bpp-role-shape is-source", "◆"));
    applied.append(createTextElement("span", "bpp-role-label", translate(copy, "applied")));
    const received = createElement("span", "bpp-role-key");
    received.append(createTextElement("span", "bpp-role-shape is-target", "●"));
    received.append(createTextElement("span", "bpp-role-label", translate(copy, "received")));
    roleLegend.append(applied, received);
    timelineMeta.append(roleLegend);
    timelineMeta.append(createTextElement("span", "bpp-count-pill", formatNumber(timelineEvents.length) + " " + translate(copy, "event"), "timeline-event-count"));
    heading.append(timelineMeta);
    section.append(heading);
    root.append(section);
    const resizeMetrics = renderMetricsChart(section, model, copy);

    if (timelineEvents.length === 0) {
      const empty = createElement("div", "bpp-empty-state", "timeline-empty-state");
      empty.append(createTextElement("strong", "bpp-empty-title", translate(copy, "emptyTimeline")));
      section.append(empty);
      return resizeMetrics;
    }

    const knownEntityIds = new Set(model.entities.map((entity) => entity.id));
    const needsGeneralLane = model.entities.length === 0 || timelineEvents.some((event) => {
      if (event.sourceId && knownEntityIds.has(event.sourceId)) return false;
      return !event.targetIds.some((targetId) => knownEntityIds.has(targetId));
    });
    const entities = model.entities.slice();
    if (needsGeneralLane) {
      entities.push({ id: "__bpp-general", name: translate(copy, "generalLane"), type: "event", side: "neutral", span: 1, asset: "" });
    }

    let timeZoomIndex = TIME_ZOOM_STEPS.indexOf(1);
    let laneZoomIndex = LANE_ZOOM_STEPS.indexOf(1);
    let laneHeight = LANE_HEIGHT;
    let clusters = [];
    let hitIndex = new Map();
    let selectedCluster = null;
    let pinnedCluster = null;
    let keyboardIndex = -1;
    let redrawRequested = false;

    const toolbar = createElement("div", "bpp-timeline-toolbar", "timeline-zoom-toolbar");
    const zoomGroups = {};
    function createZoomGroup(label, key) {
      const group = createElement("div", "bpp-zoom-group", key + "-zoom-controls");
      group.append(createTextElement("span", "bpp-zoom-label", label));
      const out = createTextElement("button", "bpp-zoom-button", "−", key + "-zoom-out");
      out.type = "button";
      out.setAttribute("aria-label", translate(copy, "zoomOut") + " · " + label);
      const reset = createTextElement("button", "bpp-zoom-value", "100%", key + "-zoom-reset");
      reset.type = "button";
      reset.setAttribute("aria-label", translate(copy, "zoomReset") + " · " + label);
      const inside = createTextElement("button", "bpp-zoom-button", "+", key + "-zoom-in");
      inside.type = "button";
      inside.setAttribute("aria-label", translate(copy, "zoomIn") + " · " + label);
      group.append(out, reset, inside);
      zoomGroups[key] = { out, reset, inside };
      return group;
    }
    toolbar.append(createZoomGroup(translate(copy, "timeZoom"), "time"));
    toolbar.append(createZoomGroup(translate(copy, "laneZoom"), "lane"));
    section.append(toolbar);

    const scroll = createElement("div", "bpp-timeline-scroll", "timeline-scroll");
    scroll.tabIndex = 0;
    const grid = createElement("div", "bpp-timeline-grid");
    const labels = renderLaneLabels(entities, copy, laneHeight);
    const canvas = createElement("canvas", "bpp-timeline-canvas", "timeline-canvas");
    canvas.tabIndex = 0;
    canvas.setAttribute("role", "img");
    canvas.setAttribute("aria-label", translate(copy, "timelineTitle") + ", " + timelineEvents.length + " " + translate(copy, "event"));
    canvas.append(document.createTextNode(translate(copy, "timelineTitle")));
    grid.append(labels, canvas);
    scroll.append(grid);
    section.append(scroll);

    const showInspector = renderInspector(section, model, copy, video, timelineEvents);
    let hoverSeekFrame = -1;
    let pendingHoverSeek = null;
    let hoverSeekAnimationFrame = 0;
    function scheduleHoverSeek(cluster) {
      if (!cluster || !video || !video.paused || model.sync.status !== "ReadyExact") {
        return;
      }
      const first = cluster.events[0];
      if (!first || first.frame === hoverSeekFrame) {
        return;
      }
      pendingHoverSeek = { frame: first.frame, combatMs: first.combatMs };
      if (hoverSeekAnimationFrame) {
        return;
      }
      hoverSeekAnimationFrame = window.requestAnimationFrame(function () {
        hoverSeekAnimationFrame = 0;
        const pending = pendingHoverSeek;
        pendingHoverSeek = null;
        if (!pending || !video.paused || model.sync.status !== "ReadyExact") {
          return;
        }
        const mediaMs = mapCombatToMedia(pending.combatMs, model.sync.anchors);
        if (!Number.isFinite(mediaMs)) {
          return;
        }
        const mediaSeconds = Math.max(0, mediaMs / 1000);
        if (Math.abs(video.currentTime - mediaSeconds) < 0.001) {
          hoverSeekFrame = pending.frame;
          return;
        }
        try {
          video.currentTime = mediaSeconds;
          hoverSeekFrame = pending.frame;
        } catch (_error) {
          // Metadata may not be available yet. A later hover will retry after the video is ready.
        }
      });
    }
    const requestDraw = function () {
      if (redrawRequested) return;
      redrawRequested = true;
      window.requestAnimationFrame(function () {
        redrawRequested = false;
        applyRelatedLaneHighlights(labels, selectedCluster, entities);
        drawTimeline(canvas, model, entities, clusters, selectedCluster, laneHeight, requestDraw);
      });
    };

    function updateZoomControls() {
      const timeScale = TIME_ZOOM_STEPS[timeZoomIndex];
      const laneScale = LANE_ZOOM_STEPS[laneZoomIndex];
      setText(zoomGroups.time.reset, Math.round(timeScale * 100) + "%");
      setText(zoomGroups.lane.reset, Math.round(laneScale * 100) + "%");
      zoomGroups.time.out.disabled = timeZoomIndex === 0;
      zoomGroups.time.inside.disabled = timeZoomIndex === TIME_ZOOM_STEPS.length - 1;
      zoomGroups.lane.out.disabled = laneZoomIndex === 0;
      zoomGroups.lane.inside.disabled = laneZoomIndex === LANE_ZOOM_STEPS.length - 1;
      canvas.setAttribute("data-time-zoom", String(timeScale));
      canvas.setAttribute("data-lane-zoom", String(laneScale));
    }

    function rebuildTimeline(preserveViewport) {
      const oldWidth = Math.max(1, canvas.width || MIN_TIMELINE_WIDTH);
      const oldLaneHeight = Math.max(1, laneHeight);
      const centerX = scroll.scrollLeft + scroll.clientWidth / 2;
      const centerCombatRatio = Math.max(0, Math.min(1, (centerX - 14) / Math.max(1, oldWidth - 28)));
      const centerLane = Math.max(0, (scroll.scrollTop + scroll.clientHeight / 2 - AXIS_HEIGHT) / oldLaneHeight);
      const selectedEventId = selectedCluster && selectedCluster.events[0] ? selectedCluster.events[0].id : "";
      const pinnedEventId = pinnedCluster && pinnedCluster.events[0] ? pinnedCluster.events[0].id : "";
      const selectedRole = selectedCluster ? selectedCluster.role : "";
      const selectedLane = selectedCluster ? selectedCluster.lane : -1;
      const pinnedRole = pinnedCluster ? pinnedCluster.role : "";
      const pinnedLane = pinnedCluster ? pinnedCluster.lane : -1;

      const timeScale = TIME_ZOOM_STEPS[timeZoomIndex];
      laneHeight = Math.round(LANE_HEIGHT * LANE_ZOOM_STEPS[laneZoomIndex]);
      const durationWidth = Math.ceil((Math.max(1000, model.durationMs) / 1000) * PIXELS_PER_SECOND * timeScale) + 28;
      const timelineWidth = Math.max(MIN_TIMELINE_WIDTH, Math.min(MAX_TIMELINE_WIDTH, durationWidth));
      const timelineHeight = AXIS_HEIGHT + entities.length * laneHeight;
      canvas.width = timelineWidth;
      canvas.height = timelineHeight;
      applyLaneHeight(labels, laneHeight);
      clusters = buildClusters(model, timelineEvents, entities, timelineWidth, laneHeight);
      hitIndex = createHitIndex(clusters);
      selectedCluster = selectedEventId
        ? clusters.find((cluster) => cluster.lane === selectedLane && cluster.role === selectedRole && cluster.events.some((event) => event.id === selectedEventId)) || null
        : null;
      pinnedCluster = pinnedEventId
        ? clusters.find((cluster) => cluster.lane === pinnedLane && cluster.role === pinnedRole && cluster.events.some((event) => event.id === pinnedEventId)) || null
        : null;
      keyboardIndex = selectedCluster ? clusters.indexOf(selectedCluster) : -1;
      updateZoomControls();
      requestDraw();

      if (preserveViewport) {
        scroll.scrollLeft = 14 + centerCombatRatio * Math.max(1, timelineWidth - 28) - scroll.clientWidth / 2;
        scroll.scrollTop = AXIS_HEIGHT + centerLane * laneHeight - scroll.clientHeight / 2;
      }
    }

    function changeZoom(kind, delta) {
      if (kind === "time") {
        timeZoomIndex = Math.max(0, Math.min(TIME_ZOOM_STEPS.length - 1, timeZoomIndex + delta));
      } else {
        laneZoomIndex = Math.max(0, Math.min(LANE_ZOOM_STEPS.length - 1, laneZoomIndex + delta));
      }
      rebuildTimeline(true);
    }

    zoomGroups.time.out.addEventListener("click", function () { changeZoom("time", -1); });
    zoomGroups.time.inside.addEventListener("click", function () { changeZoom("time", 1); });
    zoomGroups.time.reset.addEventListener("click", function () {
      timeZoomIndex = TIME_ZOOM_STEPS.indexOf(1);
      rebuildTimeline(true);
    });
    zoomGroups.lane.out.addEventListener("click", function () { changeZoom("lane", -1); });
    zoomGroups.lane.inside.addEventListener("click", function () { changeZoom("lane", 1); });
    zoomGroups.lane.reset.addEventListener("click", function () {
      laneZoomIndex = LANE_ZOOM_STEPS.indexOf(1);
      rebuildTimeline(true);
    });

    canvas.addEventListener("pointermove", function (event) {
      const hovered = nearestCluster(canvas, event.clientX, event.clientY, hitIndex);
      const active = hovered || pinnedCluster;
      if (active !== selectedCluster) {
        selectedCluster = active;
        requestDraw();
      }
      scheduleHoverSeek(hovered);
    });
    canvas.addEventListener("pointerleave", function () {
      selectedCluster = pinnedCluster;
      requestDraw();
    });
    canvas.addEventListener("click", function (event) {
      const selected = nearestCluster(canvas, event.clientX, event.clientY, hitIndex);
      if (selected) {
        if (hoverSeekAnimationFrame) {
          window.cancelAnimationFrame(hoverSeekAnimationFrame);
          hoverSeekAnimationFrame = 0;
          pendingHoverSeek = null;
        }
        selectedCluster = selected;
        pinnedCluster = selected;
        keyboardIndex = clusters.indexOf(selected);
        showInspector(selected, true);
        requestDraw();
      }
    });
    canvas.addEventListener("keydown", function (event) {
      if (event.key !== "ArrowLeft" && event.key !== "ArrowRight" && event.key !== "Enter" && event.key !== " ") {
        return;
      }
      event.preventDefault();
      if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
        const delta = event.key === "ArrowLeft" ? -1 : 1;
        keyboardIndex = Math.max(0, Math.min(clusters.length - 1, keyboardIndex < 0 ? 0 : keyboardIndex + delta));
        selectedCluster = clusters[keyboardIndex];
        pinnedCluster = selectedCluster;
        showInspector(selectedCluster, false);
        requestDraw();
      } else if (selectedCluster) {
        showInspector(selectedCluster, true);
      }
    });

    rebuildTimeline(false);
    return resizeMetrics;
  }

  function buildStatistics(model) {
    const result = {
      output: { player: [0, 0, 0, 0, 0, 0], opponent: [0, 0, 0, 0, 0, 0] },
      effects: { player: [0, 0, 0, 0], opponent: [0, 0, 0, 0] },
    };
    const entityMap = new Map(model.entities.map((entity) => [entity.id, entity]));
    const tempoActions = { CardCharge: 0, CardHaste: 1, CardSlow: 2, CardFreeze: 3 };
    for (const event of model.events) {
      if (event.kind.toLowerCase() === "health") {
        const target = event.targetIds.length > 0 ? entityMap.get(event.targetIds[0]) : null;
        const targetSide = target ? normalizeSide(target.side) : "neutral";
        const amount = asFiniteNumber(event.value, 0);
        if ((targetSide !== "player" && targetSide !== "opponent") || amount === 0) continue;
        const actionParts = event.action.split(":");
        const changedAttribute = actionParts[0] || "";
        const damageType = actionParts[1] || "";
        if (changedAttribute === "Health" && amount < 0) {
          const damage = Math.abs(amount);
          result.output[targetSide][1] += damage;
          const source = event.sourceId ? entityMap.get(event.sourceId) : null;
          const sourceSide = source ? normalizeSide(source.side) : "neutral";
          if (event.attributionConfidence === "exact" && (sourceSide === "player" || sourceSide === "opponent")) {
            result.output[sourceSide][0] += damage;
          } else {
            result.output[targetSide][2] += damage;
          }
        } else if (changedAttribute === "Health" && amount > 0 && (damageType === "Heal" || damageType === "Regen")) {
          result.output[targetSide][3] += amount;
        } else if (changedAttribute === "Shield" && amount > 0) {
          result.output[targetSide][4] += amount;
        } else if (changedAttribute === "Shield" && amount < 0) {
          result.output[targetSide][5] += Math.abs(amount);
        }
        continue;
      }
      if (event.kind.toLowerCase() !== "effect-executed" || !Object.prototype.hasOwnProperty.call(tempoActions, event.action)) continue;
      const source = entityMap.get(event.sourceId);
      const sourceSide = source ? normalizeSide(source.side) : "neutral";
      if (sourceSide === "player" || sourceSide === "opponent") result.effects[sourceSide][tempoActions[event.action]] += 1;
    }
    return result;
  }

  function renderComparisonChart(parent, title, categories, values, copy, useOrderScale, testId) {
    const card = createElement("article", "bpp-stats-chart-card", testId + "-card");
    card.append(createTextElement("h3", "bpp-stats-chart-title", title, testId + "-title"));
    if (useOrderScale) {
      card.append(createTextElement("p", "bpp-stats-chart-note", translate(copy, "signedLogScale"), testId + "-scale-note"));
    }
    const element = createElement("div", "bpp-stats-chart", testId);
    element.setAttribute("role", "img");
    element.setAttribute("aria-label", title);
    card.append(element);
    parent.append(card);
    const chart = createChart(element);
    if (!chart) return function () {};

    function dataFor(side) {
      return values[side].map((original) => ({
        value: useOrderScale ? signedOrder(original) : original,
        original,
      }));
    }
    chart.setOption({
      animation: false,
      backgroundColor: "transparent",
      color: ["#5ac9d6", "#ef7189"],
      grid: { left: 74, right: 24, top: 46, bottom: 36 },
      legend: {
        top: 8,
        right: 16,
        textStyle: { color: "#aab7ca", fontSize: 11 },
        data: [translate(copy, "player"), translate(copy, "opponent")],
      },
      tooltip: {
        trigger: "axis",
        confine: true,
        backgroundColor: "rgba(7, 12, 19, 0.96)",
        borderColor: "#40536c",
        textStyle: { color: "#edf3fb", fontSize: 12 },
        formatter: function (parameters) {
          if (!parameters || parameters.length === 0) return "";
          const rows = [parameters[0].axisValueLabel];
          for (const parameter of parameters) {
            rows.push(parameter.marker + parameter.seriesName + ": <strong>" + formatCompactNumber(parameter.data.original) + "</strong>");
          }
          return rows.join("<br>");
        },
      },
      xAxis: {
        type: "value",
        min: 0,
        axisLabel: {
          color: "#8190a7",
          formatter: useOrderScale ? orderAxisLabel : formatCompactNumber,
        },
        splitLine: { lineStyle: { color: "rgba(111, 137, 170, 0.14)" } },
      },
      yAxis: {
        type: "category",
        data: categories,
        axisLabel: { color: "#aab7ca", fontSize: 12 },
        axisLine: { lineStyle: { color: "#40536c" } },
        axisTick: { show: false },
      },
      series: [
        { name: translate(copy, "player"), type: "bar", data: dataFor("player"), barMaxWidth: 20, animation: false },
        { name: translate(copy, "opponent"), type: "bar", data: dataFor("opponent"), barMaxWidth: 20, animation: false },
      ],
      aria: { enabled: true, label: { description: title } },
    });
    const resize = function () { chart.resize(); };
    window.addEventListener("resize", resize, { passive: true });
    return resize;
  }

  function renderStatistics(root, model, copy) {
    const section = createElement("section", "bpp-section bpp-statistics-section", "statistics-section");
    const heading = createElement("div", "bpp-section-heading");
    const headingText = createElement("div");
    headingText.append(createTextElement("h2", "bpp-section-title", translate(copy, "statisticsTitle")));
    headingText.append(createTextElement("p", "bpp-section-subtitle", translate(copy, "statisticsHint")));
    heading.append(headingText);
    section.append(heading);
    root.append(section);

    if (model.events.length === 0) {
      section.append(createTextElement("p", "bpp-inline-empty", translate(copy, "statisticsEmpty"), "statistics-empty-state"));
      return function () {};
    }

    const statistics = buildStatistics(model);
    const outputTotals = [0, 1, 2, 3, 4, 5].map((index) => statistics.output.player[index] + statistics.output.opponent[index]);
    const effectTotal = statistics.effects.player.concat(statistics.effects.opponent).reduce((sum, value) => sum + value, 0);
    const kpis = createElement("div", "bpp-stats-kpis", "statistics-kpis");
    kpis.append(renderFact(translate(copy, "totalDamage"), formatCompactNumber(outputTotals[1]), "statistics-total-damage"));
    kpis.append(renderFact(translate(copy, "totalExactDamage"), formatCompactNumber(outputTotals[0]), "statistics-total-exact-damage"));
    kpis.append(renderFact(translate(copy, "totalUnattributedDamage"), formatCompactNumber(outputTotals[2]), "statistics-total-unattributed-damage"));
    kpis.append(renderFact(translate(copy, "totalHealing"), formatCompactNumber(outputTotals[3]), "statistics-total-healing"));
    kpis.append(renderFact(translate(copy, "totalShield"), formatCompactNumber(outputTotals[4]), "statistics-total-shield"));
    kpis.append(renderFact(translate(copy, "totalShieldLost"), formatCompactNumber(outputTotals[5]), "statistics-total-shield-lost"));
    kpis.append(renderFact(translate(copy, "effectCount"), formatCompactNumber(effectTotal), "statistics-effect-count"));
    section.append(kpis);

    const charts = createElement("div", "bpp-stats-charts", "statistics-charts");
    section.append(charts);
    const resizeOutput = renderComparisonChart(
      charts,
      translate(copy, "outputTotals"),
      [
        translate(copy, "exactDamageDealt"),
        translate(copy, "damageReceived"),
        translate(copy, "unattributedDamage"),
        translate(copy, "healingReceived"),
        translate(copy, "shieldReceived"),
        translate(copy, "shieldLost"),
      ],
      statistics.output,
      copy,
      true,
      "statistics-output-chart"
    );
    const resizeEffects = renderComparisonChart(
      charts,
      translate(copy, "statusCounts"),
      [translate(copy, "charge"), translate(copy, "haste"), translate(copy, "slow"), translate(copy, "freeze")],
      statistics.effects,
      copy,
      false,
      "statistics-effects-chart"
    );
    return function () {
      resizeOutput();
      resizeEffects();
    };
  }

  function renderReportTabs(root, model, copy, video) {
    const tabs = createElement("nav", "bpp-report-tabs", "report-tabs");
    tabs.setAttribute("role", "tablist");
    const timelineButton = createTextElement("button", "bpp-report-tab is-active", translate(copy, "timelineTab"), "report-tab-timeline");
    const statisticsButton = createTextElement("button", "bpp-report-tab", translate(copy, "statisticsTab"), "report-tab-statistics");
    timelineButton.type = "button";
    statisticsButton.type = "button";
    timelineButton.setAttribute("role", "tab");
    statisticsButton.setAttribute("role", "tab");
    timelineButton.setAttribute("aria-selected", "true");
    statisticsButton.setAttribute("aria-selected", "false");
    tabs.append(timelineButton, statisticsButton);

    const timelinePanel = createElement("div", "bpp-tab-panel", "report-panel-timeline");
    const statisticsPanel = createElement("div", "bpp-tab-panel", "report-panel-statistics");
    timelinePanel.setAttribute("role", "tabpanel");
    statisticsPanel.setAttribute("role", "tabpanel");
    root.append(tabs, timelinePanel, statisticsPanel);
    const resizeTimeline = renderTimeline(timelinePanel, model, copy, video) || function () {};
    const resizeStatistics = renderStatistics(statisticsPanel, model, copy) || function () {};
    statisticsPanel.hidden = true;

    function selectTab(name) {
      const timelineSelected = name === "timeline";
      timelinePanel.hidden = !timelineSelected;
      statisticsPanel.hidden = timelineSelected;
      timelineButton.classList.toggle("is-active", timelineSelected);
      statisticsButton.classList.toggle("is-active", !timelineSelected);
      timelineButton.setAttribute("aria-selected", timelineSelected ? "true" : "false");
      statisticsButton.setAttribute("aria-selected", timelineSelected ? "false" : "true");
      window.requestAnimationFrame(timelineSelected ? resizeTimeline : resizeStatistics);
    }
    timelineButton.addEventListener("click", function () { selectTab("timeline"); });
    statisticsButton.addEventListener("click", function () { selectTab("statistics"); });
  }

  function renderFatal(error) {
    const root = getRoot();
    const browserLocale = (navigator.language || "en").toLowerCase().replace(/_/g, "-");
    const locale =
      browserLocale.startsWith("zh-hant") ||
      browserLocale.startsWith("zh-tw") ||
      browserLocale.startsWith("zh-hk") ||
      browserLocale.startsWith("zh-mo")
        ? "zh-Hant"
        : browserLocale.startsWith("zh")
          ? "zh-CN"
          : "en";
    const copy = COPY[locale];
    const panel = createElement("section", "bpp-fatal-state", "report-error-state");
    panel.setAttribute("role", "alert");
    panel.append(createTextElement("span", "bpp-fatal-icon", "!", "report-error-icon"));
    panel.append(createTextElement("h1", "bpp-fatal-title", translate(copy, "reportError"), "report-error-title"));
    panel.append(createTextElement("p", "bpp-fatal-copy", translate(copy, "errorHint")));
    const code = error instanceof ReportError ? error.code : "invalidData";
    panel.append(createTextElement("p", "bpp-fatal-reason", translate(copy, code), "report-error-reason"));
    root.append(panel);
    document.documentElement.setAttribute("lang", locale);
  }

  function boot() {
    try {
      const envelope = readEnvelope();
      const locale = selectLocale(envelope);
      const copy = COPY[locale];
      document.documentElement.setAttribute("lang", locale);
      const root = getRoot();
      const model = buildViewModel(envelope, copy);
      renderHeader(root, model, copy);
      const video = renderVideo(root, model, copy);
      renderReportTabs(root, model, copy, video);
      document.documentElement.classList.add("bpp-report-ready");
    } catch (error) {
      renderFatal(error);
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot, { once: true });
  } else {
    boot();
  }
})();
