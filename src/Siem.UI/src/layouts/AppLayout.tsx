import { Toast } from "@kobalte/core/toast";
import type { RouteSectionProps } from "@solidjs/router";
import { useNavigate } from "@solidjs/router";
import { onCleanup, onMount } from "solid-js";
import { initAlertListener } from "~/realtime/alerts";
import {
  getConnection,
  startConnection,
  stopConnection,
} from "~/realtime/connection";
import { initToastListener } from "~/realtime/toasts";
import ConnectionBanner from "./ConnectionBanner";
import Sidebar from "./Sidebar";
import TopBar from "./TopBar";

export default function AppLayout(props: RouteSectionProps) {
  const navigate = useNavigate();

  let cleanupAlerts: (() => void) | undefined;
  let cleanupToasts: (() => void) | undefined;

  onMount(() => {
    startConnection();
    // Only register listeners once the connection object exists
    if (getConnection()) {
      cleanupAlerts = initAlertListener();
      cleanupToasts = initToastListener(navigate);
    }
  });

  onCleanup(() => {
    cleanupToasts?.();
    cleanupAlerts?.();
    stopConnection();
  });

  return (
    <div class="flex h-screen bg-surface-base">
      <Sidebar />
      <div class="flex flex-1 flex-col overflow-hidden">
        <TopBar />
        <ConnectionBanner />
        <main class="flex-1 overflow-auto p-6">{props.children}</main>
      </div>

      <Toast.Region>
        <Toast.List class="fixed bottom-4 right-4 z-50 flex flex-col gap-2" />
      </Toast.Region>
    </div>
  );
}
