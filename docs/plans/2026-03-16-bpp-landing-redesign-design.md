# BPP Landing Redesign Design

**Date:** 2026-03-16

## Goal

Redesign `bpp-landing` so the homepage centers on two horizontal platform icons, gives Windows an in-card expandable download drawer, keeps macOS as a clear `Coming Soon` state, reduces the visual weight of creator support, and adds a separate FAQ page that shares the same visual language.

## Product Direction

The landing page should remain a focused download page, not a marketing site and not a generic release hub.

The primary reading order should be:

1. brand
2. platform selection
3. Windows download choices
4. light author/support info

The visual tone should keep the current dark brass / bazaar atmosphere, but the layout should feel tighter, more interactive, and less like a static poster.

## Decision

Keep the core dark gold visual language, but restructure the page around a launcher-like platform row:

- one `Windows` card
- one `macOS` card
- Windows expands internally to reveal download actions
- macOS stays static with `Coming Soon`
- support options move below the main platform area and become lighter-weight cards
- WeChat QR code moves into a modal so `WeChat` and `Ko-fi` live at the same hierarchy
- FAQ becomes a separate `/faq` page with matching visual language and placeholder content

This is the recommended option because it preserves the existing brand tone while making the page's main interaction more deliberate and polished.

## Scope

### Homepage

- keep bilingual support
- keep the current Worker-based HTML rendering approach
- keep Windows and macOS as the only platform entries
- replace the current large platform sections with one horizontal interactive platform row
- turn Windows download links into an in-card drawer
- reduce persistent support visual weight

### FAQ

- add a dedicated `/faq` route
- use the same palette, border treatment, typography hierarchy, and language switch
- include placeholder accordion items for later real content

### Support

- keep `WeChat` and `Ko-fi`
- remove always-visible QR code from the homepage
- show the WeChat QR code in a modal dialog

## UX Changes

### Homepage layout

- hero becomes shorter and denser
- the platform row appears immediately after the hero
- `Windows` and `macOS` appear side by side on desktop and stack on mobile
- clicking the `Windows` card toggles an internal drawer with:
  - `GitHub Release`
  - `蓝奏云`
- the active Windows state gets stronger border, glow, and motion feedback
- `macOS` remains non-expandable and shows one concise status line

### Support area

- support moves below the author/support information row
- show three lighter cards:
  - `Author`
  - `WeChat Support`
  - `Ko-fi`
- clicking `WeChat Support` opens a centered modal with QR code and small explanatory copy
- clicking the overlay, close button, or `Esc` closes the modal

### FAQ page

- route: `/faq`
- top area mirrors the homepage shell
- main content uses accordion-style placeholder entries
- include a low-friction way back to `/`

## Visual Language

### Keep

- dark background with warm metallic highlights
- serif-forward brand typography feel
- soft grain and glow atmosphere
- rounded cards with thin brass borders

### Change

- reduce oversized empty areas
- make the platform cards look more like interactive tiles than content boxes
- lower the visual dominance of support content
- make the active state and motion do more of the storytelling

## Technical Changes

### Routing

- continue serving `/` from the Worker
- add `/faq`
- keep `/favicon.png` and `/support/wechat-pay.svg`

### Rendering structure

Do not treat the "single template string" review note as a blocking refactor for this task.

Small structural cleanup is acceptable if it directly supports the redesign:

- extract homepage section renderers where useful
- add a separate FAQ renderer
- add small helper functions for modal and accordion markup

But avoid turning this into a broad template system rewrite.

### Interactivity

Homepage script should support:

- Windows drawer toggle
- WeChat modal open/close
- `Esc` to close modal

FAQ script should support:

- accordion expand/collapse for placeholder questions

## Risks

- the in-card drawer can feel cluttered if the copy is too long in both languages
- modal focus/keyboard behavior can regress accessibility if handled loosely
- preserving the current atmosphere while tightening the layout could accidentally flatten the page if spacing is over-corrected

## Acceptance Criteria

- homepage shows one horizontal Windows card and one horizontal macOS card as the main interaction
- Windows card expands internally to show `GitHub Release` and `蓝奏云`
- macOS card remains static and shows `Coming Soon`
- homepage no longer shows the WeChat QR code by default
- homepage shows `WeChat` and `Ko-fi` at the same visual hierarchy
- clicking WeChat opens a modal with the QR code
- `/faq` exists and matches the homepage visual language
- FAQ content uses placeholder accordion entries for now
- language switching still works on homepage and FAQ page
- Worker tests cover the new homepage and FAQ behaviors
