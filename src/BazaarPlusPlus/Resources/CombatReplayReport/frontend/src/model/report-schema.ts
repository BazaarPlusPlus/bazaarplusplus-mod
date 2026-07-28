import {
  type ReportEntityV1,
  type ReportEventV1,
  type ReportFrameZeroStateV1,
  type ReportCardStatsV1,
  type ReportMetricSampleV1,
} from "./normalize.ts";
import {
  isRecord,
  type UnknownRecord,
} from "./value.ts";

const VIEWER_SCHEMA_VERSION = 1;

export interface ReportSummaryV1 {
  playerName: string;
  opponentName: string;
  outcome: string;
}

export interface ReportParticipantV1 {
  name: string;
  hero: string;
}

export interface ReportDocumentV1 {
  schemaVersion: number;
  documentId: string;
  battleId: string;
  recordedAtUtc: string;
  day?: number;
  result?: string;
  summary: ReportSummaryV1;
  player: ReportParticipantV1;
  opponent: ReportParticipantV1;
  frameDurationMs: number;
  frameCount: number;
  durationMs: number;
  winner: string;
  loser: string;
  rawRecordCount: number;
  entities: ReportEntityV1[];
  cardStats?: ReportCardStatsV1[];
  events: ReportEventV1[];
  frameZeroState: ReportFrameZeroStateV1;
  metrics: ReportMetricSampleV1[];
}

export interface RecordingSyncAnchorV1 {
  combatFrame: number;
  combatMs: number;
  mediaPtsMs: number;
  outputOrdinal: number;
}

export interface RecordingAssetV1 {
  contentKey: string;
  relativeUrl: string;
  semanticRole: string;
  naturalWidth: number;
  naturalHeight: number;
  sha256: string;
}

export interface RecordingManifestV1 {
  schemaVersion: number;
  artifactId: string;
  recordingId: string;
  battleId: string;
  videoRelativeUrl: string;
  scrubVideoRelativeUrl?: string;
  syncMetadataStatus: string;
  width?: number;
  height?: number;
  framesPerSecond?: number;
  durationMs?: number;
  syncAnchors: RecordingSyncAnchorV1[];
  assets: RecordingAssetV1[];
}

export interface ReportEnvelopeV1 {
  schemaVersion: number;
  locale: string;
  battleDocument: ReportDocumentV1;
  recordingManifest: RecordingManifestV1;
}

export class ReportError extends Error {
  readonly code: string;
  readonly details: string;

  constructor(code: string, details = "") {
    super(code);
    this.name = "ReportError";
    this.code = code;
    this.details = details;
  }
}

function invalid(path: string, reason: string): never {
  throw new ReportError("invalidData", `${path}:${reason}`);
}

function exactRecord(
  value: unknown,
  path: string,
  required: readonly string[],
  optional: readonly string[] = [],
): UnknownRecord {
  if (!isRecord(value)) invalid(path, "object");
  const allowed = new Set(required.concat(optional));
  for (const key of required) {
    if (!Object.prototype.hasOwnProperty.call(value, key)) {
      invalid(`${path}.${key}`, "missing");
    }
  }
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) invalid(`${path}.${key}`, "unexpected");
  }
  return value;
}

function requiredString(
  record: UnknownRecord,
  key: string,
  path: string,
): string {
  const value = record[key];
  if (typeof value !== "string") invalid(`${path}.${key}`, "string");
  return value;
}

function requiredNumber(
  record: UnknownRecord,
  key: string,
  path: string,
  integer = false,
): number {
  const value = record[key];
  if (
    typeof value !== "number"
    || !Number.isFinite(value)
    || (integer && !Number.isInteger(value))
  ) {
    invalid(`${path}.${key}`, integer ? "integer" : "number");
  }
  return value;
}

function requiredBoolean(
  record: UnknownRecord,
  key: string,
  path: string,
): boolean {
  const value = record[key];
  if (typeof value !== "boolean") invalid(`${path}.${key}`, "boolean");
  return value;
}

function optionalString(
  record: UnknownRecord,
  key: string,
  path: string,
): void {
  if (Object.prototype.hasOwnProperty.call(record, key)) {
    requiredString(record, key, path);
  }
}

