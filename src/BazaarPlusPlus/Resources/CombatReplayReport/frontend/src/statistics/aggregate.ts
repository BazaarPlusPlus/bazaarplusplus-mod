import {
  normalizeSide,
  type NormalizedEntity,
  type NormalizedEvent,
} from "../model/normalize.ts";
import { damageKindFromType } from "../model/damage-semantics.ts";
import { asFiniteNumber, asString } from "../model/value.ts";

export type CombatSide = "player" | "opponent";

export interface StatisticsModel {
  entities: NormalizedEntity[];
  events: NormalizedEvent[];
}

export interface ActivityColumn {
  key: string;
  label: string;
  token?: string;
  semanticKey?: string;
  fallback?: string;
  eventKind: "card-attribute" | "effect-executed";
  actions: readonly string[];
  quantitative: boolean;
  aggregation: "absolute" | "signed";
  unit: string;
}

export interface ActivityBaseColumn {
  key: string;
  label: string;
  token?: string;
  fallback?: string;
  actions?: readonly string[];
  quantitative?: boolean;
}

export const ACTIVITY_COLUMNS: readonly ActivityColumn[] = [
  {
    key: "damage",
    label: "activityDamage",
    token: "damage",
    semanticKey: "status.damage",
    eventKind: "effect-executed",
    actions: ["PlayerDamage"],
    quantitative: true,
    aggregation: "absolute",
    unit: "points",
  },
  {
    key: "burn",
    label: "activityBurn",
    token: "burn",
    semanticKey: "status.burn",
    eventKind: "effect-executed",
    actions: ["PlayerBurnApply"],
    quantitative: true,
    aggregation: "absolute",
    unit: "points",
  },
  {
    key: "poison",
    label: "activityPoison",
    token: "poison",
    semanticKey: "status.poison",
    eventKind: "effect-executed",
    actions: ["PlayerPoisonApply"],
    quantitative: true,
    aggregation: "absolute",
    unit: "points",
  },
  {
    key: "heal",
    label: "activityHeal",
    token: "heal",
    semanticKey: "status.heal",
    eventKind: "effect-executed",
    actions: ["PlayerHeal"],
    quantitative: true,
    aggregation: "absolute",
    unit: "points",
  },
  {
    key: "shield",
    label: "activityShield",
    token: "shield",
    semanticKey: "status.shield",
    eventKind: "effect-executed",
    actions: ["PlayerShieldApply"],
    quantitative: true,
    aggregation: "absolute",
    unit: "points",
  },
  {
    key: "regen",
    label: "activityRegen",
    token: "regen",
    semanticKey: "status.regen",
    eventKind: "effect-executed",
    actions: ["PlayerRegenApply"],
    quantitative: true,
    aggregation: "absolute",
    unit: "points",
  },
  {
    key: "charge",
    label: "activityCharge",
    token: "charge",
    semanticKey: "status.charge",
    eventKind: "effect-executed",
    actions: ["CardCharge"],
    quantitative: true,
    aggregation: "absolute",
    unit: "ms",
  },
  {
    key: "haste",
    label: "activityHaste",
    token: "haste",
    semanticKey: "status.haste",
    eventKind: "effect-executed",
    actions: ["CardHaste"],
    quantitative: true,
    aggregation: "absolute",
    unit: "ms",
  },
  {
    key: "slow",
    label: "activitySlow",
    token: "slow",
    semanticKey: "status.slow",
    eventKind: "effect-executed",
    actions: ["CardSlow"],
    quantitative: true,
    aggregation: "absolute",
    unit: "ms",
  },
  {
    key: "freeze",
    label: "activityFreeze",
    token: "freeze",
    semanticKey: "status.freeze",
    eventKind: "effect-executed",
    actions: ["CardFreeze"],
    quantitative: true,
    aggregation: "absolute",
    unit: "ms",
  },
  {
    key: "damageModifier",
    label: "attributeDamage",
    token: "attributeDamage",
    semanticKey: "status.damage",
    eventKind: "card-attribute",
    actions: ["DamageAmount"],
    quantitative: true,
    aggregation: "signed",
    unit: "points",
  },
  {
    key: "cooldownReduction",
    label: "attributeCooldownReduction",
    token: "attributeCooldownReduction",
    semanticKey: "status.cooldownReduction",
    eventKind: "card-attribute",
    actions: ["PercentCooldownReduction"],
    quantitative: true,
    aggregation: "signed",
    unit: "percent",
  },
  {
    key: "multicast",
    label: "attributeMulticast",
    token: "attributeMulticast",
    semanticKey: "status.multicast",
    eventKind: "card-attribute",
    actions: ["Multicast"],
    quantitative: true,
    aggregation: "signed",
    unit: "points",
  },
  {
    key: "ammo",
    label: "attributeAmmo",
    token: "attributeAmmo",
    semanticKey: "status.ammo",
    eventKind: "card-attribute",
    actions: ["Ammo"],
    quantitative: true,
    aggregation: "signed",
    unit: "points",
  },
  {
    key: "critChance",
    label: "attributeCritChance",
    token: "attributeCritChance",
    semanticKey: "status.critChance",
    eventKind: "card-attribute",
    actions: ["CritChance"],
    quantitative: true,
    aggregation: "signed",
    unit: "percent",
  },
  {
    key: "destroy",
    label: "destroy",
    token: "destroy",
    semanticKey: "status.destroy",
    eventKind: "effect-executed",
    actions: ["CardDisable", "CardDestroy"],
    quantitative: false,
    aggregation: "absolute",
    unit: "",
  },
];

