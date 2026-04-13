import { Popover } from "@kobalte/core/popover";
import { createSignal, For, onCleanup, onMount, Show } from "solid-js";
import { fetchEngineStatus, recompileEngine } from "~/api/endpoints";
import type { EngineStatusResponse } from "~/api/types";
import { connectionState } from "~/realtime/connection";
import { ApiError } from "~/api/client";

// ---------------------------------------------------------------------------
// Staleness → health color mapping
// ---------------------------------------------------------------------------

function parseStalenessMinutes(staleness: string): number {
  // ASP.NET TimeSpan.ToString() formats as "HH:MM:SS" or "d.HH:MM:SS.fffffff"
  let days = 0;
  let timePart = staleness;

  // Extract day component if present (e.g. "2.03:45:00")
  const dotIdx = staleness.indexOf(".");
  if (dotIdx !== -1 && staleness.indexOf(":") > dotIdx) {
    days = parseInt(staleness.substring(0, dotIdx), 10) || 0;
    timePart = staleness.substring(dotIdx + 1);
  }

  const parts = timePart.split(":");
  if (parts.length >= 2) {
    const hours = parseInt(parts[0], 10) || 0;
    const minutes = parseInt(parts[1], 10) || 0;
    return days * 1440 + hours * 60 + minutes;
  }
  return Infinity;
}

function healthColor(status: EngineStatusResponse | null): string {
  if (!status) return "bg-severity-critical";
  const mins = parseStalenessMinutes(status.staleness);
  if (mins < 5) return "bg-state-healthy";
  if (mins < 15) return "bg-severity-medium";
  return "bg-severity-critical";
}

function connectionColor(): string {
  switch (connectionState()) {
    case "connected":
      return "bg-state-healthy";
    case "reconnecting":
    case "connecting":
      return "bg-severity-medium";
    case "disconnected":
      return "bg-severity-critical";
  }
}

// ---------------------------------------------------------------------------
// TopBar component
// ---------------------------------------------------------------------------

export default function TopBar() {
  const [engineStatus, setEngineStatus] =
    createSignal<EngineStatusResponse | null>(null);
  const [recompiling, setRecompiling] = createSignal(false);
  const [recompileError, setRecompileError] = createSignal<string | null>(null);

  let abortController: AbortController | null = null;

  const pollStatus = async () => {
    abortController?.abort();
    abortController = new AbortController();
    try {
      const status = await fetchEngineStatus(abortController.signal);
      setEngineStatus(status);
    } catch (e) {
      if (e instanceof DOMException && e.name === "AbortError") return;
      // Keep last known status on error
    }
  };

  onMount(() => {
    pollStatus();
    const interval = setInterval(pollStatus, 30_000);
    onCleanup(() => {
      clearInterval(interval);
      abortController?.abort();
    });
  });

  const handleRecompile = async () => {
    setRecompiling(true);
    setRecompileError(null);
    try {
      await recompileEngine();
      await pollStatus();
    } catch (e) {
      const msg =
        e instanceof ApiError ? e.detail ?? e.message : "Recompile failed";
      setRecompileError(msg);
    } finally {
      setRecompiling(false);
    }
  };

  const formatTime = (iso: string) => {
    try {
      return new Date(iso).toLocaleString();
    } catch {
      return iso;
    }
  };

  return (
    <header class="flex h-12 items-center justify-between border-b border-border-default bg-surface-raised px-4">
      {/* Left: placeholder for breadcrumbs in future phases */}
      <div />

      {/* Right: status indicators */}
      <div class="flex items-center gap-4">
        {/* Connection status */}
        <div class="flex items-center gap-1.5" title={`WebSocket: ${connectionState()}`}>
          <span class={`inline-block h-2 w-2 rounded-full ${connectionColor()}`} />
          <span class="text-xs text-text-muted">Live</span>
        </div>

        {/* Engine health with popover */}
        <Popover>
          <Popover.Trigger
            class="flex items-center gap-1.5 rounded px-2 py-1 text-xs text-text-muted transition-colors hover:bg-surface-overlay"
            aria-label="Engine status"
          >
            <span class={`inline-block h-2 w-2 rounded-full ${healthColor(engineStatus())}`} />
            <span>Engine</span>
          </Popover.Trigger>
          <Popover.Portal>
            <Popover.Content class="z-50 w-72 rounded-lg border border-border-default bg-surface-raised p-4 shadow-lg">
              <Popover.Arrow />
              <h3 class="mb-3 text-sm font-semibold text-text-primary">
                Rules Engine Status
              </h3>
              <Show
                when={engineStatus()}
                fallback={
                  <p class="text-sm text-text-muted">Loading status...</p>
                }
              >
                {(status) => (
                  <div class="space-y-2 text-sm">
                    <div class="flex justify-between">
                      <span class="text-text-secondary">Compiled Rules</span>
                      <span class="font-mono text-text-primary">
                        {status().ruleCount}
                      </span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-text-secondary">Last Compiled</span>
                      <span class="text-text-primary">
                        {formatTime(status().compiledAt)}
                      </span>
                    </div>
                    <div class="flex justify-between">
                      <span class="text-text-secondary">Staleness</span>
                      <span class="text-text-primary">
                        {status().staleness}
                      </span>
                    </div>

                    <Show when={status().listCaches.length > 0}>
                      <div class="border-t border-border-muted pt-2">
                        <p class="mb-1 text-xs font-medium text-text-muted">
                          List Caches
                        </p>
                        <For each={status().listCaches}>
                          {(cache) => (
                            <div class="flex justify-between text-xs">
                              <span class="text-text-secondary">
                                {cache.name}
                              </span>
                              <span class="font-mono text-text-primary">
                                {cache.memberCount} members
                              </span>
                            </div>
                          )}
                        </For>
                      </div>
                    </Show>

                    <button
                      onClick={handleRecompile}
                      disabled={recompiling()}
                      class="mt-2 w-full rounded-md bg-interactive-default px-3 py-1.5 text-sm font-medium text-white transition-colors hover:bg-interactive-hover disabled:opacity-50"
                    >
                      {recompiling() ? "Recompiling..." : "Recompile Now"}
                    </button>
                    <Show when={recompileError()}>
                      <p class="mt-1 text-xs text-severity-critical">
                        {recompileError()}
                      </p>
                    </Show>
                  </div>
                )}
              </Show>
            </Popover.Content>
          </Popover.Portal>
        </Popover>
      </div>
    </header>
  );
}
