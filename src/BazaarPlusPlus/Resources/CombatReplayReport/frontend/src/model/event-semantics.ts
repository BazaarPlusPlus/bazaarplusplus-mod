import { eventDamageKind } from "./damage-semantics.ts";
import type { NormalizedEvent } from "./normalize.ts";

export interface EventPresentation {
  groupKey: string;
  labelKey: string;
  token: string;
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
    timelineVisible: false,
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

export function cardAttributeSemantic(
  action: string,
): CardAttributeSemantic | null {
  return CARD_ATTRIBUTE_SEMANTICS[action] ?? null;
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
  event: Pick<NormalizedEvent, "kind" | "action">,
): EventPresentation {
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

  const kind = event.kind.toLowerCase();
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

  const token = baseEventKindToken(`${event.kind} ${event.action}`);
  return { groupKey: token, labelKey: token, token };
}

export function timelinePresentationToken(
  event: Pick<NormalizedEvent, "kind" | "action">,
): string {
  if (event.kind.toLowerCase() === "card-attribute") {
    return "attribute";
  }
  const damageKind = eventDamageKind(event);
  if (damageKind === "direct") return "damageDirect";
  if (damageKind === "burn") return "burn";
  if (damageKind === "poison") return "poison";
  if (damageKind === "other") return "damage";
  return eventPresentation(event).token;
}
