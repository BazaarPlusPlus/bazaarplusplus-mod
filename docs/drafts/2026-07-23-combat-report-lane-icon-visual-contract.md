# Combat report lane icon visual contract

## Background

The combat report lane has repeatedly passed functional checks (the image URL resolves and the PNG decodes) while still failing visual review. The latest failure is visible in a real report containing Honeycomb, Zarlic, Oven, Meat Tenderizer, Massive Cleaver, and Caviar:

- Small items are forced into a very narrow portrait box and become unreadable slivers.
- Medium and Large items receive wider boxes, but the native preview is then contained again inside that box, leaving an oversized frame around a small image.
- Synthetic entities such as `[Stove] Socket Effect` are `ECardType.SocketEffect` records with runtime VFX rather than collection-card art. The report projector previously mislabeled them as items, so their missing image looked like a broken item export.

This is the fourth recurrence of the same class of bug. “The PNG loaded” is no longer an acceptable completion criterion.

## Current problem

The viewer currently uses the board slot ratio as the lane thumbnail container ratio (approximately `18.6:34`, `36.1:34`, and `71.2:34` for Small, Medium, and Large) and applies `object-fit: contain` to the exported preview. In report `79db757e300b4d69aca1e5add3c4c0e6`, measured real outputs compound that loss: Small opaque content is `178×362` inside a `280×512` canvas, Medium is `346×362` inside `544×512`, and Large is `488×350` inside `1072×512`. That mixes two different semantics:

1. Board footprint communicates how many slots an item occupies.
2. A lane thumbnail must make the item recognizable at a fixed row height.

Encoding the footprint by shrinking the only visual identifier makes Small items least legible and compounds any transparent padding already present in the exported source.

## Candidate approaches

### A. Keep the current footprint-shaped box and tune CSS per item

Rejected. It creates template-specific exceptions, cannot handle new game content, and repeats the “whack-a-mole” failure mode.

### B. Crop every cached source image to its alpha bounds

Useful only if measurement proves that transparent source padding is the dominant loss. Cropping alone cannot fix the Small-item container being narrower than a readable thumbnail. Any crop must happen in the materialization/cache pipeline, be represented by a renderer-versioned render key, and never mutate an existing content-addressed object.

### C. Derive the lane footprint from the alpha-cropped native preview

Selected contract after measuring the real assets:

- Hero and skill remain square/circular at the row’s icon size.
- Item export removes transparent canvas padding but keeps the complete native card frame, tier, and enchantment treatment.
- Item lane width is calculated from the decoded cropped PNG’s `naturalWidth / naturalHeight`; it is not guessed from the original render canvas or a template-specific table.
- Small/Medium/Large therefore retain their native shape without an empty synthetic container.
- Socket effects retain their report identity as effects and use an explicit effect marker; missing item assets use a distinct missing-item marker. Neither path guesses a native image.

This keeps recognition and board geometry in the same native asset while making future templates work without per-item CSS.

### D. Cache a dedicated lane-thumbnail variant

Use only if the native cached preview cannot meet the visual contract after C. The cache key must include an explicit render variant (for example `lane-thumbnail-v1`) plus the existing game build, renderer version, asset type, stable template ID, size, tier, enchantment, variant, and visual attributes. A cache miss may call the Unity materializer; the second identical report must call it zero times.

## Verification method

Measure a real report, not fixture-only HTML, across Hero, Small, Medium, Large, and Skill:

1. Record the DOM thumbnail viewport rectangle and decoded PNG natural dimensions.
2. Measure the PNG alpha bounding box and its occupancy on both axes.
3. Measure the visible content rectangle after CSS layout.
4. Assert no non-uniform stretching and no clipping of the alpha bounds.
5. At 100% lane zoom, assert the item height is 40 px and the resulting width is at least 18 px for Small, 32 px for Medium, and 48 px for Large; Hero and Skill remain at least 32 px on both axes.
6. Assert alpha content occupies at least 85% of both axes of the cropped output (including the deliberate 8 px safety padding), while Small/Medium/Large remain visibly distinct.
7. Generate the same lineup twice and assert the second run invokes the Unity materializer zero times.
8. Capture a real 838×857 screenshot and review Hero, Small, Medium, Large, and Skill bounding boxes rather than only checking network/image decode success.

For the measured report, an 8 px crop margin produces `194×378`, `362×378`, and `504×366` outputs. Their alpha occupancy is at least 91.8% × 95.6%, and their 40 px-high lane widths are 20.5 px, 38.3 px, and 55.1 px respectively.

The generic thumbnail contact sheet for the reported failure was generated at `/tmp/bpp-icon-review-4x4/contact-4x4-methods.png`; it is diagnostic output and must not be committed.
