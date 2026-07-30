# Shared Noto fonts

The content-addressed Viewer generation embeds these complete upstream
variable font files once for every report that references that Viewer:

- `noto-sans-variable.ttf` — Noto Sans variable width and weight;
  2,049,096 bytes; SHA-256
  `bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d`.
- `noto-serif-variable.ttf` — Noto Serif variable width and weight;
  1,887,192 bytes; SHA-256
  `4d8e6761424656867019081a1a01336f3cb086982682698714054fc33f782713`.

## Source

- Google Fonts collection:
  `https://github.com/google/fonts/tree/7ff85c87f93ea6cca5f41c69f2e4edcb90240f26`
- The collection metadata pins both Noto families to source commit
  `c4a321e123e4d4ff315f57f4e0adf294fe3a95be` in
  `https://github.com/notofonts/latin-greek-cyrillic`.
- Noto Sans source `NotoSans[wdth,wght].ttf`: SHA-256
  `bfb7bb691513f12e734dc346c03a03f784912432d7e3fa8e56efcf906fe86b3d`.
- Noto Serif source `NotoSerif[wdth,wght].ttf`: SHA-256
  `4d8e6761424656867019081a1a01336f3cb086982682698714054fc33f782713`.

Both upstream families carry an identical `OFL.txt` (SHA-256
`cee9892f9f0cc8fe882c9e9537ee6a89621d86ee7ceaf70b02e2b2b1c25c061a`).
The tracked copy removes trailing spaces only so it passes repository text
checks; the wording is unchanged.

The tracked TTF files are byte-for-byte copies of those immutable upstream
sources. There is no local subsetting, instancing, compression, or
font-generation pipeline. CJK glyphs continue through the Viewer's
locale-appropriate system fallback chains.