export const ACTIVITY_BASE_COLUMNS: readonly ActivityBaseColumn[] = [
  { key: "entity", label: "activityEntity" },
  {
    key: "triggers",
    label: "activityTriggers",
    token: "trigger",
    fallback: "#",
    actions: [],
    quantitative: false,
  },
];

interface DamageTypes {
  direct: number;
  burn: number;
  poison: number;
  other: number;
}

export interface CombatStatistics {
  output: Record<CombatSide, number[]>;
  effects: Record<CombatSide, number[]>;
  damageDealt: Record<CombatSide, number>;
  damageTypes: Record<CombatSide, DamageTypes>;
}

export interface ActivityAmountDetails {
  increaseAmount: number;
  increaseCount: number;
  decreaseAmount: number;
  decreaseCount: number;
  firstRecordedValue: number | null;
  lastRecordedValue: number | null;
}

export function buildStatistics(model: StatisticsModel): CombatStatistics {
  const output: Record<CombatSide, number[]> = {
    player: [0, 0, 0, 0, 0, 0],
    opponent: [0, 0, 0, 0, 0, 0],
  };
  const effects: Record<CombatSide, number[]> = {
    player: [0, 0, 0, 0],
    opponent: [0, 0, 0, 0],
  };
  const damageDealt: Record<CombatSide, number> = {
    player: 0,
    opponent: 0,
  };
  const damageTypes: Record<CombatSide, DamageTypes> = {
    player: { direct: 0, burn: 0, poison: 0, other: 0 },
    opponent: { direct: 0, burn: 0, poison: 0, other: 0 },
  };
  const entityMap = new Map(
    model.entities.map((entity) => [entity.id, entity]),
  );
  const tempoActions: Record<string, number> = {
    CardCharge: 0,
    CardHaste: 1,
    CardSlow: 2,
    CardFreeze: 3,
  };

  for (const event of model.events) {
    if (event.kind.toLowerCase() === "health") {
      const target =
        event.targetIds.length > 0
          ? entityMap.get(event.targetIds[0])
          : null;
      const targetSide = target ? normalizeSide(target.side) : "neutral";
      const amount = asFiniteNumber(event.value, 0);
      if (
        (targetSide !== "player" && targetSide !== "opponent")
        || amount === 0
      ) {
        continue;
      }
      const actionParts = event.action.split(":");
      const changedAttribute = actionParts[0] || "";
      const damageType = actionParts[1] || "";
      if (
        (changedAttribute === "Health" || changedAttribute === "Shield")
        && amount < 0
      ) {
        const damage = Math.abs(amount);
        output[targetSide][changedAttribute === "Health" ? 1 : 5] += damage;
        damageTypes[targetSide][damageKindFromType(damageType)] += damage;
      } else if (
        changedAttribute === "Health"
        && amount > 0
        && (damageType === "Heal" || damageType === "Regen")
      ) {
        output[targetSide][3] += amount;
      } else if (changedAttribute === "Shield" && amount > 0) {
        output[targetSide][4] += amount;
      }
      continue;
    }
    if (event.kind.toLowerCase() !== "effect-executed") continue;
    const source = entityMap.get(event.sourceId);
    const sourceSide = source ? normalizeSide(source.side) : "neutral";
    if (!Object.prototype.hasOwnProperty.call(tempoActions, event.action)) {
      continue;
    }
    if (sourceSide === "player" || sourceSide === "opponent") {
      effects[sourceSide][tempoActions[event.action]] += 1;
    }
  }

  damageDealt.player = output.opponent[1] + output.opponent[5];
  damageDealt.opponent = output.player[1] + output.player[5];

  return {
    output,
    effects,
    damageDealt,
    damageTypes,
  };
}

