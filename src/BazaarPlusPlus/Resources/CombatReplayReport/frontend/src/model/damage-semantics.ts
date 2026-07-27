import type { NormalizedEvent } from "./normalize.ts";
import { asString } from "./value.ts";

export type DamageKind = "direct" | "burn" | "poison" | "other";

export function damageKindFromType(value: unknown): DamageKind {
  const normalized = asString(value, "").toLowerCase();
  if (normalized.includes("burn")) return "burn";
  if (normalized.includes("poison")) return "poison";
  if (
    normalized === ""
    || normalized === "damage"
    || normalized === "crit"
  ) {
    return "direct";
  }
  return "other";
}

export function eventDamageKind(
  event: Pick<NormalizedEvent, "kind" | "action">,
): DamageKind | null {
  const kind = event.kind.toLowerCase();
  const action = event.action.toLowerCase();
  if (kind === "health") {
    const separator = action.indexOf(":");
    return damageKindFromType(
      separator >= 0 ? action.slice(separator + 1) : action,
    );
  }
  if (kind !== "effect-executed") return null;
  if (action.includes("burn")) return "burn";
  if (action.includes("poison")) return "poison";
  if (
    action === "damage"
    || action === "crit"
    || action === "playerdamage"
  ) {
    return "direct";
  }
  return null;
}
