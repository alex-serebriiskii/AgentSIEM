import { Tooltip } from "@kobalte/core/tooltip";
import { createMemo, createSignal, onCleanup, onMount } from "solid-js";

export interface RelativeTimestampProps {
  timestamp: string | Date;
  class?: string;
}

function formatRelative(date: Date): string {
  const now = Date.now();
  const diffMs = now - date.getTime();
  const diffSec = Math.floor(diffMs / 1000);

  if (diffSec < 60) return "just now";

  const diffMin = Math.floor(diffSec / 60);
  if (diffMin < 60) return `${diffMin}m ago`;

  const diffHr = Math.floor(diffMin / 60);
  if (diffHr < 24) return `${diffHr}h ago`;

  const diffDay = Math.floor(diffHr / 24);
  if (diffDay === 1) return "yesterday";
  if (diffDay < 30) return `${diffDay}d ago`;

  return date.toLocaleDateString();
}

function formatAbsolute(date: Date): string {
  return date.toLocaleString();
}

export default function RelativeTimestamp(props: RelativeTimestampProps) {
  const [tick, setTick] = createSignal(0);

  const date = createMemo(() => {
    const ts = props.timestamp;
    return ts instanceof Date ? ts : new Date(ts);
  });

  const isoString = createMemo(() => date().toISOString());
  const relative = createMemo(() => {
    tick(); // Subscribe to tick for auto-refresh
    return formatRelative(date());
  });
  const absolute = createMemo(() => formatAbsolute(date()));

  onMount(() => {
    const interval = setInterval(() => setTick((n) => n + 1), 30_000);
    onCleanup(() => clearInterval(interval));
  });

  return (
    <Tooltip>
      <Tooltip.Trigger as="time" datetime={isoString()} class={props.class}>
        {relative()}
      </Tooltip.Trigger>
      <Tooltip.Portal>
        <Tooltip.Content class="rounded bg-surface-overlay px-2 py-1 text-xs text-text-primary shadow-lg">
          <Tooltip.Arrow />
          {absolute()}
        </Tooltip.Content>
      </Tooltip.Portal>
    </Tooltip>
  );
}
