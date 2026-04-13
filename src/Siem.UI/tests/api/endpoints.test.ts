import { describe, it, expect, vi, beforeEach } from "vitest";

const mockGet = vi.fn().mockResolvedValue({});
const mockPost = vi.fn().mockResolvedValue({});
const mockPut = vi.fn().mockResolvedValue({});
const mockDel = vi.fn().mockResolvedValue(undefined);

vi.mock("~/api/client", () => ({
  get: (...args: unknown[]) => mockGet(...args),
  post: (...args: unknown[]) => mockPost(...args),
  put: (...args: unknown[]) => mockPut(...args),
  del: (...args: unknown[]) => mockDel(...args),
}));

import {
  fetchTopAgents,
  fetchEventVolume,
  fetchAlertDistribution,
  fetchToolUsage,
  fetchAlerts,
  fetchAlert,
  acknowledgeAlert,
  resolveAlert,
  searchEvents,
  fetchSessions,
  fetchSession,
  fetchSessionTimeline,
  fetchAgentRisk,
  fetchRules,
  fetchRule,
  createRule,
  updateRule,
  deleteRule,
  activateRule,
  fetchLists,
  fetchList,
  createList,
  updateListMembers,
  fetchSuppressions,
  createSuppression,
  deleteSuppression,
  fetchEngineStatus,
  recompileEngine,
} from "~/api/endpoints";

beforeEach(() => {
  vi.clearAllMocks();
});

// ---------------------------------------------------------------------------
// Dashboard
// ---------------------------------------------------------------------------

