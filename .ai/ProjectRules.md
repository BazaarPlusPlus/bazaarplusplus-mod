# Project Rules

- Choose verification proportional to the change:
  - docs-only changes do not require `BuildAll`
  - targeted code changes should run the smallest relevant build or test project first
  - run the full `BuildAll` target when touching build logic, packaging, or before release-oriented validation
- In project documentation, write paths as relative paths from the project root.
- On Windows, do not rely on PowerShell default encoding for Chinese text; use explicit UTF-8 when reading or writing files.
- On Windows, when a command or script needs Chinese text, use a UTF-8-safe path such as file edits or a small script instead of embedding complex Chinese text directly in shell command lines.
