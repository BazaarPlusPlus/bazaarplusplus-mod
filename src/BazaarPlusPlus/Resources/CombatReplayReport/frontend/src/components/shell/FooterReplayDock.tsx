import { GripHorizontal } from "lucide-react";
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useRef,
  useState,
} from "react";
import type { ReportViewModel } from "../../model/report.ts";
import type { CombatLogEntry } from "../../combat-log/entries.ts";
import {
  CombatLogList,
  type CombatLogHandle,
  type CombatLogSelectionAnchor,
} from "../combat-log/CombatLogList.tsx";
import { Button } from "../ui/button.tsx";

const MIN_DOCK_HEIGHT = 190;
const DEFAULT_DOCK_HEIGHT = 270;
const MIN_TIMELINE_WORKSPACE_HEIGHT = 192;
const SHELL_CHROME_HEIGHT = 84;

function dockHeightBounds(): { minimum: number; maximum: number } {
  const maximum = Math.max(
    120,
    Math.min(
      Math.round(window.innerHeight * 0.52),
      window.innerHeight
        - MIN_TIMELINE_WORKSPACE_HEIGHT
        - SHELL_CHROME_HEIGHT,
    ),
  );
  return {
    minimum: Math.min(MIN_DOCK_HEIGHT, maximum),
    maximum,
  };
}

export interface FooterReplayDockHandle extends CombatLogHandle {}

export const FooterReplayDock = forwardRef<
  FooterReplayDockHandle,
  {
    initialPlaybackActive: boolean;
    initialPlaybackCombatMs: number;
    model: ReportViewModel;
    pinnedCombatMs: number;
    pinnedEventIds: readonly string[];
    onRecordingHost: (node: HTMLDivElement | null) => void;
    onSelectEntry: (
      entry: CombatLogEntry,
      anchor?: CombatLogSelectionAnchor,
    ) => void;
    t: (key: string) => string;
  }
>(function FooterReplayDock(
  {
    initialPlaybackActive,
    initialPlaybackCombatMs,
    model,
    pinnedCombatMs,
    pinnedEventIds,
    onRecordingHost,
    onSelectEntry,
    t,
  },
  forwardedRef,
): React.JSX.Element {
  const logRef = useRef<CombatLogHandle>(null);
  const dragRef = useRef<{
    pointerId: number;
    startY: number;
    height: number;
  } | null>(null);
  const [height, setHeight] = useState(() => {
    const bounds = dockHeightBounds();
    return Math.min(DEFAULT_DOCK_HEIGHT, bounds.maximum);
  });
  const [bounds, setBounds] = useState(dockHeightBounds);

  useEffect(() => {
    const clampHeight = (): void => {
      const nextBounds = dockHeightBounds();
      setBounds(nextBounds);
      setHeight((current) =>
        Math.max(
          nextBounds.minimum,
          Math.min(current, nextBounds.maximum),
        )
      );
    };
    window.addEventListener("resize", clampHeight);
    return () => window.removeEventListener("resize", clampHeight);
  }, []);

  const finishResize = (
    event: React.PointerEvent<HTMLButtonElement>,
  ): void => {
    if (event.currentTarget.hasPointerCapture(event.pointerId)) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }
    dragRef.current = null;
  };

  useImperativeHandle(
    forwardedRef,
    () => ({
      setPlaybackActive: (active) =>
        logRef.current?.setPlaybackActive(active),
      setPlaybackCombatMs: (combatMs) =>
        logRef.current?.setPlaybackCombatMs(combatMs),
      setPreviewCombatMs: (combatMs) =>
        logRef.current?.setPreviewCombatMs(combatMs),
    }),
  );

  return (
    <section
      className="relative z-30 flex shrink-0 overflow-hidden border-t border-border bg-background pt-3 shadow-sticky"
      data-bpp-test-id="footer-replay-dock"
      style={{ height }}
    >
      <Button
        aria-label={t("resizeCombatLog")}
        aria-orientation="horizontal"
        aria-valuemax={bounds.maximum}
        aria-valuemin={bounds.minimum}
        aria-valuenow={height}
        className="absolute inset-x-0 top-0 z-10 flex h-3 w-full touch-none cursor-row-resize items-center justify-center rounded-none border-0 bg-surface-raised/70 p-0 text-muted-foreground/70 opacity-100 transition-colors hover:bg-accent/70 hover:text-foreground focus-visible:bg-accent/70 focus-visible:text-foreground"
        data-bpp-test-id="footer-replay-dock-resize"
        role="separator"
        onPointerCancel={finishResize}
        onPointerDown={(event) => {
          if (event.button !== 0) return;
          dragRef.current = {
            pointerId: event.pointerId,
            startY: event.clientY,
            height,
          };
          event.currentTarget.setPointerCapture(event.pointerId);
        }}
        onPointerMove={(event) => {
          const drag = dragRef.current;
          if (!drag || drag.pointerId !== event.pointerId) return;
          const nextBounds = dockHeightBounds();
          setHeight(Math.max(
            nextBounds.minimum,
            Math.min(
              nextBounds.maximum,
              drag.height + drag.startY - event.clientY,
            ),
          ));
        }}
        onKeyDown={(event) => {
          const nextBounds = dockHeightBounds();
          const delta = event.shiftKey ? 40 : 12;
          let nextHeight = height;
          if (event.key === "ArrowUp") nextHeight += delta;
          else if (event.key === "ArrowDown") nextHeight -= delta;
          else if (event.key === "Home") nextHeight = nextBounds.minimum;
          else if (event.key === "End") nextHeight = nextBounds.maximum;
          else return;
          event.preventDefault();
          setHeight(Math.max(
            nextBounds.minimum,
            Math.min(nextHeight, nextBounds.maximum),
          ));
        }}
        onPointerUp={finishResize}
        size="icon-xs"
        type="button"
        variant="ghost"
      >
        <GripHorizontal className="h-2.5 w-8" />
      </Button>
      <CombatLogList
        initialPlaybackActive={initialPlaybackActive}
        initialPlaybackCombatMs={initialPlaybackCombatMs}
        model={model}
        onSelectEntry={onSelectEntry}
        pinnedCombatMs={pinnedCombatMs}
        pinnedEventIds={pinnedEventIds}
        ref={logRef}
        t={t}
      />
      {model.videoUrl && (
        <div
          className="h-full w-[clamp(136px,32vw,180px)] shrink-0 border-l border-border/60 bg-media min-[721px]:aspect-video min-[721px]:w-auto min-[721px]:max-w-[40vw]"
          data-bpp-test-id="footer-recording-host"
          ref={onRecordingHost}
        />
      )}
    </section>
  );
});
