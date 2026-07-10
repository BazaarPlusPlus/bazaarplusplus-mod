# Collection conventional search and IME handling

## Background

Collection search currently supports exact normalized substring matching, whitespace-compacted
matching, and ordered-subsequence fuzzy matching. Search refresh is delayed by 160 ms, but the
delay is unaware of IME composition state.

## Current problems

1. Ordered subsequences are too permissive for ordinary search. Repeated or unrelated letters
   such as `dddd` can jump across a long word and return results that do not visibly contain the
   query.
2. The debounce timer can expire while a Chinese IME composition is still active. Depending on
   the Unity UI Toolkit event sequence, an intermediate composition string can therefore refresh
   the grid before the user commits a candidate.
3. Removing arbitrary Latin subsequences also removed useful title initialisms such as `mbb` for
   `Molten Ball Blaster`.
4. Text search previously short-circuited `CollectionQuery` and ignored selected source, hero,
   day, quality, size, tag, and keyword filters.

## Matching options

### A. Contiguous substring only

Require Latin query terms to occur contiguously after Unicode normalization. Keep the existing
CJK compact/ordered path so `减速充能` can match text such as `减速时，充能` without adding pinyin.
This makes Latin matching predictable while preserving useful no-space Chinese keyword searches.

### B. Substring plus bounded edit distance

Try contiguous matching first, then allow a one-edit typo within one Latin word (two edits only
for long queries). This resembles conventional fuzzy search, but still creates edge cases that
need tuning and can surprise users.

### C. Keep ordered subsequences with a score threshold

Penalize gaps and reject low-scoring matches. This preserves abbreviations such as `intnl`, but
remains harder to explain and does not satisfy the request for traditional matching as directly.

## Recommended design

Use the language-aware form of option A now:

- Latin text: case-insensitive contiguous substring matching only.
- Latin initialisms: match the exact first letters of a multi-word display name, internal name,
  or art key; do not skip letters inside words.
- Chinese text: normalized contiguous matching plus the existing compacted/ordered no-space form.
- Multiple terms: AND semantics; each term must match.
- Text search itself is another AND condition and does not bypass any selected filter.
- Continue indexing localized/authored text, InternalName, ArtKey, tags, and related metadata.
- Do not add pinyin matching.

For IME input, subscribe to the current Input System keyboard's `onIMECompositionChange` event and
pause the search debounce while its composition has characters. The text field continues showing
the OS composition UI, but the card grid keeps its last committed results. Once composition ends,
the committed value receives the normal 160 ms debounce and is applied once. Do not poll the legacy
`UnityEngine.Input.compositionString`; the game may run with new-input-only Player Settings.

## Verification

- `dddd` does not match a word merely containing four separated `d` characters.
- `lightr` no longer matches `lighter`; `light` still does through contiguous substring matching.
- English matching remains case-insensitive.
- `mbb` matches `Molten Ball Blaster`, while `mxb` and `lightr` do not.
- A text match outside the selected source/facets is excluded; a text match satisfying every
  selected filter remains visible with source offer metadata intact.
- Chinese exact and compact queries still match; no pinyin path is introduced.
- The refresh gate does not elapse while composition is active and fires once after composition
  ends.
- Run CollectionFilterEngine tests, architecture tests, and the main build.
