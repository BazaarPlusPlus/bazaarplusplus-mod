import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
} from "react";
import type { UnknownRecord } from "../model/value.ts";
import {
  buildViewModel,
  type SupportedLocale,
} from "../model/report.ts";
import { copyForLocale } from "../i18n/use-copy.ts";
import { createInitialReportState } from "./report-state.ts";
import { reportReducer } from "./report-reducer.ts";
import { ReportHeader } from "../components/shell/ReportHeader.tsx";
import {
  TimelineView,
  type TimelineHandle,
} from "../components/timeline/TimelineView.tsx";
import { StatisticsView } from "../components/statistics/StatisticsView.tsx";
import {
  RecordingWindow,
  type RecordingHandle,
} from "../components/recording/RecordingWindow.tsx";
import { WorkbenchFooter } from "../components/shell/WorkbenchFooter.tsx";

export function ReportApp({
  envelope,
  initialLocale,
}: {
  envelope: UnknownRecord;
  initialLocale: SupportedLocale;
}): React.JSX.Element {
  const [state, dispatch] = useReducer(
    reportReducer,
    initialLocale,
    createInitialReportState,
  );
  const [recordingVisible, setRecordingVisible] = useState(true);
  const [recordingControlsHost, setRecordingControlsHost] =
    useState<HTMLDivElement | null>(null);
  const timelineRef = useRef<TimelineHandle>(null);
  const recordingRef = useRef<RecordingHandle>(null);
  const { copy, t } = useMemo(
    () => copyForLocale(state.locale),
    [state.locale],
  );
  const model = useMemo(
    () => buildViewModel(envelope, copy),
    [copy, envelope],
  );

  useLayoutEffect(() => {
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.commitCount += 1;
    }
  });

  useEffect(() => {
    document.documentElement.lang = state.locale;
    document.documentElement.classList.add("bpp-report-ready");
    return () => {
      document.documentElement.classList.remove("bpp-report-ready");
    };
  }, [state.locale]);

  useEffect(() => {
    recordingRef.current?.seekCombatMs(state.selectedCombatMs);
  }, [state.selectedCombatMs]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent): void => {
      const target = event.target as HTMLElement | null;
      if (
        target?.matches(
          "input, textarea, select, button, [contenteditable='true']",
        )
      ) {
        return;
      }
      if (event.key === "ArrowLeft" || event.key === "ArrowRight") {
        event.preventDefault();
        timelineRef.current?.navigate(event.key === "ArrowLeft" ? -1 : 1);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, []);

  const previewCombatMs = useCallback((combatMs: number | null): void => {
    if (combatMs !== null) {
      recordingRef.current?.seekCombatMs(combatMs, true);
    }
  }, []);
  const toggleRecording = useCallback((): void => {
    if (recordingVisible) {
      recordingRef.current?.prepareToHide();
    }
    setRecordingVisible(!recordingVisible);
  }, [recordingVisible]);

  return (
    <div className="flex size-full min-h-0 flex-col overflow-hidden">
      <ReportHeader
        dispatch={dispatch}
        model={model}
        state={state}
        t={t}
      />
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-panel">
        <div className="relative min-h-0 flex-1 overflow-hidden">
          <div
            aria-hidden={state.tab !== "timeline"}
            className={
              state.tab === "timeline"
                ? "absolute inset-0 flex min-h-0"
                : "pointer-events-none invisible absolute inset-0 flex min-h-0"
            }
          >
            <TimelineView
              dispatch={dispatch}
              model={model}
              onPreviewCombatMs={previewCombatMs}
              ref={timelineRef}
              state={state}
              t={t}
            />
          </div>
          {state.tab === "statistics" && (
            <div className="absolute inset-0 flex min-h-0">
              <StatisticsView
                dispatch={dispatch}
                model={model}
                state={state}
                t={t}
              />
            </div>
          )}
        </div>
        {(state.tab === "timeline" || Boolean(model.videoUrl)) && (
          <WorkbenchFooter
            dispatch={dispatch}
            hasRecording={Boolean(model.videoUrl)}
            onToggleRecording={toggleRecording}
            recordingControlsRef={setRecordingControlsHost}
            recordingVisible={recordingVisible}
            showTimelineTools={state.tab === "timeline"}
            state={state}
            t={t}
          />
        )}
      </div>
      {model.videoUrl && (
        <RecordingWindow
          controlsHost={recordingControlsHost}
          model={model}
          onPlaybackCombatTime={(combatMs) =>
            timelineRef.current?.setExternalPlayhead(combatMs)
          }
          onHide={() => {
            recordingRef.current?.prepareToHide();
            setRecordingVisible(false);
          }}
          onNextEvent={() => timelineRef.current?.navigate(1)}
          onPreviousEvent={() => timelineRef.current?.navigate(-1)}
          ref={recordingRef}
          t={t}
          visible={recordingVisible}
        />
      )}
    </div>
  );
}
