import { Component, type ErrorInfo, type ReactNode } from "react";

interface ErrorBoundaryProps {
  fallback: (error: Error) => ReactNode;
  children: ReactNode;
}

interface ErrorBoundaryState {
  error: Error | null;
}

export class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  state: ErrorBoundaryState = { error: null };

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    if (window.__BPP_VIEWER_TEST__) {
      window.__BPP_VIEWER_TEST__.lastError =
        `${error.message}\n${errorInfo.componentStack ?? ""}`;
    }
  }

  render(): ReactNode {
    return this.state.error
      ? this.props.fallback(this.state.error)
      : this.props.children;
  }
}
