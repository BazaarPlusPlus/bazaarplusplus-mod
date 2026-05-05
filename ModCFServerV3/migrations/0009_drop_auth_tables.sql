-- Migration 0009: drop V3 auth (users + tokens).
--
-- Login is being removed end-to-end. tokens.player_account_id REFERENCES
-- users(player_account_id), so tokens drops first. tokens_by_user is dropped
-- defensively before the table; SQLite would also drop it implicitly.

DROP INDEX IF EXISTS tokens_by_user;
DROP TABLE IF EXISTS tokens;
DROP TABLE IF EXISTS users;
