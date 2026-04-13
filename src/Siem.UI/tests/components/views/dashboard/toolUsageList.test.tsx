import { describe, it, expect } from "vitest";
import { render } from "@solidjs/testing-library";
import ToolUsageList from "~/views/dashboard/ToolUsageList";
import type { ToolUsageResult } from "~/api/types";

const mockData: ToolUsageResult[] = [
  { toolName: "web_search", invocationCount: 200, avgLatencyMs: 120, uniqueSessions: 5 },
  { toolName: "code_exec", invocationCount: 100, avgLatencyMs: 80, uniqueSessions: 3 },
  { toolName: "file_read", invocationCount: 1, avgLatencyMs: 10, uniqueSessions: 1 },
];

describe("ToolUsageList", () => {
  it("renders tool names and counts", () => {
    const { getByText, container } = render(() => (
      <ToolUsageList data={mockData} loading={false} />
    ));
    expect(getByText("web_search")).toBeInTheDocument();
    expect(getByText("code_exec")).toBeInTheDocument();
    expect(getByText("file_read")).toBeInTheDocument();
    expect(getByText("200")).toBeInTheDocument();
    expect(getByText("100")).toBeInTheDocument();
    // Count columns use font-mono + text-text-secondary
    const countCells = container.querySelectorAll(".font-mono.text-text-secondary");
    const counts = Array.from(countCells).map((el) => el.textContent);
    expect(counts).toEqual(["200", "100", "1"]);
  });

  it("renders rank numbers starting at 1", () => {
    const { container } = render(() => (
      <ToolUsageList data={mockData} loading={false} />
    ));
    const rankCells = container.querySelectorAll(".text-text-muted.w-6");
    const ranks = Array.from(rankCells).map((el) => el.textContent?.trim());
    expect(ranks).toEqual(["1", "2", "3"]);
  });

  it("applies minimum 2% bar width for low-count items", () => {
    const { container } = render(() => (
      <ToolUsageList data={mockData} loading={false} />
    ));
    const bars = container.querySelectorAll(".bg-interactive-default");
    // file_read has 1/200 = 0.5%, should be clamped to 2%
    const lastBar = bars[bars.length - 1] as HTMLElement;
    expect(lastBar.style.width).toBe("2%");
  });

  it("sets 100% bar width for top item", () => {
    const { container } = render(() => (
      <ToolUsageList data={mockData} loading={false} />
    ));
    const bars = container.querySelectorAll(".bg-interactive-default");
    const firstBar = bars[0] as HTMLElement;
    expect(firstBar.style.width).toBe("100%");
  });

  it("shows loading skeletons when loading", () => {
    const { container, queryByText } = render(() => (
      <ToolUsageList data={[]} loading={true} />
    ));
    const skeletons = container.querySelectorAll(".animate-pulse");
    expect(skeletons.length).toBeGreaterThan(0);
    expect(queryByText("web_search")).toBeNull();
  });

  it("shows empty state when no data", () => {
    const { getByText } = render(() => (
      <ToolUsageList data={[]} loading={false} />
    ));
    expect(getByText("No tool usage data")).toBeInTheDocument();
  });
});
