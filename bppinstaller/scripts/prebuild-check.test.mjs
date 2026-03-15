import test from "node:test";
import assert from "node:assert/strict";

import { requiredEntriesForPlatform } from "./prebuild-check.mjs";

test("macOS bundles BazaarPlusPlus SQLite dependencies", () => {
  assert.deepEqual(requiredEntriesForPlatform("macos"), [
    "run_bepinex.sh",
    "libdoorstop.dylib",
    "BepInEx/plugins/BazaarPlusPlus.dll",
    "BepInEx/plugins/BazaarPlusPlus.version",
    "BepInEx/plugins/Microsoft.Data.Sqlite.dll",
    "BepInEx/plugins/SQLitePCLRaw.batteries_v2.dll",
    "BepInEx/plugins/SQLitePCLRaw.core.dll",
    "BepInEx/plugins/SQLitePCLRaw.provider.e_sqlite3.dll",
    "BepInEx/plugins/libe_sqlite3.dylib",
  ]);
});

test("Windows bundles BazaarPlusPlus SQLite dependencies", () => {
  assert.deepEqual(requiredEntriesForPlatform("windows"), [
    "winhttp.dll",
    "doorstop_config.ini",
    "BepInEx/plugins/BazaarPlusPlus.dll",
    "BepInEx/plugins/BazaarPlusPlus.version",
    "BepInEx/plugins/Microsoft.Data.Sqlite.dll",
    "BepInEx/plugins/SQLitePCLRaw.batteries_v2.dll",
    "BepInEx/plugins/SQLitePCLRaw.core.dll",
    "BepInEx/plugins/SQLitePCLRaw.provider.e_sqlite3.dll",
    "BepInEx/plugins/e_sqlite3.dll",
  ]);
});
