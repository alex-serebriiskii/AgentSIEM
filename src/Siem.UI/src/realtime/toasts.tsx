import { Toast, toaster } from "@kobalte/core/toast";
import type { Component } from "solid-js";
import type { SignalRAlertPayload } from "~/api/types";
import { subscribe } from "./alerts";

// ---------------------------------------------------------------------------
// Module-level navigate function — set during init from within router tree
// ---------------------------------------------------------------------------

let navigateFn: ((path: string) => void) | null = null;

export function initToastListener(
  navigate: (path: string) => void,
): () => void {
  navigateFn = navigate;
  const unsubscribe = subscribe(showAlertToast);
  return () => {
    unsubscribe();
    navigateFn = null;
  };
}

// ---------------------------------------------------------------------------
// Severity → border color class mapping
// ---------------------------------------------------------------------------

const SEVERITY_BORDER: Record<string, string> = {
  critical: "border-l-severity-critical",
  high: "border-l-severity-high",
  medium: "border-l-severity-medium",
  low: "border-l-severity-low",
};

// ---------------------------------------------------------------------------
// Toast component factory — creates a Component for each alert
// ---------------------------------------------------------------------------

function makeAlertToast(
  alert: SignalRAlertPayload,
): Component<{ toastId: number }> {
  const borderClass =
    SEVERITY_BORDER[alert.severity] ?? "border-l-border-default";

  return (props) => {
    const handleClick = () => {
      navigateFn?.(`/alerts/${alert.alertId}`);
      toaster.dismiss(props.toastId);
    };

    return (
      <Toast
        toastId={props.toastId}
        duration={5000}
        role="button"
        tabIndex={0}
        class={`cursor-pointer rounded-lg border-l-4 ${borderClass} bg-surface-raised p-3 shadow-lg`}
        onClick={handleClick}
        onKeyDown={(e: KeyboardEvent) => {
          if (e.key === "Enter" || e.key === " ") {
            e.preventDefault();
            handleClick();
          }
        }}
      >
        <div class="flex flex-col gap-0.5">
          <span class="text-xs font-medium uppercase text-text-muted">
            {alert.severity}
          </span>
          <Toast.Title class="text-sm font-medium text-text-primary">
            {alert.title}
          </Toast.Title>
          <Toast.Description class="text-xs text-text-secondary">
            {alert.agentName}
          </Toast.Description>
        </div>
      </Toast>
    );
  };
}

function showAlertToast(alert: SignalRAlertPayload): void {
  toaster.show(makeAlertToast(alert));
}
