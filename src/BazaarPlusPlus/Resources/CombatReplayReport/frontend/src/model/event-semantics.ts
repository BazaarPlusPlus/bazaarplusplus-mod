import { eventDamageKind } from "./damage-semantics.ts";
import type { NormalizedEvent } from "./normalize.ts";

export interface EventPresentation {
  groupKey: string;
  labelKey: string;
  token: string;
}

export type AttributeClass =
  | "state"
  | "modifier"
  | "economy"
  | "diagnostic"
  | "unknown";

export type AttributeTimelinePolicy = "hidden" | "marker" | "range";

export interface AttributeEventPolicy {
  attributeClass: AttributeClass;
  inspectorVisible: boolean;
  statisticsVisible: boolean;
  timeline: AttributeTimelinePolicy;
}

export interface CardAttributeSemantic {
  action: string;
  activityKey?: string;
  labelKey: string;
  nativeSemanticKey?: string;
  token: string;
  timelineVisible: boolean;
  valueKind: "number" | "percent";
}

export interface PlayerAttributeSemantic {
  action: string;
  labelKey: string;
  nativeSemanticKey?: string;
  token: string;
  valueKind: "number" | "percent";
}

const PLAYER_STATE_ACTIONS = [
  "Burn",
  "Joy",
  "Health",
  "HealthRegen",
  "Poison",
  "Shield",
  "Rage",
  "Enraged",
  "EnragedDuration",
  "Tempo",
] as const;

const PLAYER_MODIFIER_ACTIONS = [
  "CritChance",
  "DamageCrit",
  "JoyCrit",
  "HealthMax",
  "HealAmount",
  "HealCrit",
  "ShieldCrit",
  "FlatDamageReduction",
  "PercentDamageReduction",
  "RageMax",
  "EnragedDurationMax",
  "TempoGainCooldownMax",
  "FlatTempoGainCooldownReduction",
  "PercentTempoGainCooldownReduction",
] as const;

const PLAYER_ECONOMY_ACTIONS = [
  "Experience",
  "Gold",
  "Income",
  "Prestige",
  "Level",
  "RerollCostModifier",
] as const;

const PLAYER_DIAGNOSTIC_ACTIONS = [
  "Custom_0",
  "Custom_1",
  "Custom_2",
  "Custom_3",
  "Custom_4",
  "Custom_5",
  "Custom_6",
  "Custom_7",
  "Custom_8",
  "Custom_9",
] as const;

const CARD_STATE_ACTIONS = [
  "Cooldown",
  "Haste",
  "Slow",
  "Freeze",
  "Flying",
  "Heated",
  "Chilled",
  "CooldownDisabled",
] as const;

const CARD_MODIFIER_ACTIONS = [
  "Ammo",
  "AmmoMax",
  "ReloadAmount",
  "ReloadTargets",
  "CooldownMax",
  "ChargeAmount",
  "ChargeTargets",
  "HasteAmount",
  "HasteTargets",
  "SlowAmount",
  "SlowTargets",
  "FreezeAmount",
  "FreezeTargets",
  "BurnApplyAmount",
  "BurnRemoveAmount",
  "PoisonApplyAmount",
  "PoisonRemoveAmount",
  "Multicast",
  "Lifesteal",
  "CritChance",
  "DamageAmount",
  "DamageCrit",
  "HealAmount",
  "HealCrit",
  "JoyApplyAmount",
  "JoyRemoveAmount",
  "JoyCrit",
  "ShieldApplyAmount",
  "ShieldRemoveAmount",
  "ShieldCrit",
  "ForceUseTargets",
  "EnchantTargets",
  "UpgradeTargets",
  "DisableTargets",
  "RepairTargets",
  "BurnCrit",
  "PoisonCrit",
  "DestroyTargets",
  "RegenApplyAmount",
  "RegenRemoveAmount",
  "RegenCrit",
  "TransformTargets",
  "FlatCooldownReduction",
  "PercentCooldownReduction",
  "EnchantRemoveTargets",
  "FlyingTargets",
  "PercentChargeReduction",
  "PercentHasteReduction",
  "PercentSlowReduction",
  "PercentFreezeReduction",
  "DestroyImmunity",
  "RageApplyAmount",
  "RageRemoveAmount",
  "TempoCost",
  "FlatTempoCostReduction",
  "PercentTempoCostReduction",
] as const;

