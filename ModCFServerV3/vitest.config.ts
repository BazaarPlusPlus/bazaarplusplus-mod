import path from "node:path";
import { fileURLToPath } from "node:url";

import {
  cloudflareTest,
  readD1Migrations,
} from "@cloudflare/vitest-pool-workers";
import { defineConfig } from "vitest/config";

const RootDir = path.dirname(fileURLToPath(import.meta.url));
const migrationsPath = path.join(RootDir, "migrations");

export default defineConfig({
  plugins: [
    cloudflareTest(async () => ({
      main: "./src/index.ts",
      wrangler: {
        configPath: "./wrangler.toml",
      },
      miniflare: {
        bindings: {
          TEST_MIGRATIONS: await readD1Migrations(migrationsPath),
          REPLAY_DOWNLOAD_SECRET: "test-replay-download-secret",
          GHOST_QUERY_LOOKBACK_DAYS: "3",
          RUN_BUNDLE_RETENTION_DAYS: "5",
        },
        isolatedStorage: true,
      },
    })),
  ],
  test: {
    include: ["test/**/*.test.ts"],
    setupFiles: ["./test/apply-migrations.ts"],
  },
});
