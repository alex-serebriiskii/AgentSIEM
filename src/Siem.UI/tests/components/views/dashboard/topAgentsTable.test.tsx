import { describe, it, expect, beforeEach } from "vitest";
import { render, waitFor } from "@solidjs/testing-library";
import { Route, Router } from "@solidjs/router";
import TopAgentsTable from "~/views/dashboard/TopAgentsTable";
import type { TopAgentResult } from "~/api/types";

const mockData: TopAgentResult[] = [
  { agentId: "a1", agentName: "CodeBot", totalEvents: 1500, totalTokens: 2_500_000, maxLatencyMs: 3200 },
  { agentId: "a2", agentName: "SearchAgent", totalEvents: 800, totalTokens: 450, maxLatencyMs: 75 },
  { agentId: "a3", agentName: "ToolRunner", totalEvents: 50, totalTokens: 999, maxLatencyMs: 999 },
];

function renderInRoute(data: TopAgentResult[], loading = false) {
  window.history.pushState({}, "", "/");
  return render(() => (
    <Router>
      <Route
        path="*"
        component={() => <TopAgentsTable data={data} loading={loading} />}
      />
    </Router>
  ));
}

beforeEach(() => {
  window.history.pushState({}, "", "/");
});

describe("TopAgentsTable", () => {
  it("renders agent names and column headers", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      expect(getByText("Agent")).toBeInTheDocument();
      expect(getByText("Events")).toBeInTheDocument();
      expect(getByText("Tokens")).toBeInTheDocument();
      expect(getByText("Max Latency")).toBeInTheDocument();
      expect(getByText("CodeBot")).toBeInTheDocument();
      expect(getByText("SearchAgent")).toBeInTheDocument();
    });
  });

  it("formats tokens with M suffix for millions", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      // 2_500_000 -> "2.5M"
      expect(getByText("2.5M")).toBeInTheDocument();
    });
  });

  it("formats tokens with k suffix for thousands", async () => {
    const data: TopAgentResult[] = [
      { agentId: "a1", agentName: "Bot", totalEvents: 1, totalTokens: 1_000, maxLatencyMs: 100 },
    ];
    const { getByText } = renderInRoute(data);
    await waitFor(() => {
      expect(getByText("1.0k")).toBeInTheDocument();
    });
  });

  it("formats tokens as plain number below 1000", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      // 450 -> "450", 999 -> "999"
      expect(getByText("450")).toBeInTheDocument();
      expect(getByText("999")).toBeInTheDocument();
    });
  });

  it("formats latency in seconds when >= 1000ms", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      // 3200ms -> "3.2s"
      expect(getByText("3.2s")).toBeInTheDocument();
    });
  });

  it("formats latency in ms when < 1000ms", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      // 75ms -> "75ms", 999ms -> "999ms"
      expect(getByText("75ms")).toBeInTheDocument();
      expect(getByText("999ms")).toBeInTheDocument();
    });
  });

  it("navigates to agent detail on row click", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      expect(getByText("CodeBot")).toBeInTheDocument();
    });
    getByText("CodeBot").closest("tr")!.click();
    await waitFor(() => {
      expect(window.location.pathname).toBe("/investigate/agents/a1");
    });
  });

  it("shows empty state when no data", async () => {
    const { getByText } = renderInRoute([]);
    await waitFor(() => {
      expect(getByText("No agent activity")).toBeInTheDocument();
    });
  });
});
