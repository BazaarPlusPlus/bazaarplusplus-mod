# BazaarPlusPlus Native Settings Screen Design

## Status

This document is design-only.

It records a recommended future direction for BazaarPlusPlus settings if the current injected rows grow beyond the visible screen area.

It does not imply immediate implementation.

## Problem

The current BazaarPlusPlus settings integration works by cloning native rows inside `OptionsDialogController` and inserting them into the gameplay settings list.

This is good enough for a small number of rows, but it does not scale well if BazaarPlusPlus adds many more toggles or keybind rows.

Primary failure mode:

- rows can extend below the visible screen area
- the parent rect can become taller, but the surrounding UI may still not expose a usable scroll path
- layout/order becomes harder to maintain as more injected rows depend on a fragile anchor

## Current State

Current runtime behavior:

- BazaarPlusPlus clones native toggle rows in `Patches/Settings/SettingsMenuToggleInstaller.cs`
- BazaarPlusPlus clones native keybind rows in `Patches/Settings/BppKeybindSettingsPatch.cs`
- rows are positioned either by native layout rebuild or by manual anchored-position math
- the parent rect is expanded if additional rows extend below the current bounds

Current strengths:

- native look and feel is preserved because real game UI rows are reused
- implementation cost is low
- behavior is acceptable for a small number of custom settings

Current weaknesses:

- too many rows may exceed the visible area
- the injected content does not own a dedicated navigation model
- continued growth increases maintenance cost

## Goal

If BazaarPlusPlus settings grow, move from "append more rows into the native gameplay list" to "open a BazaarPlusPlus-owned secondary settings screen that still looks native."

Target result:

- visual style stays close to the base game
- content can grow without screen overflow
- layout is predictable across languages and resolutions
- BazaarPlusPlus settings are grouped in one place instead of being scattered across the native menu

## Recommended Direction

Create a dedicated `BPP Settings` entry inside the native options menu.

Selecting that entry opens a BazaarPlusPlus secondary page or overlay that:

- uses cloned native controls for rows and section headers
- uses a real scrollable content area
- uses layout-driven positioning instead of manual `anchoredPosition` stacking
- owns its own back button and title
- refreshes labels when the game language changes

This is the most robust way to keep a native-looking UI while avoiding vertical overflow.

## Why Not Keep Appending Rows

Continuing the current approach is attractive in the short term, but it becomes progressively worse as feature count rises.

Problems with continued row injection:

- every new toggle depends on the stability of native anchors
- manual placement is more fragile than layout-driven placement
- even if content height is expanded, the player may still not be able to reach the bottom of the list comfortably
- a crowded native gameplay list mixes BazaarPlusPlus features with unrelated game settings

## Proposed UX

### Entry point

Add one BazaarPlusPlus button or row from the native options screen.

Suggested labels:

- English: `BPP Settings`
- Simplified Chinese: `BPP 设置`

### Secondary screen

The BazaarPlusPlus page should include:

- title/header
- back button
- scrollable content area
- one or more sections such as `Gameplay`, `Tooltips`, `Hotkeys`, `Debug`

### Content style

Prefer cloning existing native elements:

- toggle rows for boolean settings
- keybind rows for hotkey settings
- section headers or separators where available

Do not restyle from scratch unless the native element cannot be reused.

## Layout Requirements

The BazaarPlusPlus screen should be layout-driven.

Preferred structure:

- root panel
- viewport
- scroll content container
- `VerticalLayoutGroup`
- `ContentSizeFitter` only if it behaves correctly with the chosen scroll setup

Behavior requirements:

- content must remain reachable when it exceeds screen height
- row order must not depend on custom pixel math
- long localized labels must not overlap controls
- the page must still work at smaller resolutions

## Navigation Requirements

The page should behave like a native subpage.

Requirements:

- entering the page hides or de-emphasizes the parent gameplay list
- pressing the back button returns to the previous options view
- reopening the menu should not create duplicate BazaarPlusPlus pages
- repeated language changes should update all visible BazaarPlusPlus labels in place

## Language Support

Current language codes confirmed from decompiled game code:

- `en-US`
- `de-DE`
- `pt-BR`
- `zh-CN`
- `ko-KR`

Additional locale mapping is also visible in localization utility code:

- `it-IT`
- `fr-FR`
- `es-ES`
- `ja-JP`
- `ru-RU`
- `uk-UA`

Recommendation:

- keep BazaarPlusPlus localization resolution independent from the game text database
- use code-based label maps with a small language matcher
- fall back to English for unsupported languages

## Suggested File Boundaries

If this feature is implemented later, a clean split would look like this:

- `Game/Settings/BppSettingsScreenController.cs`
- `Game/Settings/BppSettingsSectionDefinition.cs`
- `Game/Settings/BppSettingsRowFactory.cs`
- `Patches/Settings/BppSettingsEntryPatch.cs`
- `Patches/Settings/BppSettingsScreenPatch.cs`

Potential reuse from the current code:

- `Game/Settings/SettingsMenuToggleBridge.cs`
- `Game/Settings/SettingsMenuToggleDefinition.cs`
- `Patches/Settings/SettingsMenuToggleInstaller.cs`
- `Game/Input/BppKeyBindRowController.cs`

The row-bridge logic can stay mostly intact even if the container/screen architecture changes.

## Implementation Strategy

Recommended order:

1. Add one native-looking `BPP Settings` entry.
2. Open a BazaarPlusPlus secondary panel with a working back path.
3. Move existing BazaarPlusPlus toggles into that panel.
4. Move BazaarPlusPlus keybind rows into the same panel.
5. Convert layout to a real scrollable content container.
6. Add section grouping and language-refresh support.

This sequence reduces risk because the entry and navigation path are validated before migrating all settings.

## Non-Goals

This design does not aim to:

- replace the entire native options menu
- localize through the game's internal translation database
- expose every BazaarPlusPlus debug feature in release builds
- support arbitrary runtime UI skinning

## Risks

### Native prefab drift

Future game updates may change the structure of the native options UI.

Mitigation:

- keep anchor lookup defensive
- log failures clearly
- isolate BazaarPlusPlus screen construction from the rest of the mod

### Scroll container mismatch

If BazaarPlusPlus attaches to the wrong viewport or content root, content may still clip or scroll incorrectly.

Mitigation:

- verify the actual runtime hierarchy before implementation
- prefer creating one isolated BazaarPlusPlus content root over mutating multiple existing containers

### Localization growth

More supported languages increase label maintenance cost.

Mitigation:

- keep one shared language matcher
- centralize BazaarPlusPlus label resolution

## Recommendation

When BazaarPlusPlus needs more than a handful of settings rows, stop extending the native gameplay list directly.

Instead:

- keep one lightweight entry in the native options menu
- move BazaarPlusPlus settings into a dedicated native-looking secondary page
- make that page scrollable and layout-driven

That is the cleanest way to prevent screen overflow while preserving a UI style close to the base game.
