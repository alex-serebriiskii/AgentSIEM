import type { JSX } from "solid-js";
import { Show } from "solid-js";

export interface EmptyStateProps {
  title: string;
  description?: string;
  action?: JSX.Element;
}

export default function EmptyState(props: EmptyStateProps) {
  return (
    <div class="flex flex-col items-center justify-center py-12 text-center">
      <svg
        class="mb-4 h-12 w-12 text-text-muted"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="1"
        stroke-linecap="round"
        stroke-linejoin="round"
      >
        <circle cx="12" cy="12" r="10" />
        <line x1="8" y1="12" x2="16" y2="12" />
      </svg>
      <h3 class="text-sm font-medium text-text-secondary">{props.title}</h3>
      <Show when={props.description}>
        <p class="mt-1 text-sm text-text-muted">{props.description}</p>
      </Show>
      <Show when={props.action}>
        <div class="mt-4">{props.action}</div>
      </Show>
    </div>
  );
}
