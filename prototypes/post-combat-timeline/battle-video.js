// PROTOTYPE — local replay-video metadata paired with the timeline fixture.
window.BATTLE_VIDEO = {
  schemaVersion: 1,
  battleId: "483f629c36564d9ca4b4f55dea584cc6",
  src: "videos/latest-battle.mp4",
  fps: 60,
  codec: "h264_videotoolbox",
  expectedDurationMs: 15333,
  sync: {
    mode: "estimated",
    clock: "piecewise-linear",
    provenance: "visual-review-of-legacy-recording",
    anchors: [
      { combatFrame: 0, combatMs: 0, mediaMs: 0 },
      { combatFrame: 29, combatMs: 1450, mediaMs: 1000 },
      { combatFrame: 170, combatMs: 8500, mediaMs: 14500 },
    ],
  },
};
