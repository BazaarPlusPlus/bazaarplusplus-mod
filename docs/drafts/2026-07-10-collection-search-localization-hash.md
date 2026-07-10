# Collection search localization hash leakage

## Background

Collection search intentionally indexes localized text, authored English fallback text, internal
names, art keys, tags, and related metadata. `TLocalizableText.Key` is an MD5-like lookup hash
used by the game's translation database.

## Problem

`CollectionLocalizationResolver.AddSearchTexts` also indexes `TLocalizableText.Key`. A query such
as `ddddddddd` can therefore fuzzy-match a card whose opaque localization hash contains nine `d`
characters. The observed card is `Gumball Machine`, title key
`69d1f37d94ddc3150f8dd44d12d53ced`.

This differs from matching meaningful internal fields such as `blighttemper`: localization hashes
have no user-facing or domain-search value.

## Fix

Exclude localization keys from title, description, and tooltip search variants. Preserve current
locale text and authored fallback text, so bilingual search behavior is unchanged.

## Verification

- Add a regression card whose localization key contains nine `d` characters and assert that
  `ddddddddd` returns no result.
- Assert that the same card remains searchable by localized/authored content.
- Run CollectionFilterEngine tests, architecture tests, and the main build.