const CARD_ECONOMY_ACTIONS = ["BuyPrice", "SellPrice"] as const;

const CARD_DIAGNOSTIC_ACTIONS = [
  "Counter",
  "Custom_0",
  "Custom_1",
  "Custom_2",
  "Custom_3",
  "Custom_4",
  "Custom_5",
  "Custom_6",
  "Custom_7",
  "Custom_8",
  "QuestCompletedCount",
  "Quest_1",
  "Quest_2",
  "Quest_3",
  "Quest_4",
  "Quest_5",
  "Quest_6",
  "Quest_7",
  "Quest_8",
  "Quest_9",
  "Quest_10",
  "Quest_11",
  "Quest_12",
] as const;

export const PLAYER_ATTRIBUTE_ACTIONS = [
  ...PLAYER_STATE_ACTIONS,
  ...PLAYER_MODIFIER_ACTIONS,
  ...PLAYER_ECONOMY_ACTIONS,
  ...PLAYER_DIAGNOSTIC_ACTIONS,
] as const;

export const CARD_ATTRIBUTE_ACTIONS = [
  ...CARD_STATE_ACTIONS,
  ...CARD_MODIFIER_ACTIONS,
  ...CARD_ECONOMY_ACTIONS,
  ...CARD_DIAGNOSTIC_ACTIONS,
] as const;

const PLAYER_STATE_SET = new Set<string>(PLAYER_STATE_ACTIONS);
const PLAYER_MODIFIER_SET = new Set<string>(PLAYER_MODIFIER_ACTIONS);
const PLAYER_ECONOMY_SET = new Set<string>(PLAYER_ECONOMY_ACTIONS);
const PLAYER_DIAGNOSTIC_SET = new Set<string>(PLAYER_DIAGNOSTIC_ACTIONS);
const CARD_STATE_SET = new Set<string>(CARD_STATE_ACTIONS);
const CARD_MODIFIER_SET = new Set<string>(CARD_MODIFIER_ACTIONS);
const CARD_ECONOMY_SET = new Set<string>(CARD_ECONOMY_ACTIONS);
const CARD_DIAGNOSTIC_SET = new Set<string>(CARD_DIAGNOSTIC_ACTIONS);
const CARD_RANGE_SET = new Set<string>(["Haste", "Slow", "Freeze"]);
const PLAYER_PERCENT_ATTRIBUTE_SET = new Set<string>([
  "CritChance",
  "PercentDamageReduction",
  "PercentTempoGainCooldownReduction",
]);
const PLAYER_NATIVE_SEMANTIC_KEYS: Readonly<Record<string, string>> = {
  CritChance: "status.critChance",
  DamageCrit: "status.damage",
  Experience: "status.experience",
  Gold: "status.gold",
  HealAmount: "status.heal",
  HealCrit: "status.heal",
  Income: "status.gold",
  JoyCrit: "status.joy",
  RageMax: "status.rage",
  ShieldCrit: "status.shield",
};
const CARD_NATIVE_SEMANTIC_KEYS: Readonly<Record<string, string>> = {
  Ammo: "status.ammo",
  BurnApplyAmount: "status.burn",
  BurnCrit: "status.burn",
  BurnRemoveAmount: "status.burn",
  ChargeAmount: "status.charge",
  CritChance: "status.critChance",
  DamageAmount: "status.damage",
  DamageCrit: "status.damage",
  FreezeAmount: "status.freeze",
  HasteAmount: "status.haste",
  HealAmount: "status.heal",
  HealCrit: "status.heal",
  JoyApplyAmount: "status.joy",
  JoyCrit: "status.joy",
  JoyRemoveAmount: "status.joy",
  Multicast: "status.multicast",
  PercentCooldownReduction: "status.cooldownReduction",
  PoisonApplyAmount: "status.poison",
  PoisonCrit: "status.poison",
  PoisonRemoveAmount: "status.poison",
  RageApplyAmount: "status.rage",
  RageRemoveAmount: "status.rage",
  RegenApplyAmount: "status.regen",
  RegenCrit: "status.regen",
  RegenRemoveAmount: "status.regen",
  ShieldApplyAmount: "status.shield",
  ShieldCrit: "status.shield",
  ShieldRemoveAmount: "status.shield",
  SlowAmount: "status.slow",
};
const CARD_PERCENT_ATTRIBUTE_SET = new Set<string>([
  "CritChance",
  "Lifesteal",
  "PercentChargeReduction",
  "PercentCooldownReduction",
  "PercentFreezeReduction",
  "PercentHasteReduction",
  "PercentSlowReduction",
  "PercentTempoCostReduction",
]);

