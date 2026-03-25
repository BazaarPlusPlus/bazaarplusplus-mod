# Combat Speed Presentation Redesign

**Goal:** Reframe the root-level HTML presentation so it explains combat speed behavior to ordinary players, not developers.

**Audience:** Players who want a clear answer to "does speed-up change battle outcomes?"

**Approved direction:** Option A, conclusion-first.

## Core message

The presentation should establish one point immediately:

`Combat speed changes local playback, not battle results.`

Everything else should support that claim with the minimum needed implementation context.

## Narrative structure

Use four slides:

1. **Conclusion first**
   - Lead with "speed changes playback, not results."
   - Secondary line: when the animation starts, the result is already determined.

2. **Simple causal pipeline**
   - Show a single left-to-right flow:
     `server calculates -> sends full battle playback -> client plays it locally`
   - Avoid leading with developer class names.

3. **What changes / what does not**
   - Use a direct two-column comparison.
   - Left side: playback duration, animation pace, local viewing rhythm.
   - Right side: win/loss, event order, frame contents.

4. **Reuse of the game's original acceleration path**
   - Explain that the mod hooks the existing playback speed entrypoint rather than replacing combat logic.
   - Acknowledge the earlier issue where native first-fight acceleration was overridden.
   - State that the new version preserves the game's native first-fight fast-forward behavior.

## Presentation constraints

- Optimize for spoken explanation, not self-study.
- Keep technical names as optional anchors only.
- Each slide should carry one judgment, not a chain of overlapping explanations.
- Prefer visual comparison and flow over stacked prose.

## Implementation notes

- Keep the current single-file HTML presentation architecture.
- Reuse the existing warm-paper visual style where possible.
- Simplify content before adding any new decorative structure.
