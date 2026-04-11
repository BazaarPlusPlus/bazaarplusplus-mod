# Chest Screen Input Investigation (2026-04)

## Summary

This document captures the investigation into the Bazaar++ mod causing broken mouse left/right interactions when entering the chest-related screens in The Bazaar. Multiple mitigation paths were tried during the session. None produced an acceptable result, and the changes were reverted.

Observed symptom from the user:

- Entering the chest-related screen caused mouse left/right input to malfunction.
- Mouse wheel scrolling still worked.
- The issue appeared while the mod was enabled.

## What Was Observed

From `BepInEx/LogOutput.log` and the added diagnostics, the following signals were consistently useful:

- `EventSystem.current` remained the persistent `DontDestroyOnLoad` `InputManager`.
- The chest / collection flow used additive scene transitions.
- `CollectionUIScene` objects continued to appear in pointer hit results after entering `ChestSelectScene` / `CollectionWheelScene`.
- `DefaultLoadingScene` background UI also appeared in raycast hits during the transition.
- In some phases, `currentSelectedGameObject` still referenced an object from `CollectionUIScene`.

Most important inference:

- The problem looked more like stale UI state / stale hit targets across additive scene transitions than a simple `InputAction` binding failure.

## Investigation Paths Tried

### 1. Chest-specific diagnostics

We first added diagnostics to capture:

- all active `EventSystem` instances
- current selected object
- active input modules
- Collection / Chest controller state
- UI Toolkit state for the HistoryPanel runtime UI

Useful outcome:

- confirmed the persistent `InputManager` / `EventSystem.current` split from scene-local UI state
- confirmed old scene UI still participated in raycasts

Why this did not solve the issue:

- diagnostics improved visibility only; they did not remove the underlying interference

### 2. Stale selection repair

We added an experiment to clear `EventSystem.current.currentSelectedGameObject` when it belonged to a different scene than the active chest / collection scene.

Useful outcome:

- proved stale selection existed

Why this did not solve the issue:

- the main failure started even when `currentSelectedGameObject` was already `null`
- stale selection was a secondary symptom, not the primary cause

### 3. Inactive raycaster / graphic suppression

We tried to disable inactive-scene raycasters and later only specific blocker graphics such as:

- `FadeOverlay`
- `Btn_Shadow`
- `CollectionTypes`
- `DefaultLoadingScene` blockers

Useful outcome:

- confirmed old scene UI was still intercepting hits

Why this did not solve the issue:

- broader suppression removed legitimate interaction in collection content
- narrower suppression still produced broken transitions
- the workaround was too fragile because the surviving hit targets changed by phase

### 4. Scene guards for high-risk runtime features

We added scene guards to stop mod features inside `CollectionUIScene`, `ChestSelectScene`, and `CollectionWheelScene`, including:

- HistoryPanel
- CardSetPreview
- MonsterPreviewItemBoard
- settings dock behaviors
- tooltip refresh paths
- showcase / tooltip Harmony patches

Useful outcome:

- reduced direct mod activity inside the sensitive scenes

Why this did not solve the issue:

- some mod-created UI objects had already been created earlier and could still remain present
- reducing logic did not fully isolate all previously created objects and patched behavior

### 5. Plugin scene whitelist mode

We then tried a whitelist model where the plugin only ran in a fixed set of scenes such as:

- `HeroSelectScene`
- `GameScene`
- `GameplayLoading`
- `EndOfRun`

Outside that whitelist, the plugin attempted to:

- `UnpatchSelf()`
- disable high-risk `Behaviour`s
- hide residual mod-created UI objects

Why this did not solve the issue:

- this over-isolated the plugin and removed expected mod functionality
- even with aggressive isolation, the result was not acceptable from the user’s perspective
- the approach was operationally too blunt for the current plugin architecture

## Current Assessment

The session did not produce a safe workaround that both:

- preserves the intended mod features
- and prevents chest / collection input breakage

The most defensible current assessment is:

- the issue likely involves a combination of native additive-scene UI behavior and mod-created persistent UI / patched interaction paths
- the mod architecture is currently too eager: several systems are attached globally at startup and can create or influence UI outside the exact scene where they are needed
- patch-time or startup-time global attachment is a poor fit for scene-sensitive UI flows like chest / collection

## Recommended Future Direction

If this is revisited, the next attempt should avoid incremental suppression and instead move to a stricter architecture:

1. Do not attach high-risk UI systems globally at plugin startup.
2. Instantiate feature UIs lazily only inside the scenes that actually need them.
3. Separate harmless background services from interactive UI features.
4. Make scene ownership explicit per feature instead of trying to retroactively suppress behavior after objects already exist.
5. Treat collection / chest as opt-in scenes for any preview, tooltip, overlay, or injected button feature.

## Revert Decision

Because the explored mitigations were not stable and some removed intended functionality, the changes from this investigation were reverted after this document was written.
