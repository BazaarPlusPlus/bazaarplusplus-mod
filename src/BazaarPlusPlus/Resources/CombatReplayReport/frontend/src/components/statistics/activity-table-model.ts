import type {
  ActivityColumn,
  ActivitySortState,
  CombatSide,
  EntityActivityRow,
} from "../../statistics/aggregate.ts";

export interface ActivityRowGroup {
  side: CombatSide;
  rows: EntityActivityRow[];
}

export function nextActivitySort(
  current: ActivitySortState,
  column: ActivityColumn | "triggers" | "entity",
): ActivitySortState {
  const key = typeof column === "string" ? column : column.key;
  const metric =
    typeof column === "string" || !column.quantitative
      ? "count"
      : "amount";
  return {
    key,
    metric,
    direction:
      current.key === key && current.direction === "desc" ? "asc" : "desc",
  };
}

export function groupActivityRows(
  rows: readonly EntityActivityRow[],
): ActivityRowGroup[] {
  return (["player", "opponent"] as const)
    .map((side) => ({
      side,
      rows: rows.filter((row) => row.side === side),
    }))
    .filter((group) => group.rows.length > 0);
}
