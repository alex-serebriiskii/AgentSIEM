import type { Severity } from "~/api/types";

export interface SeverityBadgeProps {
  severity: Severity;
  size?: "sm" | "md";
}

const SEVERITY_STYLES: Record<Severity, string> = {
  critical: "bg-severity-critical-bg text-severity-critical",
  high: "bg-severity-high-bg text-severity-high",
  medium: "bg-severity-medium-bg text-severity-medium",
  low: "bg-severity-low-bg text-severity-low",
};

const LABELS: Record<Severity, string> = {
  critical: "Critical",
  high: "High",
  medium: "Medium",
  low: "Low",
};

const SIZE_CLASSES: Record<string, string> = {
  sm: "px-1.5 py-0.5 text-xs",
  md: "px-2 py-1 text-sm",
};

export default function SeverityBadge(props: SeverityBadgeProps) {
  const size = () => props.size ?? "md";

  return (
    <span
      class={`inline-flex items-center rounded-full font-medium ${SEVERITY_STYLES[props.severity]} ${SIZE_CLASSES[size()]}`}
    >
      {LABELS[props.severity]}
    </span>
  );
}
