import { defineConfig, devices } from "@playwright/test";

const sharedUse = {
  viewport: { width: 1300, height: 857 },
  deviceScaleFactor: 2,
  locale: "en-US",
  testIdAttribute: "data-bpp-test-id",
  screenshot: "only-on-failure",
  trace: "retain-on-failure",
};

export default defineConfig({
  testDir: "./tests",
  testMatch: "**/*.behavior.spec.mjs",
  outputDir: "./test-results",
  fullyParallel: false,
  workers: 1,
  forbidOnly: true,
  reporter: [["list"]],
  projects: [
    {
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        ...sharedUse,
      },
    },
    {
      name: "webkit",
      use: {
        ...devices["Desktop Safari"],
        ...sharedUse,
      },
    },
  ],
});
