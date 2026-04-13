import { ErrorBoundary } from "solid-js";
import AppRouter from "./router";

function ErrorFallback(err: unknown, reset: () => void) {
  return (
    <div class="flex h-screen items-center justify-center bg-surface-base p-8">
      <div class="max-w-lg text-center">
        <h1 class="text-2xl font-semibold text-text-primary">
          Something went wrong
        </h1>
        <p class="mt-2 font-mono text-sm text-text-muted">
          {err instanceof Error ? err.message : String(err)}
        </p>
        <button
          onClick={reset}
          class="mt-4 rounded-md bg-interactive-default px-4 py-2 text-sm font-medium text-white hover:bg-interactive-hover"
        >
          Try Again
        </button>
      </div>
    </div>
  );
}

export default function App() {
  return (
    <ErrorBoundary fallback={ErrorFallback}>
      <AppRouter />
    </ErrorBoundary>
  );
}
