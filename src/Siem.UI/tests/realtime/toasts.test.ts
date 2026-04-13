import { describe, it, expect, vi, beforeEach } from "vitest";
import type { SignalRAlertPayload } from "~/api/types";

// Capture subscriber registered via subscribe()
let subscriberFn: ((alert: SignalRAlertPayload) => void) | null = null;
const mockUnsubscribe = vi.fn();

vi.mock("~/realtime/alerts", () => ({
  subscribe: vi.fn((fn: (alert: SignalRAlertPayload) => void) => {
    subscriberFn = fn;
    return () => {
      subscriberFn = null;
      mockUnsubscribe();
    };
  }),
}));

// Mock the toaster to capture what gets shown
const mockShow = vi.fn();
const mockDismiss = vi.fn();

vi.mock("@kobalte/core/toast", () => ({
  Toast: {},
  toaster: {
    show: mockShow,
    dismiss: mockDismiss,
  },
}));

let toastsModule: typeof import("~/realtime/toasts");

beforeEach(async () => {
  vi.clearAllMocks();
  subscriberFn = null;
  vi.resetModules();
  toastsModule = await import("~/realtime/toasts");
});

function makePayload(overrides?: Partial<SignalRAlertPayload>): SignalRAlertPayload {
  return {
    alertId: "alert-1",
    ruleId: "rule-1",
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

describe("Toast listener", () => {
  it("subscribes to alerts on init", () => {
    const navigate = vi.fn();
    toastsModule.initToastListener(navigate);

    expect(subscriberFn).not.toBeNull();
  });

  it("shows a toast when an alert arrives", () => {
    const navigate = vi.fn();
    toastsModule.initToastListener(navigate);

    subscriberFn!(makePayload());

    expect(mockShow).toHaveBeenCalledOnce();
  });

  it("unsubscribes on cleanup", () => {
    const navigate = vi.fn();
    const cleanup = toastsModule.initToastListener(navigate);

    cleanup();

    expect(mockUnsubscribe).toHaveBeenCalledOnce();
    expect(subscriberFn).toBeNull();
  });

  it("shows toast for each severity level", () => {
    const navigate = vi.fn();
    toastsModule.initToastListener(navigate);

    for (const severity of ["critical", "high", "medium", "low"] as const) {
      subscriberFn!(makePayload({ severity }));
    }

    expect(mockShow).toHaveBeenCalledTimes(4);
  });
});
