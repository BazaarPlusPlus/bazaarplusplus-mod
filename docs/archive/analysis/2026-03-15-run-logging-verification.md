# Run Logging Verification

## Automated

- `dotnet run --project tests/RunLoggingCapture.Tests/RunLoggingCapture.Tests.csproj`
- `dotnet build BazaarPlusPlus.csproj`

## Manual

1. Start a fresh run and verify a new `run_id` directory appears under `BepInEx/config/BazaarPlusPlus/run-logs/<date>/`.
2. Confirm `meta.json` exists for that run and contains the expected `run_id`.
3. Advance to an encounter or supported choice screen and verify `events.ndjson` appends `selection_seen`.
4. Force-close or stop the game before run completion, relaunch, and verify the next session resumes the active run instead of creating a second directory.
5. Finish the run or leave it so the module writes `status.json`, then verify the terminal status file exists and `active-run.json` is cleared.

## Status

- Automated checks: pending Task 11 full verification.
- Manual runtime checks: not yet executed in this workspace.