describe("Dashboard endpoints", () => {
  it("fetchTopAgents passes hours, limit, and signal", async () => {
    const signal = AbortSignal.timeout(5000);
    await fetchTopAgents(24, 10, signal);
    expect(mockGet).toHaveBeenCalledWith("/api/dashboard/top-agents", {
      params: { hours: 24, limit: 10 },
      signal,
    });
  });

  it("fetchEventVolume passes hours and signal", async () => {
    await fetchEventVolume(12);
    expect(mockGet).toHaveBeenCalledWith("/api/dashboard/event-volume", {
      params: { hours: 12 },
      signal: undefined,
    });
  });

  it("fetchAlertDistribution passes hours", async () => {
    await fetchAlertDistribution(6);
    expect(mockGet).toHaveBeenCalledWith("/api/dashboard/alert-distribution", {
      params: { hours: 6 },
      signal: undefined,
    });
  });

  it("fetchToolUsage passes hours and limit", async () => {
    await fetchToolUsage(24, 5);
    expect(mockGet).toHaveBeenCalledWith("/api/dashboard/tool-usage", {
      params: { hours: 24, limit: 5 },
      signal: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// Alerts
// ---------------------------------------------------------------------------

describe("Alert endpoints", () => {
  it("fetchAlerts passes filter params", async () => {
    await fetchAlerts({ status: "open", severity: "high", page: 1 });
    expect(mockGet).toHaveBeenCalledWith("/api/alerts", {
      params: expect.objectContaining({ status: "open", severity: "high", page: 1 }),
      signal: undefined,
    });
  });

  it("fetchAlert uses correct URL", async () => {
    await fetchAlert("alert-123");
    expect(mockGet).toHaveBeenCalledWith("/api/alerts/alert-123", {
      signal: undefined,
    });
  });

  it("acknowledgeAlert sends PUT to correct URL", async () => {
    await acknowledgeAlert("alert-456");
    expect(mockPut).toHaveBeenCalledWith(
      "/api/alerts/alert-456/acknowledge",
      undefined,
      { signal: undefined },
    );
  });

  it("resolveAlert sends PUT with body", async () => {
    const body = { resolutionNote: "fixed" };
    await resolveAlert("alert-789", body);
    expect(mockPut).toHaveBeenCalledWith(
      "/api/alerts/alert-789/resolve",
      body,
      { signal: undefined },
    );
  });
});

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

describe("Event endpoints", () => {
  it("searchEvents passes search params", async () => {
    await searchEvents({ agent_id: "a1", event_type: "tool_call", page: 2 });
    expect(mockGet).toHaveBeenCalledWith("/api/events", {
      params: expect.objectContaining({ agent_id: "a1", event_type: "tool_call", page: 2 }),
      signal: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// Sessions
// ---------------------------------------------------------------------------

describe("Session endpoints", () => {
  it("fetchSessions passes filter params", async () => {
    await fetchSessions({ agent_id: "a1", has_alerts: true });
    expect(mockGet).toHaveBeenCalledWith("/api/sessions", {
      params: expect.objectContaining({ agent_id: "a1", has_alerts: true }),
      signal: undefined,
    });
  });

  it("fetchSession uses correct URL", async () => {
    await fetchSession("sess-1");
    expect(mockGet).toHaveBeenCalledWith("/api/sessions/sess-1", {
      signal: undefined,
    });
  });

  it("fetchSessionTimeline passes limit", async () => {
    await fetchSessionTimeline("sess-1", 100);
    expect(mockGet).toHaveBeenCalledWith("/api/sessions/sess-1/timeline", {
      params: { limit: 100 },
      signal: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// Agents
// ---------------------------------------------------------------------------

describe("Agent endpoints", () => {
  it("fetchAgentRisk passes lookback", async () => {
    await fetchAgentRisk("agent-1", "24h");
    expect(mockGet).toHaveBeenCalledWith("/api/agents/agent-1/risk", {
      params: { lookback: "24h" },
      signal: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// Rules
// ---------------------------------------------------------------------------

describe("Rule endpoints", () => {
  it("fetchRules passes enabled filter", async () => {
    await fetchRules(true);
    expect(mockGet).toHaveBeenCalledWith("/api/rules", {
      params: { enabled: true },
      signal: undefined,
    });
  });

  it("fetchRule uses correct URL", async () => {
    await fetchRule("rule-1");
    expect(mockGet).toHaveBeenCalledWith("/api/rules/rule-1", {
      signal: undefined,
    });
  });

  it("createRule sends POST with body", async () => {
    const body = {
      name: "Test",
      description: "desc",
      conditionJson: {},
      createdBy: "admin",
    };
    await createRule(body);
    expect(mockPost).toHaveBeenCalledWith("/api/rules", body, {
      signal: undefined,
    });
  });

  it("updateRule sends PUT with body", async () => {
    const body = { name: "Updated" };
    await updateRule("rule-1", body);
    expect(mockPut).toHaveBeenCalledWith("/api/rules/rule-1", body, {
      signal: undefined,
    });
  });

  it("deleteRule sends DELETE", async () => {
    await deleteRule("rule-1");
    expect(mockDel).toHaveBeenCalledWith("/api/rules/rule-1", {
      signal: undefined,
    });
  });

  it("activateRule sends POST", async () => {
    await activateRule("rule-1");
    expect(mockPost).toHaveBeenCalledWith(
      "/api/rules/rule-1/activate",
      undefined,
      { signal: undefined },
    );
  });
});

// ---------------------------------------------------------------------------
// Managed Lists
// ---------------------------------------------------------------------------

describe("Managed List endpoints", () => {
  it("fetchLists calls GET /api/lists", async () => {
    await fetchLists();
    expect(mockGet).toHaveBeenCalledWith("/api/lists", {
      signal: undefined,
    });
  });

  it("fetchList uses correct URL", async () => {
    await fetchList("list-1");
    expect(mockGet).toHaveBeenCalledWith("/api/lists/list-1", {
      signal: undefined,
    });
  });

  it("createList sends POST with body", async () => {
    const body = { name: "Blocklist", description: "Blocked agents" };
    await createList(body);
    expect(mockPost).toHaveBeenCalledWith("/api/lists", body, {
      signal: undefined,
    });
  });

  it("updateListMembers sends PUT with body", async () => {
    const body = { members: ["a", "b"] };
    await updateListMembers("list-1", body);
    expect(mockPut).toHaveBeenCalledWith("/api/lists/list-1/members", body, {
      signal: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// Suppressions
// ---------------------------------------------------------------------------

describe("Suppression endpoints", () => {
  it("fetchSuppressions passes filter params", async () => {
    await fetchSuppressions({ rule_id: "r1" });
    expect(mockGet).toHaveBeenCalledWith("/api/suppressions", {
      params: expect.objectContaining({ rule_id: "r1" }),
      signal: undefined,
    });
  });

  it("createSuppression sends POST with body", async () => {
    const body = {
      reason: "Maintenance",
      createdBy: "admin",
      durationMinutes: 60,
    };
    await createSuppression(body);
    expect(mockPost).toHaveBeenCalledWith("/api/suppressions", body, {
      signal: undefined,
    });
  });

  it("deleteSuppression sends DELETE", async () => {
    await deleteSuppression("sup-1");
    expect(mockDel).toHaveBeenCalledWith("/api/suppressions/sup-1", {
      signal: undefined,
    });
  });
});

// ---------------------------------------------------------------------------
// Engine
// ---------------------------------------------------------------------------

describe("Engine endpoints", () => {
  it("fetchEngineStatus calls GET /api/engine/status", async () => {
    await fetchEngineStatus();
    expect(mockGet).toHaveBeenCalledWith("/api/engine/status", {
      signal: undefined,
    });
  });

  it("recompileEngine sends POST", async () => {
    await recompileEngine();
    expect(mockPost).toHaveBeenCalledWith(
      "/api/engine/recompile",
      undefined,
      { signal: undefined },
    );
  });
});
