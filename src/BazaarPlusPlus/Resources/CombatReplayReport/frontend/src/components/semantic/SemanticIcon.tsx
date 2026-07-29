import {
  Activity,
  CircleOff,
  CircleDot,
  ClockArrowUp,
  Flame,
  Gauge,
  HeartPulse,
  Plus,
  Shield,
  Smile,
  Snowflake,
  Sparkles,
  Skull,
  Swords,
  Zap,
  type LucideIcon,
} from "lucide-react";
import { cn } from "../../lib/utils.ts";

const ICONS: Record<string, LucideIcon> = {
  attribute: Sparkles,
  attributeAmmo: Sparkles,
  attributeChilled: Sparkles,
  attributeCooldownReduction: Sparkles,
  attributeCritChance: Sparkles,
  attributeDamage: Sparkles,
  attributeFreezeReduction: Sparkles,
  attributeHealthMax: Plus,
  attributeMulticast: Sparkles,
  attributeSlowReduction: Sparkles,
  burn: Flame,
  charge: Zap,
  damage: Swords,
  destroy: CircleOff,
  defeat: Skull,
  freeze: Snowflake,
  haste: ClockArrowUp,
  heal: Plus,
  healthRegen: HeartPulse,
  poison: CircleDot,
  joy: Smile,
  rage: Zap,
  regen: HeartPulse,
  shield: Shield,
  skill: Sparkles,
  slow: Gauge,
  status: Activity,
  trigger: Zap,
};

const COLORS: Record<string, string> = {
  damage: "text-damage",
  burn: "text-burn",
  poison: "text-poison",
  joy: "text-brand-soft",
  rage: "text-rage",
  heal: "text-heal",
  regen: "text-regen",
  shield: "text-shield",
  charge: "text-charge",
  haste: "text-haste",
  slow: "text-slow",
  freeze: "text-freeze",
  skill: "text-skill",
  attribute: "text-attribute",
  attributeAmmo: "text-attribute",
  attributeChilled: "text-freeze",
  attributeCooldownReduction: "text-attribute",
  attributeCritChance: "text-attribute",
  attributeDamage: "text-damage",
  attributeFreezeReduction: "text-freeze",
  attributeHealthMax: "text-heal",
  attributeMulticast: "text-attribute",
  attributeSlowReduction: "text-slow",
  destroy: "text-damage",
  defeat: "text-damage",
};

export function SemanticIcon({
  token,
  className,
  label,
}: {
  token: string;
  className?: string;
  label?: string;
}): React.JSX.Element {
  const Icon = ICONS[token] ?? Activity;
  return (
    <Icon
      aria-label={label}
      aria-hidden={label ? undefined : true}
      className={cn("size-icon-md shrink-0", COLORS[token], className)}
      strokeWidth={2}
    />
  );
}
