import { Show } from "solid-js";
import { connectionState } from "~/realtime/connection";

export default function ConnectionBanner() {
  const isDisconnected = () => connectionState() === "disconnected";
  const isReconnecting = () => connectionState() === "reconnecting";
  const showBanner = () => isDisconnected() || isReconnecting();

  return (
    <Show when={showBanner()}>
      <div
        role="alert"
        class={`flex items-center gap-2 px-4 py-2 text-sm font-medium ${
          isDisconnected()
            ? "bg-severity-critical/10 text-severity-critical"
            : "bg-severity-medium/10 text-severity-medium"
        }`}
      >
        <span class="inline-block h-2 w-2 animate-pulse rounded-full bg-current" />
        {isDisconnected()
          ? "Connection lost. Attempting to reconnect..."
          : "Live updates paused — reconnecting..."}
      </div>
    </Show>
  );
}
