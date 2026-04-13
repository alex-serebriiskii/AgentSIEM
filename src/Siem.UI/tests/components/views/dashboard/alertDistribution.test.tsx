import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, waitFor, fireEvent } from "@solidjs/testing-library";
import { Route, Router } from "@solidjs/router";
import AlertDistribution from "~/views/dashboard/AlertDistribution";
import type { AlertDistributionResult } from "~/api/types";

const mockData: AlertDistributionResult[] = [
  { severity: "critical", status: "open", count: 3 },
  { severity: "critical", status: "acknowledged", count: 1 },
  { severity: "high", status: "open", count: 5 },
  { severity: "medium", status: "open", count: 10 },
  { severity: "low", status: "open", count: 2 },
];

function renderInRoute(data: AlertDistributionResult[], loading = false) {
  window.history.pushState({}, "", "/");
  return render(() => (
    <Router>
      <Route
        path="*"
        component={() => <AlertDistribution data={data} loading={loading} />}
      />
    </Router>
  ));
}

beforeEach(() => {
  window.history.pushState({}, "", "/");
});

describe("AlertDistribution", () => {
  it("renders segments for each severity with data", async () => {
    const { container } = renderInRoute(mockData);
    await waitFor(() => {
      const segments = container.querySelectorAll("[data-testid^='segment-']");
      expect(segments.length).toBe(4);
    });
  });

  it("shows counts in the legend", async () => {
    const { getByText } = renderInRoute(mockData);
    await waitFor(() => {
      // Critical: 3 + 1 = 4
      expect(getByText("4")).toBeInTheDocument();
      // High: 5
      expect(getByText("5")).toBeInTheDocument();
      // Medium: 10
      expect(getByText("10")).toBeInTheDocument();
      // Low: 2
      expect(getByText("2")).toBeInTheDocument();
    });
  });

  it("navigates to alerts view on segment click", async () => {
    const { container } = renderInRoute(mockData);
    await waitFor(() => {
      const criticalSegment = container.querySelector(
        "[data-testid='segment-critical']",
      );
      expect(criticalSegment).toBeTruthy();
      fireEvent.click(criticalSegment!);
    });
    await waitFor(() => {
      expect(window.location.pathname).toBe("/alerts");
      expect(window.location.search).toContain("severity=critical");
    });
  });

  it("shows empty state when no alerts", async () => {
    const { getByText } = renderInRoute([]);
    await waitFor(() => {
      expect(getByText("No alerts")).toBeInTheDocument();
    });
  });
});
