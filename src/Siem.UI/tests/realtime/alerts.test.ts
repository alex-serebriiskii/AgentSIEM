import { describe, it, expect, vi, beforeEach } from "vitest";
import type { SignalRAlertPayload } from "~/api/types";

// Mock connection module to capture the AlertReceived handler
let alertReceivedHandler: ((payload: SignalRAlertPayload) => void) | null =
  null;

vi.mock("~/realtime/connection", () => ({
  onAlertReceived: vi.fn((cb: (payload: SignalRAlertPayload) => void) => {
    alertReceivedHandler = cb;
    return () => {
      alertReceivedHandler = null;
    };
  }),
}));

let alertsModule: typeof import("~/realtime/alerts");

function makePayload(overrides?: Partial<SignalRAlertPayload>): SignalRAlertPayload {
  return {
    alertId: crypto.randomUUID(),
    ruleId: crypto.randomUUID(),
    ruleName: "Test Rule",
    severity: "high",
    title: "Test Alert",
    agentId: "agent-01",
    agentName: "TestAgent",
    sessionId: "session-01",
    triggeredAt: new Date().toISOString(),
    recentAlertCount: 1,
    labels: {},
    ...overrides,
  };
}

beforeEach(async () => {
  vi.clearAllMocks();
  vi.resetModules();
  alertReceivedHandler = null;
  alertsModule = await import("~/realtime/alerts");
  alertsModule.initAlertListener();
});

describe("Real-time alert state", () => {
  it("increments openAlertCount on AlertReceived", () => {
    expect(alertsModule.openAlertCount()).toBe(0);

    alertReceivedHandler!(makePayload());
    expect(alertsModule.openAlertCount()).toBe(1);

    alertReceivedHandler!(makePayload());
    expect(alertsModule.openAlertCount()).toBe(2);
  });

  it("caps recentAlerts ring buffer at 50", () => {
    for (let i = 0; i < 55; i++) {
      alertReceivedHandler!(makePayload());
    }

    expect(alertsModule.recentAlerts().length).toBe(50);
  });

  it("notifies subscribers", () => {
    const subscriber = vi.fn();
    alertsModule.subscribe(subscriber);

    const payload = makePayload();
    alertReceivedHandler!(payload);

    expect(subscriber).toHaveBeenCalledWith(payload);
  });

  it("deduplicates alerts by alertId", () => {
    const fixedId = "dup-alert-id";
    const payload = makePayload({ alertId: fixedId });

    alertReceivedHandler!(payload);
    alertReceivedHandler!(payload);
    alertReceivedHandler!(payload);

    // Count should only increment once for the unique alert
    expect(alertsModule.openAlertCount()).toBe(1);
    // Only one entry in recent alerts
    expect(alertsModule.recentAlerts().length).toBe(1);
  });

  it("does not deduplicate alerts with different IDs", () => {
    alertReceivedHandler!(makePayload({ alertId: "a1" }));
    alertReceivedHandler!(makePayload({ alertId: "a2" }));
    alertReceivedHandler!(makePayload({ alertId: "a3" }));

    expect(alertsModule.openAlertCount()).toBe(3);
    expect(alertsModule.recentAlerts().length).toBe(3);
  });

  it("allows resetting the alert count", () => {
    alertReceivedHandler!(makePayload());
    alertReceivedHandler!(makePayload());
    expect(alertsModule.openAlertCount()).toBe(2);

    alertsModule.resetAlertCount(0);
    expect(alertsModule.openAlertCount()).toBe(0);
  });

  it("unsubscribes external subscribers", () => {
    const subscriber = vi.fn();
    const unsub = alertsModule.subscribe(subscriber);

    unsub();
    alertReceivedHandler!(makePayload());

    expect(subscriber).not.toHaveBeenCalled();
  });
});
