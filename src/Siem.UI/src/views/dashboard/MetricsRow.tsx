import { For, Show } from "solid-js";
import Sparkline from "~/components/Sparkline";
import { SkeletonCard } from "~/components/LoadingSkeleton";

export interface MetricWidget {
  label: string;
  value: number;
  sparklineData: number[];
  formatValue?: (n: number) => string;
}

export interface MetricsRowProps {
  metrics: MetricWidget[];
  loading: boolean;
}

function exceedsThreshold(value: number, data: number[]): boolean {
  if (data.length < 2) return false;
  const avg = data.reduce((a, b) => a + b, 0) / data.length;
  return value > avg * 1.5;
}

const defaultFormat = (n: number) => n.toLocaleString();

export default function MetricsRow(props: MetricsRowProps) {
  return (
    <div class="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
      <Show
        when={!props.loading}
        fallback={
          <For each={Array.from({ length: 4 })}>
            {() => <SkeletonCard />}
          </For>
        }
      >
        <For each={props.metrics}>
          {(metric) => {
            const exceeded = () => exceedsThreshold(metric.value, metric.sparklineData);
            const format = () => metric.formatValue ?? defaultFormat;

            return (
              <div>
                <span
                  class="font-mono text-3xl font-light"
                  classList={{
                    "text-text-primary": !exceeded(),
                    "text-severity-high": exceeded(),
                  }}
                >
                  {format()(metric.value)}
                </span>
                <p class="mt-1 text-sm text-text-muted">{metric.label}</p>
                <Show when={metric.sparklineData.length > 0}>
                  <div class="mt-2">
                    <Sparkline
                      data={metric.sparklineData}
                      width={120}
                      height={32}
                      color={
                        exceeded()
                          ? "var(--color-severity-high)"
                          : undefined
                      }
                    />
                  </div>
                </Show>
              </div>
            );
          }}
        </For>
      </Show>
    </div>
  );
}