const CARD_ATTRIBUTE_SEMANTICS: Readonly<
  Record<string, CardAttributeSemantic>
> = {
  Ammo: {
    action: "Ammo",
    activityKey: "ammo",
    labelKey: "attributeAmmo",
    nativeSemanticKey: "status.ammo",
    token: "attributeAmmo",
    timelineVisible: true,
    valueKind: "number",
  },
  Chilled: {
    action: "Chilled",
    labelKey: "attributeChilled",
    token: "attributeChilled",
    timelineVisible: true,
    valueKind: "number",
  },
  CritChance: {
    action: "CritChance",
    activityKey: "critChance",
    labelKey: "attributeCritChance",
    nativeSemanticKey: "status.critChance",
    token: "attributeCritChance",
    timelineVisible: true,
    valueKind: "percent",
  },
  DamageAmount: {
    action: "DamageAmount",
    activityKey: "damageModifier",
    labelKey: "attributeDamage",
    nativeSemanticKey: "status.damage",
    token: "attributeDamage",
    timelineVisible: true,
    valueKind: "number",
  },
  Multicast: {
    action: "Multicast",
    activityKey: "multicast",
    labelKey: "attributeMulticast",
    nativeSemanticKey: "status.multicast",
    token: "attributeMulticast",
    timelineVisible: true,
    valueKind: "number",
  },
  PercentCooldownReduction: {
    action: "PercentCooldownReduction",
    activityKey: "cooldownReduction",
    labelKey: "attributeCooldownReduction",
    nativeSemanticKey: "status.cooldownReduction",
    token: "attributeCooldownReduction",
    timelineVisible: true,
    valueKind: "percent",
  },
  PercentFreezeReduction: {
    action: "PercentFreezeReduction",
    labelKey: "attributeFreezeReduction",
    token: "attributeFreezeReduction",
    timelineVisible: true,
    valueKind: "percent",
  },
  PercentSlowReduction: {
    action: "PercentSlowReduction",
    labelKey: "attributeSlowReduction",
    token: "attributeSlowReduction",
    timelineVisible: true,
    valueKind: "percent",
  },
};

const PLAYER_ATTRIBUTE_SEMANTICS: Readonly<
  Record<string, PlayerAttributeSemantic>
> = {
  HealthMax: {
    action: "HealthMax",
    labelKey: "attributeHealthMax",
    nativeSemanticKey: "status.heal",
    token: "attributeHealthMax",
    valueKind: "number",
  },
};

function policy(
  attributeClass: AttributeClass,
  timeline: AttributeTimelinePolicy,
  inspectorVisible: boolean,
  statisticsVisible: boolean,
): AttributeEventPolicy {
  return {
    attributeClass,
    timeline,
    inspectorVisible,
    statisticsVisible,
  };
}

const UNKNOWN_ATTRIBUTE_POLICY = policy(
  "unknown",
  "hidden",
  false,
  false,
);

