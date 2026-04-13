import * as signalR from "@microsoft/signalr";
import { createSignal } from "solid-js";
import type { SignalRAlertPayload } from "~/api/types";

// ---------------------------------------------------------------------------
// Connection state signal
// ---------------------------------------------------------------------------

export type ConnectionState =
  | "disconnected"
  | "connecting"
  | "connected"
  | "reconnecting";

const [connectionState, setConnectionState] =
  createSignal<ConnectionState>("disconnected");

export { connectionState };

// ---------------------------------------------------------------------------
// Module-scoped singleton
// ---------------------------------------------------------------------------

let connection: signalR.HubConnection | null = null;
let retryTimer: ReturnType<typeof setTimeout> | null = null;

const INITIAL_RETRY_DELAYS = [0, 2000, 5000, 10000, 30000];

export function getConnection(): signalR.HubConnection | null {
  return connection;
}

function attemptStart(conn: signalR.HubConnection, attempt = 0): void {
  setConnectionState("connecting");
  conn.start().then(
    () => setConnectionState("connected"),
    () => {
      const delay =
        INITIAL_RETRY_DELAYS[Math.min(attempt, INITIAL_RETRY_DELAYS.length - 1)];
      retryTimer = setTimeout(() => {
        retryTimer = null;
        if (connection === conn) {
          attemptStart(conn, attempt + 1);
        }
      }, delay);
    },
  );
}

export function startConnection(): void {
  if (connection) return;

  connection = new signalR.HubConnectionBuilder()
    .withUrl("/hubs/alerts")
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .build();

  connection.onreconnecting(() => {
    setConnectionState("reconnecting");
  });

  connection.onreconnected(() => {
    setConnectionState("connected");
    // Notify listeners that reconnection happened so they can backfill
    for (const cb of reconnectCallbacks) {
      cb();
    }
  });

  connection.onclose(() => {
    setConnectionState("disconnected");
  });

  attemptStart(connection);
}

export async function stopConnection(): Promise<void> {
  if (retryTimer != null) {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
  if (!connection) return;
  const conn = connection;
  connection = null;
  setConnectionState("disconnected");
  await conn.stop();
}

// ---------------------------------------------------------------------------
// Alert event registration
// ---------------------------------------------------------------------------

export function onAlertReceived(
  callback: (payload: SignalRAlertPayload) => void,
): () => void {
  const conn = connection;
  if (!conn) {
    throw new Error("SignalR connection not started");
  }
  conn.on("AlertReceived", callback);
  return () => {
    conn.off("AlertReceived", callback);
  };
}

// ---------------------------------------------------------------------------
// Reconnect callbacks (for backfill)
// ---------------------------------------------------------------------------

const reconnectCallbacks = new Set<() => void>();

export function onReconnected(callback: () => void): () => void {
  reconnectCallbacks.add(callback);
  return () => {
    reconnectCallbacks.delete(callback);
  };
}
