import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const currentDir = path.dirname(fileURLToPath(import.meta.url));
const installerRoot = path.resolve(currentDir, "..");
const settingsRoute = path.join(
  installerRoot,
  "src",
  "routes",
  "settings",
  "+page.svelte",
);
const homePagePath = path.join(installerRoot, "src", "routes", "+page.svelte");
const configCommandPath = path.join(
  installerRoot,
  "src-tauri",
  "src",
  "commands",
  "config.rs",
);
const readmePath = path.resolve(installerRoot, "..", "README.md");
const whatsNewPagePath = path.join(
  installerRoot,
  "src",
  "routes",
  "whats-new",
  "+page.svelte",
);

function readHomePage() {
  return fs.readFileSync(homePagePath, "utf8");
}

function readRootReadme() {
  return fs.readFileSync(readmePath, "utf8");
}

function readWhatsNewPage() {
  return fs.readFileSync(whatsNewPagePath, "utf8");
}

test("installer no longer ships a settings route", () => {
  assert.equal(fs.existsSync(settingsRoute), false);
});

test("installer no longer ships config write commands", () => {
  assert.equal(fs.existsSync(configCommandPath), false);
});

test("installer homepage no longer links to settings", () => {
  const homePage = readHomePage();

  assert.equal(homePage.includes('href="/settings'), false);
  assert.equal(homePage.includes("BazaarPlusPlus Settings"), false);
});

test("installer copy points users to in-game settings", () => {
  const homePage = readHomePage();
  const readme = readRootReadme();

  assert.equal(homePage.includes("BazaarPlusPlus Settings"), false);
  assert.equal(homePage.includes("from the in-game options menu"), true);
  assert.equal(readme.includes("plugin settings page"), false);
  assert.equal(readme.includes("返回 Installer"), false);
  assert.equal(readme.includes("游戏内的 BazaarPlusPlus 设置"), false);
});

test("whats-new page does not keep the removed subtitle style", () => {
  const whatsNewPage = readWhatsNewPage();

  assert.equal(whatsNewPage.includes(".subtitle {"), false);
});
