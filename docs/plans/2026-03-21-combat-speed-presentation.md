# Combat Speed Presentation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Restructure the player-facing HTML PPT so it explains combat speed as a playback-only change using a four-slide conclusion-first narrative.

**Architecture:** Keep the presentation as one standalone HTML file. Replace the repeated explanation slides with a tighter four-slide sequence, then adjust the supporting CSS classes so the new flow diagram and comparison blocks remain visually clear on common viewport sizes.

**Tech Stack:** Static HTML, CSS, vanilla JavaScript slide controller

---

### Task 1: Document the approved narrative

**Files:**
- Create: `docs/plans/2026-03-21-combat-speed-presentation-design.md`
- Modify: `docs/plans/2026-03-21-combat-speed-presentation.md`

**Step 1: Write the design note**

Capture the agreed audience, core message, four-slide structure, and first-fight acceleration note in the design document.

**Step 2: Review the note for scope**

Check that the note stays player-facing and does not drift into implementation-detail-heavy copy.

### Task 2: Refactor the presentation content

**Files:**
- Modify: `bazaar-client-server-presentation.html`

**Step 1: Replace the current five-slide narrative with four slides**

- Slide 1: conclusion-first claim
- Slide 2: server-to-client playback pipeline
- Slide 3: changed vs unchanged comparison
- Slide 4: reuse of native speed path and fix note

**Step 2: Add only the CSS needed for the new layouts**

- Introduce a simple pipeline layout
- Introduce a compact callout style for the fix note
- Remove or ignore old content structures that are no longer used

**Step 3: Keep technical anchors lightweight**

Mention `CombatSimHandler.SetSpeed` only as supporting detail rather than headline content.

### Task 3: Verify structure and wording

**Files:**
- Modify: `bazaar-client-server-presentation.html`

**Step 1: Inspect the updated slide headings and section count**

Run text searches to confirm there are four slides and the new titles are present.

**Step 2: Review for player-facing wording**

Check that the presentation leads with judgments players care about and that technical nouns no longer dominate the slides.

**Step 3: Do a final HTML sanity pass**

Read the updated file around the modified sections to catch obvious structural mistakes.
