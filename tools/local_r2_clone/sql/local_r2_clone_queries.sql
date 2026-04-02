SELECT EXISTS (
  SELECT 1
  FROM battle_replays b
  WHERE b.opponent_account_id IS NOT NULL
    AND EXISTS (
      SELECT 1
      FROM battle_replays p
      WHERE p.player_account_id = b.opponent_account_id
    )
) AS has_match;

SELECT
  battle_id,
  recorded_at_utc,
  player_account_id,
  opponent_account_id,
  result
FROM battle_replays b
WHERE b.opponent_account_id IS NOT NULL
  AND EXISTS (
    SELECT 1
    FROM battle_replays p
    WHERE p.player_account_id = b.opponent_account_id
  )
ORDER BY recorded_at_utc DESC;

SELECT
  r.run_id,
  r.status,
  r.hero_name,
  r.ended_at_utc,
  COUNT(b.battle_id) AS battle_count
FROM run_summaries_latest r
LEFT JOIN battle_replays b
  ON b.run_id = r.run_id
GROUP BY r.run_id, r.status, r.hero_name, r.ended_at_utc
ORDER BY r.ended_at_utc DESC;