export interface EntityActivityRow {
  entity: NormalizedEntity;
  side: CombatSide;
  triggers: number;
  counts: Record<string, number>;
  amounts: Record<string, number>;
  amountDetails: Record<string, ActivityAmountDetails>;
  quantifiedCounts: Record<string, number>;
}

export interface ActivitySortState {
  key: string;
  metric: "amount" | "count";
  direction: "asc" | "desc";
}

export function isActivityEntity(entity: unknown): entity is NormalizedEntity {
  if (!entity || typeof entity !== "object") return false;
  const candidate = entity as Partial<NormalizedEntity>;
  const type = asString(candidate.type, "").toLowerCase();
  const side = normalizeSide(asString(candidate.side, "neutral"));
  return (
    (type === "item" || type === "skill")
    && (side === "player" || side === "opponent")
  );
}

export function buildEntityActivity(
  model: StatisticsModel,
): EntityActivityRow[] {
  const columnByEvent = new Map<string, string>();
  const columnByKey = new Map(
    ACTIVITY_COLUMNS.map((column) => [column.key, column]),
  );
  for (const column of ACTIVITY_COLUMNS) {
    for (const action of column.actions) {
      columnByEvent.set(
        `${column.eventKind}\u001f${action}`,
        column.key,
      );
    }
  }
  const entityMap = new Map(
    model.entities.map((entity) => [entity.id, entity]),
  );
  const rows = new Map<string, EntityActivityRow>();
  for (const entity of model.entities) {
    if (!isActivityEntity(entity) || rows.has(entity.id)) continue;
    const counts: Record<string, number> = {};
    const amounts: Record<string, number> = {};
    const amountDetails: Record<string, ActivityAmountDetails> = {};
    const quantifiedCounts: Record<string, number> = {};
    for (const column of ACTIVITY_COLUMNS) {
      counts[column.key] = 0;
      amounts[column.key] = 0;
      amountDetails[column.key] = {
        increaseAmount: 0,
        increaseCount: 0,
        decreaseAmount: 0,
        decreaseCount: 0,
        firstRecordedValue: null,
        lastRecordedValue: null,
      };
      quantifiedCounts[column.key] = 0;
    }
    rows.set(entity.id, {
      entity,
      side: normalizeSide(entity.side) as CombatSide,
      triggers: 0,
      counts,
      amounts,
      amountDetails,
      quantifiedCounts,
    });
  }
  const applyEvent = (
    row: EntityActivityRow,
    event: NormalizedEvent,
    count: number,
    columnKey: string,
  ): void => {
    row.counts[columnKey] += count;
    const column = columnByKey.get(columnKey);
    const hasValue =
      event.value !== null
      && event.value !== undefined
      && event.value !== ""
      && Number.isFinite(asFiniteNumber(event.value, Number.NaN));
    if (!column || !column.quantitative || !hasValue) return;
    const value = asFiniteNumber(event.value, 0);
    row.amounts[columnKey] +=
      column.aggregation === "signed" ? value : Math.abs(value);
    row.quantifiedCounts[columnKey] += count;
    if (column.aggregation !== "signed") return;

    const details = row.amountDetails[columnKey];
    if (!details) return;
    if (value > 0) {
      details.increaseAmount += value;
      details.increaseCount += count;
    } else if (value < 0) {
      details.decreaseAmount += value;
      details.decreaseCount += count;
    }

    const previousValue = asFiniteNumber(
      event.previousValue,
      Number.NaN,
    );
    const currentValue = asFiniteNumber(
      event.currentValue,
      Number.NaN,
    );
    if (
      details.firstRecordedValue === null
      && Number.isFinite(previousValue)
    ) {
      details.firstRecordedValue = previousValue;
    }
    if (Number.isFinite(currentValue)) {
      details.lastRecordedValue = currentValue;
    }
  };

  for (const event of model.events) {
    const kind = event.kind.toLowerCase();
    const columnKey = columnByEvent.get(`${kind}\u001f${event.action}`);
    const count = Math.max(1, event.occurrences || 1);
    if (kind === "effect-executed") {
      if (event.attributionConfidence !== "exact") continue;
      const directSource = entityMap.get(event.sourceId);
      const triggerSource = entityMap.get(event.triggerSourceId);
      const source =
        directSource && isActivityEntity(directSource)
          ? directSource
          : triggerSource && isActivityEntity(triggerSource)
            ? triggerSource
            : null;
      if (!source) continue;
      const row = rows.get(source.id);
      if (!row) continue;
      row.triggers += count;
      if (columnKey) applyEvent(row, event, count, columnKey);
      continue;
    }
    if (kind !== "card-attribute" || !columnKey) continue;
    for (const targetId of new Set(event.targetIds)) {
      const target = entityMap.get(targetId);
      if (!target || !isActivityEntity(target)) continue;
      const row = rows.get(target.id);
      if (row) applyEvent(row, event, count, columnKey);
    }
  }
  return Array.from(rows.values());
}

