import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";

// We need to mock signalR before importing the module under test
const mockConnection = {
  start: vi.fn().mockResolvedValue(undefined),
  stop: vi.fn().mockResolvedValue(undefined),
  on: vi.fn(),
  off: vi.fn(),
  onreconnecting: vi.fn(),
  onreconnected: vi.fn(),
  onclose: vi.fn(),
};

const mockBuilder = {
  withUrl: vi.fn().mockReturnThis(),
  withAutomaticReconnect: vi.fn().mockReturnThis(),
  build: vi.fn().mockReturnValue(mockConnection),
};

vi.mock("@microsoft/signalr", () => {
  // HubConnectionBuilder must be constructable with `new`
  function HubConnectionBuilder() {
    return mockBuilder;
  }
  return { HubConnectionBuilder };
});

let connectionModule: typeof import("~/realtime/connection");

beforeEach(async () => {
  vi.clearAllMocks();
  vi.resetModules();
  connectionModule = await import("~/realtime/connection");
});

afterEach(() => {
  try {
    connectionModule.stopConnection();
  } catch {
    // ignore
  }
});

describe("SignalR connection", () => {
  it("initial state is disconnected", () => {
    expect(connectionModule.connectionState()).toBe("disconnected");
  });

  it("transitions to connected on successful start", async () => {
    connectionModule.startConnection();
    await vi.waitFor(() => {
      expect(connectionModule.connectionState()).toBe("connected");
    });
  });

  it("transitions to reconnecting on connection drop", async () => {
    connectionModule.startConnection();
    await vi.waitFor(() => {
      expect(connectionModule.connectionState()).toBe("connected");
    });

    // Simulate onreconnecting callback
    const onReconnectingCb = mockConnection.onreconnecting.mock.calls[0][0];
    onReconnectingCb();

    expect(connectionModule.connectionState()).toBe("reconnecting");
  });

  it("transitions to disconnected on close", async () => {
    connectionModule.startConnection();
    await vi.waitFor(() => {
      expect(connectionModule.connectionState()).toBe("connected");
    });

    // Simulate onclose callback
    const onCloseCb = mockConnection.onclose.mock.calls[0][0];
    onCloseCb();

    expect(connectionModule.connectionState()).toBe("disconnected");
  });
});
