import { createSignal } from "solid-js";
import type { SignalRAlertPayload } from "~/api/types";
import { onAlertReceived } from "./connection";

// ---------------------------------------------------------------------------
// Reactive state
// ---------------------------------------------------------------------------

const MAX_RECENT = 50;

const [openAlertCount, setOpenAlertCount] = createSignal(0);
const [recentAlerts, setRecentAlerts] = createSignal<SignalRAlertPayload[]>([]);

export { openAlertCount, recentAlerts };

export function resetAlertCount(count: number): void {
  setOpenAlertCount(count);
}

// ---------------------------------------------------------------------------
// Subscriber pattern for external consumers (e.g. toasts)
// ---------------------------------------------------------------------------

export type AlertSubscriber = (alert: SignalRAlertPayload) => void;

const subscribers = new Set<AlertSubscriber>();

export function subscribe(fn: AlertSubscriber): () => void {
  subscribers.add(fn);
  return () => {
    subscribers.delete(fn);
  };
}

// ---------------------------------------------------------------------------
// Initialization — call after connection is started
// ---------------------------------------------------------------------------

export function initAlertListener(): () => void {
  const unsubscribe = onAlertReceived((payload) => {
    setRecentAlerts((prev) => {
      // Deduplicate by alertId — only increment count for new alerts
      if (prev.some((a) => a.alertId === payload.alertId)) {
        return prev;
      }
      setOpenAlertCount((c) => c + 1);
      return [payload, ...prev].slice(0, MAX_RECENT);
    });

    // Notify external subscribers
    for (const fn of subscribers) {
      fn(payload);
    }
  });

  return unsubscribe;
}
