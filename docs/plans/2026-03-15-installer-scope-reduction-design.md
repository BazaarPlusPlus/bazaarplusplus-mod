# Installer Scope Reduction Design

**Date:** 2026-03-15

## Goal

Shrink the BazaarPlusPlus installer into a one-time installation utility and move all runtime mod configuration into the game itself.

## Product Direction

The installer should only handle:

- environment detection
- install or reinstall
- uninstall
- launch game
- version and informational pages

The installer should no longer handle:

- reading or writing `BazaarPlusPlus.cfg`
- exposing plugin feature toggles
- teaching the user to return to the installer after install for configuration

## Decision

Remove the standalone settings page entirely and remove the installer's config read/write backend commands.

This is the recommended option because it aligns the UI structure with the product's actual job. The current `/settings` route behaves as a runtime mod control surface, which conflicts with the desired positioning of the installer as a one-time tool.

## Scope

### Remove

- `bppinstaller/src/routes/settings/+page.svelte`
- all `/settings` links and entry points from the installer UI
- Tauri config commands that read or write `BazaarPlusPlus.cfg`
- settings-related i18n keys and UI text

### Keep

- install, reinstall, uninstall, detect, and launch flows
- `About` and `What's New`
- feature descriptions, but only as descriptive copy

## UX Changes

The home page remains the only operational screen.

Feature callouts may still mention optional features such as Combat Status Bar, but the copy must point users to in-game settings instead of a separate installer page.

Examples:

- "Enable it in BazaarPlusPlus Settings" -> "Enable it in the in-game BazaarPlusPlus settings"
- "Return to the Installer and enable it on the plugin settings page" -> "Enable it in the in-game BazaarPlusPlus settings"

## Technical Changes

### Frontend

- delete the settings route file
- remove the installed-state settings link from `bppinstaller/src/routes/+page.svelte`
- remove install confirmation copy that references later configuration in installer settings
- update affected copy in installer pages and README files
- remove unused settings-related message keys from `bppinstaller/src/lib/i18n.ts`

### Backend

- delete `bppinstaller/src-tauri/src/commands/config.rs`
- remove `config` module export from `bppinstaller/src-tauri/src/commands/mod.rs`
- remove config command imports and registrations from `bppinstaller/src-tauri/src/lib.rs`

## Risks

The main risk is not runtime breakage but stale guidance:

- leftover `/settings` links
- leftover settings-related i18n keys that hide dead code
- README or update notes still telling users to go back to the installer

## Acceptance Criteria

- no installer route, link, or button opens a settings page
- installer backend exposes no config read/write commands
- installer main flow still supports detect, install, reinstall, uninstall, and launch
- feature-related copy points users to in-game settings where applicable
- `README.md` and installer copy no longer reference returning to installer settings
- installer frontend type-check and backend tests/build checks still pass