function optionalNumber(
  record: UnknownRecord,
  key: string,
  path: string,
  integer = false,
): void {
  if (Object.prototype.hasOwnProperty.call(record, key)) {
    requiredNumber(record, key, path, integer);
  }
}

function optionalBoolean(
  record: UnknownRecord,
  key: string,
  path: string,
): void {
  if (Object.prototype.hasOwnProperty.call(record, key)) {
    requiredBoolean(record, key, path);
  }
}

function requiredArray(
  record: UnknownRecord,
  key: string,
  path: string,
): unknown[] {
  const value = record[key];
  if (!Array.isArray(value)) invalid(`${path}.${key}`, "array");
  return value;
}

function requiredStringArray(
  record: UnknownRecord,
  key: string,
  path: string,
): void {
  const values = requiredArray(record, key, path);
  for (let index = 0; index < values.length; index += 1) {
    if (typeof values[index] !== "string") {
      invalid(`${path}.${key}[${index}]`, "string");
    }
  }
}

function validateSchemaVersion(
  record: UnknownRecord,
  path: string,
): void {
  const schemaVersion = requiredNumber(
    record,
    "schemaVersion",
    path,
    true,
  );
  if (schemaVersion !== VIEWER_SCHEMA_VERSION) {
    invalid(`${path}.schemaVersion`, "unsupported");
  }
}

function validateSummary(value: unknown, path: string): void {
  const summary = exactRecord(
    value,
    path,
    ["playerName", "opponentName", "outcome"],
  );
  requiredString(summary, "playerName", path);
  requiredString(summary, "opponentName", path);
  requiredString(summary, "outcome", path);
}

function validateParticipant(value: unknown, path: string): void {
  const participant = exactRecord(value, path, ["name", "hero"]);
  requiredString(participant, "name", path);
  requiredString(participant, "hero", path);
}

function validateCombatantState(value: unknown, path: string): void {
  const state = exactRecord(
    value,
    path,
    [],
    ["health", "rage", "healthRegen", "shield", "burn", "poison"],
  );
  for (const metric of [
    "health",
    "rage",
    "healthRegen",
    "shield",
    "burn",
    "poison",
  ]) {
    optionalNumber(state, metric, path, true);
  }
}

function validateFrameZeroState(value: unknown, path: string): void {
  const state = exactRecord(value, path, ["player", "opponent"]);
  validateCombatantState(state.player, `${path}.player`);
  validateCombatantState(state.opponent, `${path}.opponent`);
}

function validateEntity(value: unknown, path: string): void {
  const entity = exactRecord(
    value,
    path,
    ["entityId", "owner", "type", "name", "order"],
    [
      "templateId",
      "size",
      "slot",
      "span",
      "tier",
      "enchant",
      "contentKey",
      "assetRelativeUrl",
    ],
  );
  requiredString(entity, "entityId", path);
  requiredString(entity, "owner", path);
  requiredString(entity, "type", path);
  requiredString(entity, "name", path);
  requiredNumber(entity, "order", path, true);
  for (const key of [
    "templateId",
    "size",
    "tier",
    "enchant",
    "contentKey",
    "assetRelativeUrl",
  ]) {
    optionalString(entity, key, path);
  }
  optionalNumber(entity, "slot", path, true);
  optionalNumber(entity, "span", path, true);
}

function validateRawReference(value: unknown, path: string): void {
  const reference = exactRecord(
    value,
    path,
    ["category", "type", "index"],
  );
  requiredString(reference, "category", path);
  requiredString(reference, "type", path);
  requiredNumber(reference, "index", path, true);
}

