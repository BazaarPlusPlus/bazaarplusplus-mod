/// <reference types="@cloudflare/vitest-pool-workers/types" />

declare namespace Cloudflare {
  interface Env {
    DB: D1Database;
    RUN_BUNDLE_BUCKET: R2Bucket;
    BAZAARDB_BUCKET: R2Bucket;
    REPLAY_DOWNLOAD_SECRET: string;
    BAZAARDB_PULL_TOKEN: string;
    GHOST_QUERY_LOOKBACK_DAYS: string;
    RUN_BUNDLE_RETENTION_DAYS: string;
    TEST_MIGRATIONS: import("@cloudflare/vitest-pool-workers").D1Migration[];
  }
}

declare module "*.sql?raw" {
  const content: string;
  export default content;
}
