import { For } from "solid-js";

function SkeletonBlock(props: { class?: string }) {
  return (
    <div
      class={`animate-pulse rounded bg-surface-overlay ${props.class ?? ""}`}
    />
  );
}

export function SkeletonText(props: { width?: string }) {
  return (
    <SkeletonBlock
      class={`h-4 ${props.width ?? "w-3/4"}`}
    />
  );
}

export function SkeletonRow(props: { columns?: number }) {
  const cols = () => props.columns ?? 4;
  return (
    <div class="flex items-center gap-4 py-3">
      <For each={Array.from({ length: cols() })}>
        {(_, i) => (
          <SkeletonBlock
            class={`h-4 ${i() === 0 ? "w-32" : "flex-1"}`}
          />
        )}
      </For>
    </div>
  );
}

export function SkeletonCard() {
  return (
    <div class="rounded-lg border border-border-default bg-surface-raised p-4">
      <SkeletonBlock class="mb-3 h-5 w-1/3" />
      <div class="space-y-2">
        <SkeletonBlock class="h-4 w-full" />
        <SkeletonBlock class="h-4 w-5/6" />
        <SkeletonBlock class="h-4 w-2/3" />
      </div>
    </div>
  );
}

export function SkeletonTable(props: { rows?: number; columns?: number }) {
  const rows = () => props.rows ?? 5;
  const cols = () => props.columns ?? 4;
  return (
    <div class="divide-y divide-border-muted">
      {/* Header */}
      <div class="flex items-center gap-4 py-3">
        <For each={Array.from({ length: cols() })}>
          {() => <SkeletonBlock class="h-3 flex-1" />}
        </For>
      </div>
      {/* Rows */}
      <For each={Array.from({ length: rows() })}>
        {() => <SkeletonRow columns={cols()} />}
      </For>
    </div>
  );
}
