import { Minus } from "lucide-react";
import {
  forwardRef,
  useImperativeHandle,
  useRef,
} from "react";
import { createPortal } from "react-dom";
import type { ReportViewModel } from "../../model/report.ts";
import { formatTimecode } from "../../i18n/format.ts";
import { Button } from "../ui/button.tsx";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "../ui/tooltip.tsx";
import {
  RecordingControls,
} from "./RecordingControls.tsx";
import { useRecordingPlayback } from "./useRecordingPlayback.ts";

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
  const dragRef = useRef<{
    pointerId: number;
    startX: number;
    startY: number;
    left: number;
    top: number;
  } | null>(null);
  const {
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
  } = useRecordingPlayback({
    model,
    onPlaybackCombatTime,
    visible,
  });

  useImperativeHandle(
    forwardedRef,
    () => ({ prepareToHide, seekCombatMs }),
    [prepareToHide, seekCombatMs],
  );

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
    <RecordingControls
      mediaMsLabel={formatTimecode(mediaMs)}
      onFullscreen={() => videoRef.current?.requestFullscreen?.()}
      onNextEvent={onNextEvent}
      onPreviousEvent={onPreviousEvent}
      onSpeedChange={(index) => {
        setSpeedIndex(index);
        setSpeedOpen(false);
      }}
      onSpeedOpenChange={setSpeedOpen}
      onTogglePlayback={() => {
        void togglePlayback();
      }}
      playing={playing}
      speedIndex={speedIndex}
      speedOpen={speedOpen}
      t={t}
    />
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
            onLoadedMetadata={() => handleLoadedMetadata(hostRef.current)}
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