export function cardAttributeSemantic(
  action: string,
): CardAttributeSemantic | null {
  const explicit = CARD_ATTRIBUTE_SEMANTICS[action];
  if (explicit) return explicit;
  if (!CARD_MODIFIER_SET.has(action) && !CARD_ECONOMY_SET.has(action)) {
    return null;
  }
  return {
    action,
    labelKey: `attribute.${action}`,
    nativeSemanticKey: CARD_NATIVE_SEMANTIC_KEYS[action],
    token: "attribute",
    timelineVisible: CARD_MODIFIER_SET.has(action),
    valueKind: CARD_PERCENT_ATTRIBUTE_SET.has(action)
      ? "percent"
      : "number",
  };
}

export function playerAttributeSemantic(
  action: string,
): PlayerAttributeSemantic | null {
  const explicit = PLAYER_ATTRIBUTE_SEMANTICS[action];
  if (explicit) return explicit;
  if (!PLAYER_MODIFIER_SET.has(action) && !PLAYER_ECONOMY_SET.has(action)) {
    return null;
  }
  return {
    action,
    labelKey: `attribute.${action}`,
    nativeSemanticKey: PLAYER_NATIVE_SEMANTIC_KEYS[action],
    token: "attribute",
    valueKind: PLAYER_PERCENT_ATTRIBUTE_SET.has(action)
      ? "percent"
      : "number",
  };
}

export function cardAttributePolicy(action: string): AttributeEventPolicy {
  if (CARD_STATE_SET.has(action)) {
    return policy(
      "state",
      CARD_RANGE_SET.has(action) ? "range" : "hidden",
      false,
      false,
    );
  }
  if (CARD_MODIFIER_SET.has(action)) {
    const semantic = cardAttributeSemantic(action);
    return policy(
      "modifier",
      semantic?.timelineVisible ? "marker" : "hidden",
      true,
      Boolean(semantic?.activityKey),
    );
  }
  if (CARD_ECONOMY_SET.has(action)) {
    return policy("economy", "hidden", true, false);
  }
  if (CARD_DIAGNOSTIC_SET.has(action)) {
    return policy("diagnostic", "hidden", false, false);
  }
  return UNKNOWN_ATTRIBUTE_POLICY;
}

export function playerAttributePolicy(action: string): AttributeEventPolicy {
  if (PLAYER_STATE_SET.has(action)) {
    return policy("state", "hidden", false, false);
  }
  if (PLAYER_MODIFIER_SET.has(action)) {
    const semantic = playerAttributeSemantic(action);
    return policy(
      "modifier",
      semantic ? "marker" : "hidden",
      true,
      false,
    );
  }
  if (PLAYER_ECONOMY_SET.has(action)) {
    return policy("economy", "hidden", true, false);
  }
  if (PLAYER_DIAGNOSTIC_SET.has(action)) {
    return policy("diagnostic", "hidden", false, false);
  }
  return UNKNOWN_ATTRIBUTE_POLICY;
}

export function eventAttributePolicy(
  event: Pick<
    NormalizedEvent,
    "kind" | "action" | "resolvedAttributeAction"
  >,
): AttributeEventPolicy {
  if (event.resolvedAttributeAction) {
    return cardAttributePolicy(event.resolvedAttributeAction);
  }
  const kind = event.kind.toLowerCase();
  if (kind === "card-attribute") {
    return cardAttributePolicy(event.action);
  }
  if (kind === "player-attribute") {
    return playerAttributePolicy(event.action);
  }
  return UNKNOWN_ATTRIBUTE_POLICY;
}

export function eventAttributeSemantic(
  event: Pick<
    NormalizedEvent,
    "kind" | "action" | "resolvedAttributeAction"
  >,
): CardAttributeSemantic | PlayerAttributeSemantic | null {
  if (event.resolvedAttributeAction) {
    return cardAttributeSemantic(event.resolvedAttributeAction);
  }
  const kind = event.kind.toLowerCase();
  if (kind === "card-attribute") {
    return cardAttributeSemantic(event.action);
  }
  if (kind === "player-attribute") {
    return playerAttributeSemantic(event.action);
  }
  return null;
}

