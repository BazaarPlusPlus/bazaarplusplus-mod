# BPP Landing Redesign Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Redesign `bpp-landing` around a two-platform interactive row, a WeChat support modal, and a new `/faq` placeholder page while preserving the current bilingual Worker deployment.

**Architecture:** Keep the Cloudflare Worker as the rendering entrypoint in `bpp-landing/src/index.ts`, but split the implementation into a few targeted render helpers inside that module. Drive the redesign with tests first: homepage structure, platform drawer markup, support modal hooks, FAQ route rendering, and language behavior. Avoid a broad template-system refactor.

**Tech Stack:** TypeScript, Cloudflare Worker runtime, HTML/CSS/inline JS string rendering, Node test runner

---

### Task 1: Lock the redesigned homepage and FAQ requirements with failing tests

**Files:**
- Modify: `bpp-landing/test/homepage.test.mjs`

**Step 1: Write the failing tests**

Add or update tests to assert:

- homepage contains one Windows platform card and one macOS platform card
- homepage contains hidden or collapsed Windows drawer content for `GitHub Release` and `蓝奏云`
- homepage contains `WeChat Support` / `微信赞赏` trigger markup instead of always-visible QR layout
- homepage includes modal shell markup for the WeChat QR code
- `/faq` returns HTML with placeholder FAQ sections in both Chinese and English

Example assertions:

```js
assert.match(html, /data-platform-card="windows"/);
assert.match(html, /data-platform-drawer="windows"/);
assert.match(html, /GitHub Release/);
assert.match(html, /蓝奏云/);
assert.match(html, /data-modal-open="wechat"/);
assert.match(html, /data-modal="wechat"/);
```

**Step 2: Run tests to verify they fail**

Run: `npm test`

Expected: FAIL because the homepage and FAQ route do not yet match the new structure.

**Step 3: Commit the failing tests**

```bash
git add bpp-landing/test/homepage.test.mjs
git commit -m "test: cover landing redesign structure"
```

### Task 2: Refactor copy and route helpers to support homepage and FAQ content

**Files:**
- Modify: `bpp-landing/src/index.ts`

**Step 1: Write minimal implementation**

Extend the locale messages to cover:

- platform-row copy
- drawer labels
- WeChat modal copy
- FAQ nav / title / placeholder questions and answers

Add helper functions for:

- route-aware page titles and descriptions
- FAQ placeholder content
- optional nav links between `/` and `/faq`

Keep this cleanup limited to supporting the redesign.

**Step 2: Run focused tests**

Run: `npm test`

Expected: some tests still FAIL because the homepage and FAQ layout has not been rebuilt yet.

**Step 3: Commit**

```bash
git add bpp-landing/src/index.ts
git commit -m "refactor: add landing redesign copy and route helpers"
```

### Task 3: Rebuild the homepage around the platform cards and support cards

**Files:**
- Modify: `bpp-landing/src/index.ts`

**Step 1: Write minimal implementation**

Replace the current main content structure with:

- denser hero shell
- one horizontal platform row with:
  - `data-platform-card="windows"`
  - `data-platform-card="macos"`
- one internal Windows drawer region:
  - `data-platform-drawer="windows"`
- lighter lower row containing:
  - author card
  - WeChat support card
  - Ko-fi card

Preserve the dark brass visual language while tightening spacing and hierarchy.

**Step 2: Add the Windows drawer behavior**

Update the inline script so clicking the Windows card:

- toggles the drawer open and closed
- updates `aria-expanded`
- applies an active-state class

**Step 3: Run tests**

Run: `npm test`

Expected: homepage structure tests PASS; FAQ tests may still FAIL.

**Step 4: Commit**

```bash
git add bpp-landing/src/index.ts
git commit -m "feat: redesign landing platform row"
```

### Task 4: Add the WeChat modal and equalize support hierarchy

**Files:**
- Modify: `bpp-landing/src/index.ts`

**Step 1: Write the failing test**

Add assertions for:

- modal trigger and modal container markup
- no persistent support-grid QR image block in the homepage body layout
- modal close affordance markup

