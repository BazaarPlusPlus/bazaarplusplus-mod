import { Minus, Plus, Video, VideoOff } from "lucide-react";
import type { ReportAction } from "../../app/report-reducer.ts";
import {
  TIME_ZOOM_STEPS,
  type ReportState,
} from "../../app/report-state.ts";
import { Button } from "../ui/button.tsx";
import { ButtonGroup } from "../ui/button-group.tsx";
import { Toggle } from "../ui/toggle.tsx";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "../ui/tooltip.tsx";

export function WorkbenchFooter({
  dispatch,
  hasRecording,
  onToggleRecording,
  recordingControlsRef,
  recordingVisible,
  showTimelineTools,
  state,
  t,
}: {
  dispatch: React.Dispatch<ReportAction>;
  hasRecording: boolean;
  onToggleRecording: () => void;
  recordingControlsRef: (node: HTMLDivElement | null) => void;
  recordingVisible: boolean;
  showTimelineTools: boolean;
  state: ReportState;
  t: (key: string) => string;
}): React.JSX.Element {
  const zoom = TIME_ZOOM_STEPS[state.timeZoomIndex];

  return (
    <footer
      className="relative z-40 flex h-9 shrink-0 items-center gap-3 overflow-hidden border-t border-border bg-surface/98 px-2 shadow-sticky"
      data-bpp-test-id="workbench-footer"
    >
      {showTimelineTools && (
        <ButtonGroup
          aria-label={t("timeZoom")}
          className="shrink-0"
          data-bpp-test-id="timeline-zoom-toolbar"
        >
          <Button
            aria-label={t("zoomOut")}
            data-bpp-test-id="time-zoom-out"
            disabled={state.timeZoomIndex === 0}
            onClick={() => dispatch({ type: "zoom", direction: -1 })}
            size="icon-xs"
            type="button"
            variant="outline"
          >
            <Minus className="size-3.5" />
          </Button>
          <Button
            aria-label={t("zoomReset")}
            className="min-w-12 px-2 font-mono text-micro text-brand-soft"
            data-bpp-test-id="time-zoom-reset"
            onClick={() => dispatch({ type: "reset-zoom" })}
            size="xs"
            type="button"
            variant="outline"
          >
            {Math.round(zoom * 100)}%
          </Button>
          <Button
            aria-label={t("zoomIn")}
            data-bpp-test-id="time-zoom-in"
            disabled={
              state.timeZoomIndex === TIME_ZOOM_STEPS.length - 1
            }
            onClick={() => dispatch({ type: "zoom", direction: 1 })}
            size="icon-xs"
            type="button"
            variant="outline"
          >
            <Plus className="size-3.5" />
          </Button>
        </ButtonGroup>
      )}

      {hasRecording && (
        <Tooltip>
          <TooltipTrigger asChild>
            <Toggle
              aria-label={
                recordingVisible
                  ? t("hideRecording")
                  : t("showRecording")
              }
              className="ml-auto size-7 shrink-0 px-0"
              data-bpp-test-id="recording-visibility-toggle"
              onPressedChange={(pressed) => {
                if (pressed !== recordingVisible) onToggleRecording();
              }}
              pressed={recordingVisible}
              size="xs"
              variant="outline"
            >
              {recordingVisible
                ? <Video className="size-3.5" />
                : <VideoOff className="size-3.5" />}
            </Toggle>
          </TooltipTrigger>
          <TooltipContent>
            {recordingVisible
              ? t("hideRecording")
              : t("showRecording")}
          </TooltipContent>
        </Tooltip>
      )}

      <div
        className="flex shrink-0 items-center"
        data-bpp-test-id="recording-controls-slot"
        ref={recordingControlsRef}
      />
    </footer>
  );
}