export function baseEventKindToken(kind: unknown): string {
  const normalized = String(kind ?? "").toLowerCase();
  if (
    normalized.includes("damage")
    || normalized.includes("burn")
    || normalized.includes("poison")
  ) {
    return "damage";
  }
  if (
    normalized.includes("heal")
    || normalized.includes("regen")
    || normalized.includes("restore")
  ) {
    return "heal";
  }
  if (normalized.includes("shield")) return "shield";
  if (normalized.includes("charge")) return "charge";
  if (normalized.includes("haste") || normalized.includes("speedup")) {
    return "haste";
  }
  if (normalized.includes("slow")) return "slow";
  if (normalized.includes("freeze")) return "freeze";
  if (normalized.includes("skill")) return "skill";
  if (normalized.includes("trigger")) return "trigger";
  return "status";
}

export function eventPresentation(
  event: Pick<
    NormalizedEvent,
    "kind" | "action" | "resolvedAttributeAction"
  >,
): EventPresentation {
  const kind = event.kind.toLowerCase();
  if (kind === "combatant-died") {
    const action = event.action.toLowerCase();
    return {
      groupKey: "defeat",
      labelKey:
        action.endsWith(":direct")
          ? "defeatDirect"
          : action.endsWith(":burn")
            ? "defeatBurn"
            : action.endsWith(":poison")
              ? "defeatPoison"
              : action.endsWith(":other")
                ? "defeatOther"
                : "defeat",
      token: "defeat",
    };
  }

  const damageKind = eventDamageKind(event);
  if (damageKind) {
    return {
      groupKey: `damage-${damageKind}`,
      labelKey:
        damageKind === "direct"
          ? "damageDirect"
          : damageKind === "burn"
            ? "damageBurn"
            : damageKind === "poison"
              ? "damagePoison"
              : "damageOther",
      token:
        damageKind === "burn" || damageKind === "poison"
          ? damageKind
          : "damage",
    };
  }

  if (event.resolvedAttributeAction) {
    const semantic = cardAttributeSemantic(event.resolvedAttributeAction);
    return {
      groupKey:
        event.action === "CardReload"
          ? "attribute-reload"
          : `attribute-${event.resolvedAttributeAction}`,
      labelKey:
        event.action === "CardReload"
          ? "reload"
          : semantic?.labelKey ?? "attribute",
      token: semantic?.token ?? "attribute",
    };
  }
  if (
    kind === "effect-executed"
    && (event.action === "CardDisable" || event.action === "CardDestroy")
  ) {
    return {
      groupKey: "destroy",
      labelKey: "destroy",
      token: "destroy",
    };
  }

  if (kind === "card-attribute") {
    const semantic = cardAttributeSemantic(event.action);
    if (semantic) {
      return {
        groupKey: `attribute-${event.action}`,
        labelKey: semantic.labelKey,
        token: semantic.token,
      };
    }
    return {
      groupKey: `attribute-${event.action || "unknown"}`,
      labelKey: "attribute",
      token: "attribute",
    };
  }

  if (kind === "player-attribute") {
    const semantic = playerAttributeSemantic(event.action);
    if (semantic) {
      return {
        groupKey: `player-attribute-${event.action}`,
        labelKey: semantic.labelKey,
        token: semantic.token,
      };
    }
  }

  const token = baseEventKindToken(`${event.kind} ${event.action}`);
  return { groupKey: token, labelKey: token, token };
}

export function timelinePresentationToken(
  event: Pick<
    NormalizedEvent,
    "kind" | "action" | "resolvedAttributeAction"
  >,
): string {
  if (event.resolvedAttributeAction) return "attribute";
  if (event.kind.toLowerCase() === "card-attribute") {
    return "attribute";
  }
  const playerAttribute = event.kind.toLowerCase() === "player-attribute"
    ? playerAttributeSemantic(event.action)
    : null;
  if (playerAttribute) return playerAttribute.token;
  const damageKind = eventDamageKind(event);
  if (damageKind === "direct") return "damageDirect";
  if (damageKind === "burn") return "burn";
  if (damageKind === "poison") return "poison";
  if (damageKind === "other") return "damage";
  return eventPresentation(event).token;
}
