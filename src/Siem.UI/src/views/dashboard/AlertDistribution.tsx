import { createMemo, For, Show } from "solid-js";
import { useNavigate } from "@solidjs/router";
import type { AlertDistributionResult, Severity } from "~/api/types";
import EmptyState from "~/components/EmptyState";
import { SkeletonCard } from "~/components/LoadingSkeleton";

export interface AlertDistributionProps {
  data: AlertDistributionResult[];
  loading: boolean;
}

const SEVERITY_ORDER: Severity[] = ["critical", "high", "medium", "low"];

const SEVERITY_COLORS: Record<Severity, string> = {
  critical: "bg-severity-critical",
  high: "bg-severity-high",
  medium: "bg-severity-medium",
  low: "bg-severity-low",
};

const SEVERITY_LABELS: Record<Severity, string> = {
  critical: "Critical",
  high: "High",
  medium: "Medium",
  low: "Low",
};

export default function AlertDistribution(props: AlertDistributionProps) {
  const navigate = useNavigate();

  const bySeverity = createMemo(() => {
    const counts = new Map<Severity, number>();
    for (const s of SEVERITY_ORDER) counts.set(s, 0);
    for (const item of props.data) {
      counts.set(item.severity, (counts.get(item.severity) ?? 0) + item.count);
    }
    return SEVERITY_ORDER.map((s) => ({ severity: s, count: counts.get(s)! }));
  });

  const total = createMemo(() =>
    bySeverity().reduce((sum, s) => sum + s.count, 0),
  );

  return (
    <div class="rounded-lg border border-border-default bg-surface-raised p-4">
      <h2 class="mb-4 text-sm font-medium uppercase tracking-wider text-text-secondary">
        Alert Distribution
      </h2>
      <Show when={!props.loading} fallback={<SkeletonCard />}>
        <Show
          when={total() > 0}
          fallback={<EmptyState title="No alerts" description="No alerts in this time range" />}
        >
          <div
            class="flex h-6 w-full overflow-hidden rounded-md"
            role="group"
            aria-label="Alert distribution by severity"
          >
            <For each={bySeverity()}>
              {(segment) => (
                <Show when={segment.count > 0}>
                  <button
                    class={`transition-opacity hover:opacity-80 ${SEVERITY_COLORS[segment.severity]}`}
                    style={{ width: `${(segment.count / total()) * 100}%` }}
                    aria-label={`${SEVERITY_LABELS[segment.severity]}: ${segment.count} alerts`}
                    onClick={() =>
                      navigate(`/alerts?severity=${segment.severity}`)
                    }
                    data-testid={`segment-${segment.severity}`}
                  />
                </Show>
              )}
            </For>
          </div>

          <div class="mt-3 flex flex-wrap gap-4">
            <For each={bySeverity()}>
              {(segment) => (
                <div class="flex items-center gap-1.5 text-sm">
                  <span
                    class={`inline-block h-2.5 w-2.5 rounded-full ${SEVERITY_COLORS[segment.severity]}`}
                  />
                  <span class="text-text-muted">
                    {SEVERITY_LABELS[segment.severity]}
                  </span>
                  <span class="font-mono text-text-primary">
                    {segment.count}
                  </span>
                </div>
              )}
            </For>
          </div>
        </Show>
      </Show>
    </div>
  );
}