**Step 2: Run tests to verify they fail**

Run: `npm test`

Expected: FAIL until modal markup and behavior are added.

**Step 3: Write minimal implementation**

Add:

- `WeChat Support` card trigger
- modal shell with QR image
- close button
- overlay container

Update inline script so the modal:

- opens from the WeChat trigger
- closes on close button
- closes on backdrop click
- closes on `Escape`

**Step 4: Run tests**

Run: `npm test`

Expected: PASS for modal coverage.

**Step 5: Commit**

```bash
git add bpp-landing/src/index.ts bpp-landing/test/homepage.test.mjs
git commit -m "feat: add wechat support modal"
```

### Task 5: Add the FAQ page with placeholder accordion items

**Files:**
- Modify: `bpp-landing/src/index.ts`
- Modify: `bpp-landing/test/homepage.test.mjs`

**Step 1: Write the failing test**

Add assertions that:

- `https://bazaarplusplus.com/faq` returns 200 HTML
- page contains FAQ heading and placeholder accordion items
- Chinese and English language selection still works
- FAQ page links back to homepage

Example:

```js
const response = await handleRequest(new Request("https://bazaarplusplus.com/faq?lang=en"));
assert.equal(response.status, 200);
assert.match(html, /FAQ/);
assert.match(html, /placeholder/i);
```

**Step 2: Run tests to verify they fail**

Run: `npm test`

Expected: FAIL because `/faq` is currently a 404.

**Step 3: Write minimal implementation**

Add:

- route handling for `/faq`
- FAQ page renderer with matching shell
- placeholder accordion items
- small inline script for accordion expand/collapse

**Step 4: Run tests**

Run: `npm test`

Expected: PASS for FAQ route and placeholder rendering.

**Step 5: Commit**

```bash
git add bpp-landing/src/index.ts bpp-landing/test/homepage.test.mjs
git commit -m "feat: add landing faq page"
```

### Task 6: Fix locale-aware caching for negotiated HTML

**Files:**
- Modify: `bpp-landing/src/index.ts`
- Modify: `bpp-landing/test/homepage.test.mjs`

**Step 1: Write the failing test**

Add assertions that homepage and FAQ HTML responses include:

- `content-type: text/html`
- `vary: accept-language`

**Step 2: Run tests to verify they fail**

Run: `npm test`

Expected: FAIL because the current HTML responses do not send `Vary`.

**Step 3: Write minimal implementation**

Update HTML responses so locale-negotiated pages include:

```ts
'vary': 'accept-language'
```

Keep asset routes unchanged.

**Step 4: Run tests**

Run: `npm test`

Expected: PASS for locale-caching coverage.

**Step 5: Commit**

```bash
git add bpp-landing/src/index.ts bpp-landing/test/homepage.test.mjs
git commit -m "fix: vary landing html by accept-language"
```

### Task 7: Run verification and capture visual evidence

**Files:**
- No code changes required

**Step 1: Run automated verification**

Run:

```bash
cd /Users/yxinyu/codes/BazaarPlusPlus/bpp-landing
npm test
npm run typecheck
```

Expected: PASS.

**Step 2: Run local preview**

Run:

```bash
cd /Users/yxinyu/codes/BazaarPlusPlus/bpp-landing
npm run dev -- --port 8791
```

Expected: Worker serves `/` and `/faq` locally.

**Step 3: Capture screenshots**

Capture:

- homepage zh
- homepage en
- faq zh
- faq en
- homepage with Windows drawer open
- homepage with WeChat modal open

**Step 4: Review responsive behavior**

Check:

- desktop two-card layout
- mobile stacked layout
- drawer does not overflow on narrow screens
- modal remains centered and dismissible

**Step 5: Commit**

```bash
git add bpp-landing/src/index.ts bpp-landing/test/homepage.test.mjs docs/plans/2026-03-16-bpp-landing-redesign-design.md docs/plans/2026-03-16-bpp-landing-redesign.md
git commit -m "feat: redesign bpp landing experience"
```
