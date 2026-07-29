# Combat Log event-label truncation

## Background

The Footer workbench renders a virtualized Combat Log beside an optional 16:9
recording pane. Every row uses the same six-column grid:

1. time;
2. event kind;
3. source;
4. relation;
5. target;
6. amount.

An earlier fix replaced the original fixed `4.75rem` event-kind track with
`clamp(8rem, 20%, 12rem)`.

## Current problem

Real reports still truncate labels such as `Attribute change` and
`Cooldown reduction` even when the source and target columns contain visible
unused space.

This is a repeated failure: the first fix changed the CSS rule but did not
reproduce the complete workbench geometry reported by the user.

## Root cause

The previous browser regression used a 2048px fixture without a recording URL.
The Combat Log therefore owned the entire footer width. It also inspected only
the virtual list's initial visible rows, which contained short labels.

In the real workbench:

- the recording pane consumes more than 300px;
- the remaining log can be narrower than 700px even on a desktop viewport;
- the kind track falls to its 8rem minimum;
- that minimum does not include enough room for a native icon, the icon gap,
  and the longest English product labels;
- later virtual rows containing the long labels were never measured.

## Candidate approaches

### Let labels overflow into the source track

Rejected. It would break the shared column anchors and can overlap a recorded
source.

### Use a per-row `max-content` kind track

Rejected. Each virtual row owns an independent grid, so content-sized tracks
would move the source and target anchors from row to row.

### Increase the bounded responsive track and test the real workbench

Selected. Both parent and detail rows use
`clamp(10rem, 24%, 14rem)`. The lower bound fits the icon, gap, and long label;
the upper bound prevents the kind column from consuming unbounded space.

## Verification contract

- Use a report with a non-empty recording manifest so the right recording pane
  is present.
- Use a 1024×900 viewport and assert the log is narrower than 700px while the
  recording pane is wider than 300px.
- Put the longest event labels after enough rows to require virtual scrolling.
- Scroll to the end and assert `scrollWidth <= clientWidth` for:
  `Cooldown reduction`, `Freeze resistance`, `Slow resistance`,
  `Critical Chance`, `Damage stat`, and `Multicast`.
- Run the contract in Chromium and WebKit.
- Re-run the complete frontend typecheck, pure-module suite, and behavior suite.

## Verified outcome

- The focused workbench regression passes in Chromium and WebKit.
- All 49 pure-module tests and all 88 behavior tests pass.
- The refreshed localhost report uses a 189px kind track at 1280px and has no
  truncated visible event-kind labels.
