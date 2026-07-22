# Offline combat report viewer assets

`viewer.js` and `viewer.css` are the source for the current immutable Viewer release. The release registry prepends the pinned ECharts distribution to the classic Viewer IIFE and installs the result under `report-viewer/v<version>/`; the browser still loads exactly one script and one stylesheet from `file://`, with no server dependency. `viewer.css` uses only system fonts.

Committed `vN/viewer.js` and `vN/viewer.css` files are immutable source snapshots for older releases. Never edit or delete one in place: change the current sources, increment `ViewerReleaseRegistry.CurrentVersion`, add the old/current byte pins, and keep every supported generation embedded so existing reports continue to open.

The host HTML contract is intentionally small:

- Place exactly one inert `<script type="application/json" id="bpp-report-data">` in the document.
- The payload is `EmbeddedReportEnvelopeV1` with camel-case `schemaVersion`, `battleDocument`, and optional `recordingManifest` fields.
- Reference this directory's CSS and JavaScript with generation-relative URLs. Load `viewer.js` as a classic deferred script.
- Provide an optional empty element with `data-bpp-test-id="report-root"`; the viewer creates one when it is absent.
- Keep the CSP metadata before every resource reference. The viewer needs local script, style, image, and media siblings only.

Minimal payload shape:

```json
{
  "schemaVersion": 1,
  "locale": "zh-CN",
  "battleDocument": {
    "battleId": "battle-id",
    "durationMs": 8500,
    "summary": {
      "playerName": "Player",
      "opponentName": "Opponent",
      "outcome": "loss"
    },
    "entities": [],
    "events": []
  },
  "recordingManifest": {
    "battleId": "battle-id",
    "recordingId": "0123456789abcdef0123456789abcdef",
    "videoRelativeUrl": "../CombatReplayVideos/2026-07-22/0123456789abcdef0123456789abcdef.mp4",
    "syncMetadataStatus": "ReadyUnsynced",
    "syncAnchors": []
  }
}
```

`ReadyExact` is accepted only when battle identities match and at least two monotonic `{ combatMs, mediaPtsMs }` anchors are present. Otherwise the viewer fails closed to `ReadyUnsynced`: video playback remains available while automatic timeline seeking is disabled.

Timeline events are painted on one canvas and clustered per frame/lane/role, so raw events do not become one DOM node each. Routine cooldown/countdown attribute records are omitted from lane markers but remain available in the frame inspector; sustained haste/slow/freeze state is reconstructed as a flat interval. Selecting a frame opens its complete event list in bounded pages. Source endpoints are diamonds, target endpoints are circles, self-targeting endpoints combine both shapes, and unassigned endpoints are squares.

ECharts is embedded into each installed `viewer.js` release at install time. It is not copied beside individual reports and is never loaded as a second runtime request.
