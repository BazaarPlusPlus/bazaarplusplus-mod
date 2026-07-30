import type { NormalizedEvent } from "./normalize.ts";

export function indexSemanticIcons(
  events: readonly NormalizedEvent[],
  catalog: ReadonlyMap<string, string> = new Map(),
): ReadonlyMap<string, string> {
  const index = new Map(catalog);
  for (const event of events) {
    if (
      event.iconSemanticKey
      && event.icon
      && !index.has(event.iconSemanticKey)
    ) {
      index.set(event.iconSemanticKey, event.icon);
    }
  }
  return index;
}
