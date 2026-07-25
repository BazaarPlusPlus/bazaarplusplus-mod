import {
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Maximize2,
  Minus,
  Pause,
  Play,
} from "lucide-react";
import {
  forwardRef,
  useCallback,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import { createPortal } from "react-dom";
import type { ReportViewModel } from "../../model/report.ts";
import {
  mapCombatToMedia,
  mapMediaToCombat,
} from "../../recording/sync.ts";
import { formatTimecode } from "../../i18n/format.ts";
import { Button } from "../ui/button.tsx";
import { ButtonGroup } from "../ui/button-group.tsx";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "../ui/popover.tsx";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "../ui/tooltip.tsx";

const SPEEDS = [0.5, 1, 1.5, 2] as const;
const WORKBENCH_FOOTER_HEIGHT = 36;
const WORKBENCH_FOOTER_GAP = 10;

export interface RecordingHandle {
  seekCombatMs: (combatMs: number, preview?: boolean) => void;
  prepareToHide: () => void;
}

export const RecordingWindow = forwardRef<
  RecordingHandle,
  {
    controlsHost: HTMLDivElement | null;
    model: ReportViewModel;
    visible: boolean;
    onHide: () => void;
    onPlaybackCombatTime: (combatMs: number) => void;
    onPreviousEvent: () => void;
    onNextEvent: () => void;
    t: (key: string) => string;
  }
>(function RecordingWindow(
  {
    controlsHost,
    model,
    visible,
    onHide,
    onPlaybackCombatTime,
    onPreviousEvent,
    onNextEvent,
    t,
  },
  forwardedRef,
): React.JSX.Element | null {
  const hostRef = useRef<HTMLElement>(null);
  const videoRef = useRef<HTMLVideoElement>(null);
  const dragRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    left: number;
    top: number;
  } | null>(null);
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

  useImperativeHandle(
    forwardedRef,
    () => ({ prepareToHide, seekCombatMs }),
    [prepareToHide, seekCombatMs],
  );

  useEffect(
    () => () => window.cancelAnimationFrame(previewFrameRef.current),
    [],
  );

  useEffect(() => {
    const video = videoRef.current;
    if (!video) return;
    video.playbackRate = SPEEDS[speedIndex];
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

  const handleLoadedMetadata = (): void => {
    const host = hostRef.current;
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
      video.playbackRate = SPEEDS[speedIndex];
      resumeMediaMsRef.current = clampedMediaMs;
      requestedMediaMsRef.current = clampedMediaMs;
      setMediaMs(clampedMediaMs);
    }
    setLoaded(true);
  };

  const startDrag = (
    event: React.PointerEvent<HTMLElement>,
  ): void => {
    if (event.button !== 0 || !hostRef.current) return;
    const target = event.target as HTMLElement;
    if (target.closest("button")) return;
    const bounds = hostRef.current.getBoundingClientRect();
    dragRef.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      left: bounds.left,
      top: bounds.top,
    };
    hostRef.current.style.right = "auto";
    hostRef.current.style.bottom = "auto";
    event.currentTarget.setPointerCapture(event.pointerId);
  };

  const moveDrag = (
    event: React.PointerEvent<HTMLElement>,
  ): void => {
    const drag = dragRef.current;
    const host = hostRef.current;
    if (!drag || !host || drag.pointerId !== event.pointerId) return;
    const width = host.offsetWidth;
    const height = host.offsetHeight;
    const footerTop =
      document
        .querySelector<HTMLElement>(
          '[data-bpp-test-id="workbench-footer"]',
        )
        ?.getBoundingClientRect().top
      ?? window.innerHeight - WORKBENCH_FOOTER_HEIGHT;
    const left = Math.max(
      8,
      Math.min(
        window.innerWidth - width - 8,
        drag.left + event.clientX - drag.startX,
      ),
    );
    const top = Math.max(
      64,
      Math.min(
        footerTop - height - WORKBENCH_FOOTER_GAP,
        drag.top + event.clientY - drag.startY,
      ),
    );
    host.style.left = `${left}px`;
    host.style.top = `${top}px`;
  };

  if (!visible) return null;

  const controls = (
    <div
      className="flex items-center gap-1"
      data-bpp-test-id="recording-controls"
    >
      <ButtonGroup>
        <Button
          aria-label={t("previousEvent")}
          data-bpp-test-id="recording-previous-event"
          onClick={onPreviousEvent}
          size="icon-xs"
          type="button"
          variant="outline"
        >
          <ChevronLeft className="size-icon-sm" />
        </Button>
        <Button
          aria-label={playing ? t("pauseRecording") : t("playRecording")}
          data-bpp-test-id="recording-play-toggle"
          onClick={togglePlayback}
          size="icon-xs"
          type="button"
          variant="outline"
        >
          {playing
            ? <Pause className="size-icon-md" />
            : <Play className="size-icon-md" />}
        </Button>
        <Button
          aria-label={t("nextEvent")}
          data-bpp-test-id="recording-next-event"
          onClick={onNextEvent}
          size="icon-xs"
          type="button"
          variant="outline"
        >
          <ChevronRight className="size-icon-sm" />
        </Button>
      </ButtonGroup>
      <span
        className="ml-1 min-w-[68px] text-center font-mono text-micro text-brand-soft"
        data-bpp-test-id="recording-timecode"
      >
        {formatTimecode(mediaMs)}
      </span>
      <Popover onOpenChange={setSpeedOpen} open={speedOpen}>
        <PopoverTrigger asChild>
          <Button
            aria-label={t("playbackSpeed")}
            className="ml-auto min-w-14 gap-1 px-2 font-mono text-micro text-brand-soft"
            data-bpp-test-id="recording-speed"
            size="xs"
            type="button"
            variant="outline"
          >
            {SPEEDS[speedIndex]}×
            <ChevronDown className="size-icon-xs" />
          </Button>
        </PopoverTrigger>
        <PopoverContent
          align="end"
          className="w-28 p-1"
          data-bpp-test-id="recording-speed-popover"
          side="top"
          sideOffset={8}
        >
          <div aria-label={t("playbackSpeed")} role="radiogroup">
            {SPEEDS.map((speed, index) => {
              const selected = index === speedIndex;
              return (
                <Button
                  aria-checked={selected}
                  className="w-full justify-between px-2 font-mono"
                  data-bpp-test-id={`recording-speed-option-${speed}`}
                  key={speed}
                  onClick={() => {
                    setSpeedIndex(index);
                    setSpeedOpen(false);
                  }}
                  role="radio"
                  size="xs"
                  type="button"
                  variant={selected ? "secondary" : "ghost"}
                >
                  {speed}×
                  <Check
                    className={selected
                      ? "size-icon-xs"
                      : "size-icon-xs opacity-0"}
                  />
                </Button>
              );
            })}
          </div>
        </PopoverContent>
      </Popover>
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            aria-label={t("fullscreenRecording")}
            data-bpp-test-id="recording-fullscreen"
            onClick={() => videoRef.current?.requestFullscreen?.()}
            size="icon-xs"
            type="button"
            variant="ghost"
          >
            <Maximize2 className="size-icon-sm" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{t("fullscreenRecording")}</TooltipContent>
      </Tooltip>
    </div>
  );

  return (
    <>
      <section
        aria-label={t("recordingTitle")}
        className="bpp-recording-window"
        data-bpp-test-id="recording-window"
        ref={hostRef}
      >
        <header
          className="bpp-recording-drag-handle"
          data-bpp-test-id="recording-drag-handle"
          onPointerCancel={() => {
            dragRef.current = null;
          }}
          onPointerDown={startDrag}
          onPointerMove={moveDrag}
          onPointerUp={() => {
            dragRef.current = null;
          }}
        >
          <strong>{t("recordingTitle")}</strong>
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                aria-label={t("hideRecording")}
                className="ml-auto"
                onClick={onHide}
                size="icon-xs"
                type="button"
                variant="ghost"
              >
                <Minus className="size-icon-sm" />
              </Button>
            </TooltipTrigger>
            <TooltipContent>{t("hideRecording")}</TooltipContent>
          </Tooltip>
        </header>
        <div className="bpp-recording-media bg-media">
          <video
            className="block size-full object-contain"
            data-bpp-test-id="recording-video"
            onCanPlay={() => setLoaded(true)}
            onError={() => setLoadFailed(true)}
            onLoadedMetadata={handleLoadedMetadata}
            onPause={() => setPlaying(false)}
            onPlay={() => setPlaying(true)}
            onTimeUpdate={handleTimeUpdate}
            playsInline
            preload="metadata"
            ref={videoRef}
            src={model.videoUrl}
          />
          {!loaded && (
            <div className="pointer-events-none absolute inset-x-0 bottom-0 top-8 grid place-items-center bg-media/80 px-6 text-center text-compact text-muted-foreground">
              {t(loadFailed ? "recordingLoadError" : "recordingReady")}
            </div>
          )}
        </div>
      </section>
      {controlsHost && createPortal(controls, controlsHost)}
    </>
  );
});