function validateEvent(value: unknown, path: string): void {
  const event = exactRecord(
    value,
    path,
    [
      "eventId",
      "frame",
      "frameSequence",
      "combatTimeMs",
      "kind",
      "action",
      "targetEntityIds",
      "removedTargetEntityIds",
      "role",
      "attributionConfidence",
      "rawReference",
    ],
    [
      "effectId",
      "executionContextId",
      "sourceEntityId",
      "triggerSourceEntityId",
      "value",
      "previousValue",
      "currentValue",
      "unit",
      "isCritical",
      "iconSemanticKey",
      "iconContentKey",
      "iconAssetRelativeUrl",
    ],
  );
  requiredString(event, "eventId", path);
  requiredNumber(event, "frame", path, true);
  requiredNumber(event, "frameSequence", path, true);
  requiredNumber(event, "combatTimeMs", path, true);
  requiredString(event, "kind", path);
  requiredString(event, "action", path);
  requiredStringArray(event, "targetEntityIds", path);
  requiredStringArray(event, "removedTargetEntityIds", path);
  requiredString(event, "role", path);
  requiredString(event, "attributionConfidence", path);
  for (const key of [
    "effectId",
    "executionContextId",
    "sourceEntityId",
    "triggerSourceEntityId",
    "unit",
    "iconSemanticKey",
    "iconContentKey",
    "iconAssetRelativeUrl",
  ]) {
    optionalString(event, key, path);
  }
  optionalNumber(event, "value", path, true);
  optionalNumber(event, "previousValue", path, true);
  optionalNumber(event, "currentValue", path, true);
  optionalBoolean(event, "isCritical", path);
  validateRawReference(event.rawReference, `${path}.rawReference`);
}

function validateMetric(value: unknown, path: string): void {
  const metric = exactRecord(
    value,
    path,
    ["frame", "combatTimeMs", "combatant", "metric", "value", "unit"],
  );
  requiredNumber(metric, "frame", path, true);
  requiredNumber(metric, "combatTimeMs", path, true);
  requiredString(metric, "combatant", path);
  requiredString(metric, "metric", path);
  requiredNumber(metric, "value", path, true);
  requiredString(metric, "unit", path);
}

function validateCardStats(value: unknown, path: string): void {
  const stats = exactRecord(
    value,
    path,
    [
      "entityId",
      "damageDone",
      "shieldAdded",
      "healAdded",
      "joyAdded",
      "poisonAdded",
      "burnAdded",
      "hastedCardsCount",
      "slowedCardsCount",
      "frozenCardsCount",
      "useCount",
      "regenAdded",
      "rageAdded",
    ],
  );
  requiredString(stats, "entityId", path);
  for (const key of [
    "damageDone",
    "shieldAdded",
    "healAdded",
    "joyAdded",
    "poisonAdded",
    "burnAdded",
    "hastedCardsCount",
    "slowedCardsCount",
    "frozenCardsCount",
    "useCount",
    "regenAdded",
    "rageAdded",
  ]) {
    requiredNumber(stats, key, path, true);
  }
}

function validateDocument(value: unknown, path: string): void {
  const documentModel = exactRecord(
    value,
    path,
    [
      "schemaVersion",
      "documentId",
      "battleId",
      "recordedAtUtc",
      "summary",
      "player",
      "opponent",
      "frameDurationMs",
      "frameCount",
      "durationMs",
      "winner",
      "loser",
      "rawRecordCount",
      "entities",
      "events",
      "frameZeroState",
      "metrics",
    ],
    ["day", "result", "cardStats"],
  );
  validateSchemaVersion(documentModel, path);
  requiredString(documentModel, "documentId", path);
  requiredString(documentModel, "battleId", path);
  requiredString(documentModel, "recordedAtUtc", path);
  optionalNumber(documentModel, "day", path, true);
  optionalString(documentModel, "result", path);
  validateSummary(documentModel.summary, `${path}.summary`);
  validateParticipant(documentModel.player, `${path}.player`);
  validateParticipant(documentModel.opponent, `${path}.opponent`);
  for (const key of [
    "frameDurationMs",
    "frameCount",
    "durationMs",
    "rawRecordCount",
  ]) {
    requiredNumber(documentModel, key, path, true);
  }
  requiredString(documentModel, "winner", path);
  requiredString(documentModel, "loser", path);
  const entities = requiredArray(documentModel, "entities", path);
  entities.forEach((entity, index) =>
    validateEntity(entity, `${path}.entities[${index}]`)
  );
  if (Object.prototype.hasOwnProperty.call(documentModel, "cardStats")) {
    const cardStats = requiredArray(documentModel, "cardStats", path);
    cardStats.forEach((stats, index) =>
      validateCardStats(stats, `${path}.cardStats[${index}]`)
    );
  }
  const events = requiredArray(documentModel, "events", path);
  events.forEach((event, index) =>
    validateEvent(event, `${path}.events[${index}]`)
  );
  validateFrameZeroState(
    documentModel.frameZeroState,
    `${path}.frameZeroState`,
  );
  const metrics = requiredArray(documentModel, "metrics", path);
  metrics.forEach((metric, index) =>
    validateMetric(metric, `${path}.metrics[${index}]`)
  );
}

