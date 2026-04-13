import type { AlertStatus } from "~/api/types";

export interface StatusIndicatorProps {
  status: AlertStatus;
}

const STATUS_STYLES: Record<AlertStatus, { dot: string; label: string }> = {
  open: { dot: "bg-severity-high", label: "Open" },
  acknowledged: { dot: "bg-interactive-default", label: "Acknowledged" },
  resolved: { dot: "bg-state-healthy", label: "Resolved" },
};

export default function StatusIndicator(props: StatusIndicatorProps) {
  const style = () => STATUS_STYLES[props.status];

  return (
    <span class="inline-flex items-center gap-1.5">
      <span class={`inline-block h-2 w-2 rounded-full ${style().dot}`} />
      <span class="text-sm text-text-secondary">{style().label}</span>
    </span>
  );
}
