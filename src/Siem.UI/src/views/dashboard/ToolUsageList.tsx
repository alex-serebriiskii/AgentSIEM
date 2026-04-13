import { createMemo, For, Show } from "solid-js";
import type { ToolUsageResult } from "~/api/types";
import EmptyState from "~/components/EmptyState";
import { SkeletonRow } from "~/components/LoadingSkeleton";

export interface ToolUsageListProps {
  data: ToolUsageResult[];
  loading: boolean;
}

export default function ToolUsageList(props: ToolUsageListProps) {
  const maxCount = createMemo(() => {
    const counts = props.data.map((t) => t.invocationCount);
    return counts.length > 0 ? Math.max(...counts) : 1;
  });

  return (
    <div class="rounded-lg border border-border-default bg-surface-raised p-4">
      <h2 class="mb-4 text-sm font-medium uppercase tracking-wider text-text-secondary">
        Tool Usage
      </h2>
      <Show
        when={!props.loading}
        fallback={
          <div class="space-y-1">
            <For each={Array.from({ length: 5 })}>
              {() => <SkeletonRow columns={3} />}
            </For>
          </div>
        }
      >
        <Show
          when={props.data.length > 0}
          fallback={
            <EmptyState
              title="No tool usage data"
              description="No tool invocations in this time range"
            />
          }
        >
          <div class="space-y-1">
            <For each={props.data}>
              {(tool, i) => (
                <div class="flex items-center gap-3 py-2">
                  <span class="w-6 text-right text-sm text-text-muted">
                    {i() + 1}
                  </span>
                  <span class="min-w-0 flex-1 truncate font-mono text-sm text-text-primary">
                    {tool.toolName}
                  </span>
                  <span class="w-16 text-right font-mono text-sm text-text-secondary">
                    {tool.invocationCount.toLocaleString()}
                  </span>
                  <div class="w-20" role="presentation" aria-hidden="true">
                    <div
                      class="h-1.5 rounded-full bg-interactive-default"
                      style={{
                        width: `${Math.max((tool.invocationCount / maxCount()) * 100, 2)}%`,
                      }}
                    />
                  </div>
                </div>
              )}
            </For>
          </div>
        </Show>
      </Show>
    </div>
  );
}
