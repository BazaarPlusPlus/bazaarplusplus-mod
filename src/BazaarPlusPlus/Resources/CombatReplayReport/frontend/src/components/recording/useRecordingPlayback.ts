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
  const previewFrameRef = useRef(0);
  const requestedMediaMsRef = useRef(Number.NaN);
  const resumeMediaMsRef = useRef(0);
  const [playing, setPlaying] = useState(false);
  const [speedIndex, setSpeedIndex] = useState(1);
  const [speedOpen, setSpeedOpen] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [loadFailed, setLoadFailed] = useState(false);
  const [mediaMs, setMediaMs] = useState(0);

  const exact = model.sync.status === "ReadyExact";

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
    setMediaMs(mapped);
    if (!video) return;
    if (
      alreadyRequested
      && Math.abs(video.currentTime * 1_000 - mapped) < 45
    ) {
      return;
    }
    const apply = (): void => {
      if (!videoRef.current) return;
      videoRef.current.currentTime = Math.max(0, mapped / 1_000);
    };
    if (preview) {
      window.cancelAnimationFrame(previewFrameRef.current);
      previewFrameRef.current = window.requestAnimationFrame(apply);
    } else {
      apply();
    }
  }, [exact, model.sync.anchors]);

  const prepareToHide = useCallback((): void => {
    window.cancelAnimationFrame(previewFrameRef.current);
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
  }, []);

  useEffect(
    () => () => window.cancelAnimationFrame(previewFrameRef.current),
    [],
  );

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.playbackRate = RECORDING_SPEEDS[speedIndex];
  }, [speedIndex]);

  useEffect(() => {
    if (visible) return;
    setPlaying(false);
    setSpeedOpen(false);
    setLoaded(false);
    setLoadFailed(false);
  }, [visible]);

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
    resumeMediaMsRef.current = nextMediaMs;
    requestedMediaMsRef.current = nextMediaMs;
    setMediaMs(nextMediaMs);
    // Timeline hover seeks a paused recording to preview that frame. Those
    // programmatic time updates must not feed back into the pinned playhead.
    // Only an actively playing recording owns playhead progression.
    if (!exact || video.paused) return;
    const combatMs = mapMediaToCombat(nextMediaMs, model.sync.anchors);
    if (combatMs !== null) onPlaybackCombatTime(combatMs);
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
    handleEnded,
    handleLoadedMetadata,
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