function validateSyncAnchor(value: unknown, path: string): void {
  const anchor = exactRecord(
    value,
    path,
    ["combatFrame", "combatMs", "mediaPtsMs", "outputOrdinal"],
  );
  requiredNumber(anchor, "combatFrame", path, true);
  requiredNumber(anchor, "combatMs", path, true);
  requiredNumber(anchor, "mediaPtsMs", path, true);
  requiredNumber(anchor, "outputOrdinal", path, true);
}

function validateAsset(value: unknown, path: string): void {
  const asset = exactRecord(
    value,
    path,
    [
      "contentKey",
      "relativeUrl",
      "semanticRole",
      "naturalWidth",
      "naturalHeight",
      "sha256",
    ],
  );
  requiredString(asset, "contentKey", path);
  requiredString(asset, "relativeUrl", path);
  requiredString(asset, "semanticRole", path);
  requiredNumber(asset, "naturalWidth", path, true);
  requiredNumber(asset, "naturalHeight", path, true);
  requiredString(asset, "sha256", path);
}

function validateManifest(value: unknown, path: string): void {
  const manifest = exactRecord(
    value,
    path,
    [
      "schemaVersion",
      "artifactId",
      "recordingId",
      "battleId",
      "videoRelativeUrl",
      "syncMetadataStatus",
      "syncAnchors",
      "assets",
    ],
    [
      "scrubVideoRelativeUrl",
      "width",
      "height",
      "framesPerSecond",
      "durationMs",
    ],
  );
  validateSchemaVersion(manifest, path);
  for (const key of [
    "artifactId",
    "recordingId",
    "battleId",
    "videoRelativeUrl",
    "syncMetadataStatus",
  ]) {
    requiredString(manifest, key, path);
  }
  optionalNumber(manifest, "width", path, true);
  optionalNumber(manifest, "height", path, true);
  optionalNumber(manifest, "framesPerSecond", path);
  optionalNumber(manifest, "durationMs", path, true);
  if (manifest.scrubVideoRelativeUrl !== undefined) {
    requiredString(manifest, "scrubVideoRelativeUrl", path);
  }
  const anchors = requiredArray(manifest, "syncAnchors", path);
  anchors.forEach((anchor, index) =>
    validateSyncAnchor(anchor, `${path}.syncAnchors[${index}]`)
  );
  const assets = requiredArray(manifest, "assets", path);
  assets.forEach((asset, index) =>
    validateAsset(asset, `${path}.assets[${index}]`)
  );
}

export function decodeEnvelope(value: unknown): ReportEnvelopeV1 {
  const envelope = exactRecord(
    value,
    "envelope",
    ["schemaVersion", "locale", "battleDocument", "recordingManifest"],
  );
  const schemaVersion = requiredNumber(
    envelope,
    "schemaVersion",
    "envelope",
    true,
  );
  if (schemaVersion !== VIEWER_SCHEMA_VERSION) {
    throw new ReportError("unsupportedSchema", String(schemaVersion));
  }
  requiredString(envelope, "locale", "envelope");
  validateDocument(envelope.battleDocument, "envelope.battleDocument");
  validateManifest(
    envelope.recordingManifest,
    "envelope.recordingManifest",
  );
  return envelope as unknown as ReportEnvelopeV1;
}

export function readEnvelope(): ReportEnvelopeV1 {
  const payloadNodes = document.querySelectorAll("#bpp-report-data");
  if (payloadNodes.length !== 1) {
    throw new ReportError("duplicateData", String(payloadNodes.length));
  }
  const payload = payloadNodes[0];
  if (
    payload.tagName !== "SCRIPT"
    || payload.getAttribute("type") !== "application/json"
  ) {
    throw new ReportError("invalidData", "payload-element");
  }
  const raw = payload.textContent;
  if (!raw || raw.trim().length === 0) {
    throw new ReportError("invalidData", "empty-payload");
  }
  try {
    return decodeEnvelope(JSON.parse(raw));
  } catch (error) {
    if (error instanceof ReportError) throw error;
    throw new ReportError(
      "invalidData",
      error instanceof Error ? error.message : "json",
    );
  }
}