export function activityRowValue(
  row: EntityActivityRow,
  key: string,
  metric: "amount" | "count",
  columnByKey: ReadonlyMap<string, ActivityColumn>,
): string | number | null {
  if (key === "entity") return asString(row.entity.name, row.entity.id);
  if (key === "triggers") return row.triggers;
  const column = columnByKey.get(key);
  if (metric === "amount" && column?.quantitative) {
    return row.quantifiedCounts[key] > 0 ? row.amounts[key] : null;
  }
  return row.counts[key] || 0;
}

export function compareActivityRows(
  left: EntityActivityRow,
  right: EntityActivityRow,
  sortState: ActivitySortState,
  columnByKey: ReadonlyMap<string, ActivityColumn>,
): number {
  const leftValue = activityRowValue(
    left,
    sortState.key,
    sortState.metric,
    columnByKey,
  );
  const rightValue = activityRowValue(
    right,
    sortState.key,
    sortState.metric,
    columnByKey,
  );
  let primary = 0;
  if (leftValue === null || rightValue === null) {
    primary =
      leftValue === rightValue ? 0 : leftValue === null ? 1 : -1;
  } else {
    primary =
      typeof leftValue === "string"
        ? leftValue.localeCompare(String(rightValue))
        : leftValue - Number(rightValue);
    if (sortState.direction === "desc") primary *= -1;
  }
  if (primary !== 0) return primary;

  return (
    (left.side === right.side ? 0 : left.side === "player" ? -1 : 1)
    || asString(left.entity.type, "").localeCompare(
      asString(right.entity.type, ""),
    )
    || asString(left.entity.name, left.entity.id).localeCompare(
      asString(right.entity.name, right.entity.id),
    )
    || left.entity.id.localeCompare(right.entity.id)
  );
}
