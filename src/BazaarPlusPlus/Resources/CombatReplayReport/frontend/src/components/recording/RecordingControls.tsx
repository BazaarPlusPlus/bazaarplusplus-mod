import {
  Check,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Maximize2,
  Pause,
  Play,
} from "lucide-react";
import { Button } from "../ui/button.tsx";
import { ButtonGroup } from "../ui/button-group.tsx";
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "../ui/popover.tsx";
import { ControlTooltip } from "../ui/tooltip.tsx";

export const RECORDING_SPEEDS = [0.5, 1, 1.5, 2] as const;

export function RecordingControls({
  mediaMsLabel,
  onFullscreen,
  onNextEvent,
  onPreviousEvent,
  onSpeedChange,
  onSpeedOpenChange,
  onTogglePlayback,
  playing,
  speedIndex,
  speedOpen,
  t,
}: {
  mediaMsLabel: string;
  onFullscreen: () => void;
  onNextEvent: () => void;
  onPreviousEvent: () => void;
  onSpeedChange: (index: number) => void;
  onSpeedOpenChange: (open: boolean) => void;
  onTogglePlayback: () => void;
  playing: boolean;
  speedIndex: number;
  speedOpen: boolean;
  t: (key: string) => string;
}): React.JSX.Element {
  return (
    <div
      className="flex items-center gap-1"
      data-bpp-test-id="recording-controls"
    >
      <ButtonGroup>
        <ControlTooltip label={t("previousEvent")}>
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
        </ControlTooltip>
        <ControlTooltip
          label={playing ? t("pauseRecording") : t("playRecording")}
        >
          <Button
            aria-label={playing ? t("pauseRecording") : t("playRecording")}
            data-bpp-test-id="recording-play-toggle"
            onClick={onTogglePlayback}
            size="icon-xs"
            type="button"
            variant="outline"
          >
            {playing
              ? <Pause className="size-icon-md" />
              : <Play className="size-icon-md" />}
          </Button>
        </ControlTooltip>
        <ControlTooltip label={t("nextEvent")}>
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
        </ControlTooltip>
      </ButtonGroup>
      <span
        className="ml-1 min-w-[68px] text-center font-data text-micro tabular-nums text-brand-soft"
        data-bpp-test-id="recording-timecode"
      >
        {mediaMsLabel}
      </span>
      <Popover onOpenChange={onSpeedOpenChange} open={speedOpen}>
        <ControlTooltip label={t("playbackSpeed")}>
          <PopoverTrigger asChild>
            <Button
              aria-label={t("playbackSpeed")}
              className="ml-auto min-w-14 gap-1 px-2 font-data text-micro tabular-nums text-brand-soft"
              data-bpp-test-id="recording-speed"
              size="xs"
              type="button"
              variant="outline"
            >
              {RECORDING_SPEEDS[speedIndex]}×
              <ChevronDown className="size-icon-xs" />
            </Button>
          </PopoverTrigger>
        </ControlTooltip>
        <PopoverContent
          align="end"
          className="w-28 p-1"
          data-bpp-test-id="recording-speed-popover"
          side="top"
          sideOffset={8}
        >
          <div aria-label={t("playbackSpeed")} role="radiogroup">
            {RECORDING_SPEEDS.map((speed, index) => {
              const selected = index === speedIndex;
              return (
                <Button
                  aria-checked={selected}
                  className="w-full justify-between px-2 font-data tabular-nums"
                  data-bpp-test-id={`recording-speed-option-${speed}`}
                  key={speed}
                  onClick={() => onSpeedChange(index)}
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
      <ControlTooltip label={t("fullscreenRecording")}>
        <Button
          aria-label={t("fullscreenRecording")}
          data-bpp-test-id="recording-fullscreen"
          onClick={onFullscreen}
          size="icon-xs"
          type="button"
          variant="ghost"
        >
          <Maximize2 className="size-icon-sm" />
        </Button>
      </ControlTooltip>
    </div>
  );
}
