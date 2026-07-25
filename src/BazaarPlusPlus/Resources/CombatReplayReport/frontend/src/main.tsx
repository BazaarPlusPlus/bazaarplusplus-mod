import React from "react";
import { createRoot } from "react-dom/client";
import { AlertTriangle } from "lucide-react";
import { ErrorBoundary } from "./app/ErrorBoundary.tsx";
import { ReportApp } from "./app/ReportApp.tsx";
import { TooltipProvider } from "./components/ui/tooltip.tsx";
import { copyForLocale } from "./i18n/use-copy.ts";
import {
  normalizeLocale,
  readEnvelope,
  ReportError,
  selectLocale,
} from "./model/report.ts";
import "./styles/app.css";

declare global {
  interface Window {
    __BPP_VIEWER_TEST__?: {
      commitCount: number;
      hoverDrawCount: number;
      timelineControllerCount: number;
      timelinePreprocessCount: number;
      lastError?: string;
    };
  }
}

function reportRoot(): HTMLElement {
  const roots = document.querySelectorAll<HTMLElement>(
    "[data-bpp-test-id='report-root']",
  );
  const root = roots[0] ?? document.createElement("main");
  if (!roots[0]) {
    root.setAttribute("data-bpp-test-id", "report-root");
    document.body.append(root);
  }
  root.className = "bpp-report-root";
  return root;
}

function FatalReport({ error }: { error: unknown }): React.JSX.Element {
  const locale = normalizeLocale(navigator.language || "en");
  const { t } = copyForLocale(locale);
  const code = error instanceof ReportError ? error.code : "invalidData";
  document.documentElement.lang = locale;
  return (
    <section
      className="grid h-full place-items-center px-4"
      data-bpp-test-id="report-error-state"
      role="alert"
    >
      <div className="flex w-full max-w-xl flex-col items-center gap-3 rounded-panel border border-danger/25 bg-card p-8 text-center shadow-float">
        <AlertTriangle className="size-8 text-danger" aria-hidden="true" />
        <h1 className="text-headline font-semibold text-foreground">
          {t("reportError")}
        </h1>
        <p className="text-body text-muted-foreground">{t("errorHint")}</p>
        <p
          className="rounded-panel bg-danger/10 px-3 py-1.5 font-mono text-compact text-danger"
          data-bpp-test-id="report-error-reason"
        >
          {t(code)}
        </p>
      </div>
    </section>
  );
}

function boot(): void {
  const root = reportRoot();
  try {
    const envelope = readEnvelope();
    const initialLocale = selectLocale(envelope);
    document.documentElement.classList.add("dark");
    document.documentElement.lang = initialLocale;
    window.__BPP_VIEWER_TEST__ = {
      commitCount: 0,
      hoverDrawCount: 0,
      timelineControllerCount: 0,
      timelinePreprocessCount: 0,
    };
    createRoot(root).render(
      <React.StrictMode>
        <ErrorBoundary fallback={(error) => <FatalReport error={error} />}>
          <TooltipProvider>
            <ReportApp envelope={envelope} initialLocale={initialLocale} />
          </TooltipProvider>
        </ErrorBoundary>
      </React.StrictMode>,
    );
  } catch (error) {
    createRoot(root).render(<FatalReport error={error} />);
  }
}

if (document.readyState === "loading") {
  document.addEventListener("DOMContentLoaded", boot, { once: true });
} else {
  boot();
}
