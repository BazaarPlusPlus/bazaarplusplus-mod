import {
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react";
import type { ReportViewModel } from "../../model/report.ts";
import {
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
  const pendingPreviewMediaMsRef = useRef<number | null>(null);
  const previewSeekInFlightRef = useRef(false);
  const previewSeekTargetMediaMsRef = useRef<number | null>(null);
  const previewSeekUsedFastRef = useRef(false);
  const previewActiveRef = useRef(false);
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

  const drainPreviewSeek = useCallback((): void => {
    const mapped = pendingPreviewMediaMsRef.current;
    if (mapped === null) return;
    const video = videoRef.current;
    if (
      !video
      || !video.paused
      || previewSeekInFlightRef.current
      || video.seeking
    ) {
      if (video && !video.paused) {
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
    pendingPreviewMediaMsRef.current = null;
    if (Math.abs(video.currentTime * 1_000 - mapped) < 45) {
      setMediaMs(Math.max(0, video.currentTime * 1_000));
      return;
    }

    previewSeekTargetMediaMsRef.current = mapped;
    previewSeekUsedFastRef.current = issueVideoSeek(
      video,
      mapped,
      true,
    );
    previewSeekInFlightRef.current = video.seeking;
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.recordingPreviewSeekCount += 1;
    }
    if (!previewSeekInFlightRef.current) {
      previewSeekTargetMediaMsRef.current = null;
      previewSeekUsedFastRef.current = false;
    }
  }, []);
  drainPreviewSeekRef.current = drainPreviewSeek;

  const cancelPreviewSeek = useCallback((): void => {
    pendingPreviewMediaMsRef.current = null;
    previewSeekInFlightRef.current = videoRef.current?.seeking ?? false;
    previewSeekTargetMediaMsRef.current = null;
    previewSeekUsedFastRef.current = false;
    previewActiveRef.current = false;
  }, []);

  const cancelPreview = useCallback((): void => {
    const hadPendingPreview =
      previewActiveRef.current
      || pendingPreviewMediaMsRef.current !== null
      || previewSeekInFlightRef.current;
    cancelPreviewSeek();
    const video = videoRef.current;
    if (!hadPendingPreview || !video) return;
    const currentMediaMs = Math.max(0, video.currentTime * 1_000);
    requestedMediaMsRef.current = currentMediaMs;
    resumeMediaMsRef.current = currentMediaMs;
    setMediaMs(currentMediaMs);
  }, [cancelPreviewSeek]);

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
      if (
        alreadyRequested
        && (
          pendingPreviewMediaMsRef.current !== null
          || previewSeekInFlightRef.current
          || !video
          || Math.abs(video.currentTime * 1_000 - mapped) < 45
        )
      ) {
        return;
      }
      previewActiveRef.current = true;
      pendingPreviewMediaMsRef.current = mapped;
      if (!video) {
        setMediaMs(mapped);
        return;
      }
      drainPreviewSeekRef.current();
      return;
    }
    cancelPreviewSeek();
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
  ]);

  const prepareToHide = useCallback((): void => {
    cancelPreviewSeek();
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
  }, [cancelPreviewSeek]);

  useEffect(
    () => cancelPreviewSeek,
    [cancelPreviewSeek],
  );

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.playbackRate = RECORDING_SPEEDS[speedIndex];
  }, [speedIndex]);

  useEffect(() => {
    if (visible) return;
    cancelPreviewSeek();
    setPlaying(false);
    setSpeedOpen(false);
    setLoaded(false);
    setLoadFailed(false);
  }, [cancelPreviewSeek, visible]);

  const togglePlayback = async (): Promise<void> => {
    const video = videoRef.current;
    if (!video) return;
    if (video.paused) {
      await video.play().catch(() => undefined);
    } else {
      video.pause();
    }
  };

  const handleTimeUpdate = (): void => {
    const video = videoRef.current;
    if (!video) return;
    const nextMediaMs = video.currentTime * 1_000;
    const finalAnchor = exact
      ? model.sync.anchors[model.sync.anchors.length - 1]
      : undefined;
    if (
      finalAnchor
      && !video.paused
      && nextMediaMs >= finalAnchor.mediaPtsMs
    ) {
      const finalMediaMs = Math.max(0, finalAnchor.mediaPtsMs);
      resumeMediaMsRef.current = finalMediaMs;
      requestedMediaMsRef.current = finalMediaMs;
      setMediaMs(finalMediaMs);
      onPlaybackCombatTime(finalAnchor.combatMs);
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
    resumeMediaMsRef.current = nextMediaMs;
    requestedMediaMsRef.current = nextMediaMs;
    setMediaMs(nextMediaMs);
    if (!exact) return;
    const combatMs = mapMediaToCombat(nextMediaMs, model.sync.anchors);
    if (combatMs !== null) onPlaybackCombatTime(combatMs);
  };

  const handleSeeked = (): void => {
    const video = videoRef.current;
    if (!video) return;

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

  return {
    cancelPreview,
    handleEnded,
    handleLoadedMetadata,
    handleSeeked,
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
    videoRef,
  };
}
