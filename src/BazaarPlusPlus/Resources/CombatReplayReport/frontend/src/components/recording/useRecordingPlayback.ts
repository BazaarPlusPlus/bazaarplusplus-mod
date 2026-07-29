import {
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react";
import type { ReportViewModel } from "../../model/report.ts";
import {
  getSettledTerminalAnchor,
  mapCombatToMedia,
  mapMediaToCombat,
} from "../../recording/sync.ts";
import { RECORDING_SPEEDS } from "./RecordingControls.tsx";

type FastSeekVideoElement = HTMLVideoElement & {
  fastSeek?: (time: number) => void;
};

function issueVideoSeek(
  video: HTMLVideoElement,
  mediaMs: number,
  preferFastSeek: boolean,
): boolean {
  const seconds = Math.max(0, mediaMs / 1_000);
  const fastSeek = (video as FastSeekVideoElement).fastSeek;
  if (preferFastSeek && typeof fastSeek === "function") {
    try {
      fastSeek.call(video, seconds);
      return true;
    } catch {
      // Fall through to an exact standards-based seek.
    }
  }
  video.currentTime = seconds;
  return false;
}

export function useRecordingPlayback({
  model,
  onPlaybackCombatTime,
  visible,
}: {
  model: ReportViewModel;
  onPlaybackCombatTime: (combatMs: number) => void;
  visible: boolean;
}) {
  const videoRef = useRef<HTMLVideoElement>(null);
  const scrubVideoRef = useRef<HTMLVideoElement>(null);
  const scrubLoadedRef = useRef(false);
  const scrubFailedRef = useRef(false);
  const pendingPreviewMediaMsRef = useRef<number | null>(null);
  const previewSeekInFlightRef = useRef(false);
  const previewSeekTargetMediaMsRef = useRef<number | null>(null);
  const previewSeekUsedFastRef = useRef(false);
  const previewSeekVideoRef = useRef<HTMLVideoElement | null>(null);
  const previewActiveRef = useRef(false);
  const hideScrubAfterMainSeekRef = useRef(false);
  const drainPreviewSeekRef = useRef<() => void>(() => undefined);
  const requestedMediaMsRef = useRef(Number.NaN);
  const resumeMediaMsRef = useRef(0);
  const [playing, setPlaying] = useState(false);
  const [speedIndex, setSpeedIndex] = useState(1);
  const [speedOpen, setSpeedOpen] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);
  const [mediaMs, setMediaMs] = useState(0);

  const exact = model.sync.status === "ReadyExact";
  const settledTerminalAnchor = exact
    ? getSettledTerminalAnchor(model.sync.anchors)
    : null;
  const terminalMediaMs = settledTerminalAnchor?.mediaPtsMs ?? null;

  const setScrubVisible = useCallback((visible: boolean): void => {
    const scrubVideo = scrubVideoRef.current;
    if (!scrubVideo) return;
    scrubVideo.style.display = visible ? "block" : "none";
    scrubVideo.dataset.previewActive = visible ? "true" : "false";
  }, []);

  const selectPreviewVideo = useCallback((): HTMLVideoElement | null => {
    if (
      model.scrubVideoUrl
      && scrubLoadedRef.current
      && !scrubFailedRef.current
      && scrubVideoRef.current
    ) {
      return scrubVideoRef.current;
    }
    return videoRef.current;
  }, [model.scrubVideoUrl]);

  const drainPreviewSeek = useCallback((): void => {
    const mapped = pendingPreviewMediaMsRef.current;
    if (mapped === null) return;
    const mainVideo = videoRef.current;
    const video = previewSeekVideoRef.current ?? selectPreviewVideo();
    if (
      !video
      || (mainVideo && !mainVideo.paused)
      || previewSeekInFlightRef.current
      || video.seeking
    ) {
      if (mainVideo && !mainVideo.paused) {
        pendingPreviewMediaMsRef.current = null;
      } else if (video?.seeking) {
        previewSeekInFlightRef.current = true;
      }
      return;
    }
    if (!previewActiveRef.current) {
      pendingPreviewMediaMsRef.current = null;
      return;
    }
    if (!previewSeekVideoRef.current) {
      previewSeekVideoRef.current = video;
      setScrubVisible(video === scrubVideoRef.current);
    }
    pendingPreviewMediaMsRef.current = null;
    if (Math.abs(video.currentTime * 1_000 - mapped) < 45) {
      setMediaMs(Math.max(0, video.currentTime * 1_000));
      return;
    }

    previewSeekTargetMediaMsRef.current = mapped;
    previewSeekUsedFastRef.current = issueVideoSeek(
      video,
      mapped,
      terminalMediaMs === null || mapped < terminalMediaMs - 1,
    );
    previewSeekInFlightRef.current = video.seeking;
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.recordingPreviewSeekCount += 1;
    }
    if (!previewSeekInFlightRef.current) {
      previewSeekTargetMediaMsRef.current = null;
      previewSeekUsedFastRef.current = false;
    }
  }, [selectPreviewVideo, setScrubVisible, terminalMediaMs]);
  drainPreviewSeekRef.current = drainPreviewSeek;

  const cancelPreviewSeek = useCallback((): void => {
    pendingPreviewMediaMsRef.current = null;
    previewSeekInFlightRef.current = false;
    previewSeekTargetMediaMsRef.current = null;
    previewSeekUsedFastRef.current = false;
    previewActiveRef.current = false;
    previewSeekVideoRef.current = null;
  }, []);

  const cancelPreview = useCallback((): void => {
    const hadPendingPreview =
      previewActiveRef.current
      || pendingPreviewMediaMsRef.current !== null
      || previewSeekInFlightRef.current;
    const previewVideo =
      previewSeekVideoRef.current ?? videoRef.current;
    const usedScrubVideo = previewVideo === scrubVideoRef.current;
    const previewMediaMs =
      previewVideo && Number.isFinite(previewVideo.currentTime)
        ? Math.max(0, previewVideo.currentTime * 1_000)
        : requestedMediaMsRef.current;
    cancelPreviewSeek();
    const video = videoRef.current;
    if (!hadPendingPreview || !video) {
      if (!hideScrubAfterMainSeekRef.current) {
        setScrubVisible(false);
      }
      return;
    }
    const currentMediaMs = Number.isFinite(previewMediaMs)
      ? previewMediaMs
      : Math.max(0, video.currentTime * 1_000);
    requestedMediaMsRef.current = currentMediaMs;
    resumeMediaMsRef.current = currentMediaMs;
    setMediaMs(currentMediaMs);
    if (
      usedScrubVideo
      && Math.abs(video.currentTime * 1_000 - currentMediaMs) >= 1
    ) {
      hideScrubAfterMainSeekRef.current = true;
      video.currentTime = currentMediaMs / 1_000;
      if (video.seeking) return;
    }
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
  }, [cancelPreviewSeek, setScrubVisible]);

  const seekCombatMs = useCallback((
    combatMs: number,
    preview = false,
  ): void => {
    if (!exact) return;
    const mapped = mapCombatToMedia(combatMs, model.sync.anchors);
    if (mapped === null || !Number.isFinite(mapped)) return;
    const video = videoRef.current;
    if (preview && video && !video.paused) return;
    const alreadyRequested =
      Math.abs(mapped - requestedMediaMsRef.current) < 45;
    requestedMediaMsRef.current = mapped;
    resumeMediaMsRef.current = mapped;
    if (preview) {
      const previewVideo =
        previewSeekVideoRef.current ?? selectPreviewVideo();
      if (
        alreadyRequested
        && (
          pendingPreviewMediaMsRef.current !== null
          || previewSeekInFlightRef.current
          || !previewVideo
          || Math.abs(previewVideo.currentTime * 1_000 - mapped) < 45
        )
      ) {
        return;
      }
      previewActiveRef.current = true;
      if (!previewSeekVideoRef.current) {
        previewSeekVideoRef.current = previewVideo;
        setScrubVisible(previewVideo === scrubVideoRef.current);
      }
      pendingPreviewMediaMsRef.current = mapped;
      if (!previewVideo) {
        setMediaMs(mapped);
        return;
      }
      drainPreviewSeekRef.current();
      return;
    }
    cancelPreviewSeek();
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
    setMediaMs(mapped);
    if (!video) return;
    if (
      alreadyRequested
      && Math.abs(video.currentTime * 1_000 - mapped) < 45
    ) {
      return;
    }
    video.currentTime = Math.max(0, mapped / 1_000);
  }, [
    cancelPreviewSeek,
    exact,
    model.sync.anchors,
    selectPreviewVideo,
    setScrubVisible,
  ]);

  const prepareToHide = useCallback((): void => {
    cancelPreviewSeek();
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
    const video = videoRef.current;
    if (video) {
      if (video.readyState >= 1) {
        const nextMediaMs = Math.max(0, video.currentTime * 1_000);
        resumeMediaMsRef.current = nextMediaMs;
        requestedMediaMsRef.current = nextMediaMs;
        setMediaMs(nextMediaMs);
      }
      video.pause();
    }
    setPlaying(false);
    setSpeedOpen(false);
    setLoaded(false);
    setLoadFailed(false);
  }, [cancelPreviewSeek, setScrubVisible]);

  useEffect(() => () => {
    cancelPreviewSeek();
    setScrubVisible(false);
  }, [cancelPreviewSeek, setScrubVisible]);

  useEffect(() => {
    scrubLoadedRef.current = false;
    scrubFailedRef.current = false;
    previewSeekVideoRef.current = null;
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
  }, [model.scrubVideoUrl, setScrubVisible]);

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.playbackRate = RECORDING_SPEEDS[speedIndex];
  }, [speedIndex]);

  useEffect(() => {
    if (visible) return;
    cancelPreviewSeek();
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
    setPlaying(false);
    setSpeedOpen(false);
    setLoaded(false);
    setLoadFailed(false);
  }, [cancelPreviewSeek, setScrubVisible, visible]);

  const togglePlayback = async (): Promise<void> => {
    const video = videoRef.current;
    if (!video) return;
    if (video.paused) {
      if (previewActiveRef.current) {
        cancelPreview();
      }
      setScrubVisible(false);
      await video.play().catch(() => undefined);
    } else {
      video.pause();
    }
  };

  const handleTimeUpdate = (): void => {
    const video = videoRef.current;
    if (!video) return;
    const nextMediaMs = video.currentTime * 1_000;
    if (
      settledTerminalAnchor
      && !video.paused
      && nextMediaMs >= settledTerminalAnchor.mediaPtsMs
    ) {
      const finalMediaMs = Math.max(0, settledTerminalAnchor.mediaPtsMs);
      resumeMediaMsRef.current = finalMediaMs;
      requestedMediaMsRef.current = finalMediaMs;
      setMediaMs(finalMediaMs);
      onPlaybackCombatTime(settledTerminalAnchor.combatMs);
      if (Math.abs(nextMediaMs - finalMediaMs) >= 1) {
        video.currentTime = finalMediaMs / 1_000;
      }
      video.pause();
      return;
    }
    // Timeline hover seeks a paused recording to preview that frame. Those
    // programmatic time updates must not feed back into the pinned playhead.
    // Only an actively playing recording owns playhead progression.
    if (video.paused) {
      if (
        !previewActiveRef.current
        && !previewSeekInFlightRef.current
        && pendingPreviewMediaMsRef.current === null
      ) {
        resumeMediaMsRef.current = nextMediaMs;
        requestedMediaMsRef.current = nextMediaMs;
        setMediaMs(nextMediaMs);
      }
      return;
    }
    cancelPreviewSeek();
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
    resumeMediaMsRef.current = nextMediaMs;
    requestedMediaMsRef.current = nextMediaMs;
    setMediaMs(nextMediaMs);
    if (!exact) return;
    const combatMs = mapMediaToCombat(nextMediaMs, model.sync.anchors);
    if (combatMs !== null) onPlaybackCombatTime(combatMs);
  };

  const handleSeeked = (video: HTMLVideoElement | null): void => {
    if (!video) return;

    if (
      video === videoRef.current
      && hideScrubAfterMainSeekRef.current
      && !previewActiveRef.current
    ) {
      hideScrubAfterMainSeekRef.current = false;
      setScrubVisible(false);
    }
    if (video !== previewSeekVideoRef.current) return;

    previewSeekInFlightRef.current = false;
    if (!previewActiveRef.current) {
      previewSeekTargetMediaMsRef.current = null;
      previewSeekUsedFastRef.current = false;
      return;
    }
    if (pendingPreviewMediaMsRef.current !== null) {
      drainPreviewSeekRef.current();
      return;
    }

    const target = previewSeekTargetMediaMsRef.current;
    if (
      previewSeekUsedFastRef.current
      && target !== null
      && Math.abs(video.currentTime * 1_000 - target) >= 45
    ) {
      previewSeekUsedFastRef.current = issueVideoSeek(
        video,
        target,
        false,
      );
      previewSeekInFlightRef.current = video.seeking;
      if (window.__BPP_VIEWER_TEST__) {
        window.__BPP_VIEWER_TEST__.recordingPreviewSeekCount += 1;
      }
      if (previewSeekInFlightRef.current) return;
    }

    previewSeekTargetMediaMsRef.current = null;
    previewSeekUsedFastRef.current = false;
    const currentMediaMs = Math.max(0, video.currentTime * 1_000);
    resumeMediaMsRef.current = currentMediaMs;
    requestedMediaMsRef.current = currentMediaMs;
    setMediaMs(currentMediaMs);
  };

  const handleEnded = (): void => {
    videoRef.current?.pause();
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
    setPlaying(false);
  };

  const handleLoadedMetadata = (host: HTMLElement | null): void => {
    const video = videoRef.current;
    if (host && video && video.videoWidth > 0 && video.videoHeight > 0) {
      const aspect = video.videoWidth / video.videoHeight;
      host.style.setProperty("--bpp-recording-video-aspect", `${aspect}`);
      host.dataset.videoAspect = `${video.videoWidth}:${video.videoHeight}`;
    }
    if (video) {
      cancelPreviewSeek();
      hideScrubAfterMainSeekRef.current = false;
      setScrubVisible(false);
      const requested = requestedMediaMsRef.current;
      const targetMediaMs = Number.isFinite(requested)
        ? requested
        : resumeMediaMsRef.current;
      const durationMs = Number.isFinite(video.duration)
        ? video.duration * 1_000
        : targetMediaMs;
      const clampedMediaMs = Math.max(
        0,
        Math.min(targetMediaMs, durationMs),
      );
      video.currentTime = clampedMediaMs / 1_000;
      video.playbackRate = RECORDING_SPEEDS[speedIndex];
      resumeMediaMsRef.current = clampedMediaMs;
      requestedMediaMsRef.current = clampedMediaMs;
      setMediaMs(clampedMediaMs);
    }
    setLoaded(true);
  };

  const handleScrubLoadedMetadata = (): void => {
    const scrubVideo = scrubVideoRef.current;
    if (!scrubVideo) return;
    scrubLoadedRef.current = true;
    scrubFailedRef.current = false;
    scrubVideo.pause();
    const mainVideo = videoRef.current;
    const targetMediaMs = mainVideo
      ? Math.max(0, mainVideo.currentTime * 1_000)
      : Math.max(0, resumeMediaMsRef.current);
    const durationMs = Number.isFinite(scrubVideo.duration)
      ? scrubVideo.duration * 1_000
      : targetMediaMs;
    const clampedMediaMs = Math.min(targetMediaMs, durationMs);
    if (Math.abs(scrubVideo.currentTime * 1_000 - clampedMediaMs) >= 1) {
      scrubVideo.currentTime = clampedMediaMs / 1_000;
    }
  };

  const handleScrubError = (): void => {
    scrubLoadedRef.current = false;
    scrubFailedRef.current = true;
    if (previewSeekVideoRef.current !== scrubVideoRef.current) return;
    const fallbackTarget =
      pendingPreviewMediaMsRef.current
      ?? previewSeekTargetMediaMsRef.current
      ?? requestedMediaMsRef.current;
    previewSeekVideoRef.current = videoRef.current;
    previewSeekInFlightRef.current = false;
    previewSeekTargetMediaMsRef.current = null;
    previewSeekUsedFastRef.current = false;
    pendingPreviewMediaMsRef.current = Number.isFinite(fallbackTarget)
      ? fallbackTarget
      : null;
    setScrubVisible(false);
    drainPreviewSeekRef.current();
  };

  const handlePlay = (): void => {
    cancelPreviewSeek();
    hideScrubAfterMainSeekRef.current = false;
    setScrubVisible(false);
    setPlaying(true);
  };

  return {
    cancelPreview,
    handleEnded,
    handleLoadedMetadata,
    handleSeeked,
    handleScrubError,
    handleScrubLoadedMetadata,
    handlePlay,
    handleTimeUpdate,
    loadFailed,
    loaded,
    mediaMs,
    playing,
    prepareToHide,
    seekCombatMs,
    setLoadFailed,
    setLoaded,
    setPlaying,
    setSpeedIndex,
    setSpeedOpen,
    speedIndex,
    speedOpen,
    togglePlayback,
    scrubVideoRef,
    videoRef,
  };
}
