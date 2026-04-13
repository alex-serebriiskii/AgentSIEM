import { describe, it, expect } from "vitest";
import { render } from "@solidjs/testing-library";
import MetricsRow, { type MetricWidget } from "~/views/dashboard/MetricsRow";

const baseMetrics: MetricWidget[] = [
  { label: "Active Agents", value: 5, sparklineData: [] },
  { label: "Events / Hour", value: 120, sparklineData: [100, 110, 105, 120] },
  { label: "Open Alerts", value: 3, sparklineData: [] },
  { label: "Tokens / Min", value: 800, sparklineData: [700, 750, 780, 800] },
];

describe("MetricsRow", () => {
  it("renders four metric widgets with correct labels", () => {
    const { getByText } = render(() => (
      <MetricsRow metrics={baseMetrics} loading={false} />
    ));
    expect(getByText("Active Agents")).toBeInTheDocument();
    expect(getByText("Events / Hour")).toBeInTheDocument();
    expect(getByText("Open Alerts")).toBeInTheDocument();
    expect(getByText("Tokens / Min")).toBeInTheDocument();
  });

  it("applies severity color when metric exceeds 1.5x average", () => {
    const spikedMetrics: MetricWidget[] = [
      {
        label: "Events / Hour",
        value: 250,
        sparklineData: [10, 10, 10, 250],
      },
    ];
    const { container } = render(() => (
      <MetricsRow metrics={spikedMetrics} loading={false} />
    ));
    const numberEl = container.querySelector(".text-severity-high");
    expect(numberEl).toBeTruthy();
  });

  it("does not apply severity color when within normal range", () => {
    const normalMetrics: MetricWidget[] = [
      {
        label: "Events / Hour",
        value: 12,
        sparklineData: [10, 10, 10, 12],
      },
    ];
    const { container } = render(() => (
      <MetricsRow metrics={normalMetrics} loading={false} />
    ));
    const numberEl = container.querySelector(".text-severity-high");
    expect(numberEl).toBeNull();
  });

  it("shows loading skeletons when loading", () => {
    const { container, queryByText } = render(() => (
      <MetricsRow metrics={[]} loading={true} />
    ));
    const skeletons = container.querySelectorAll(".animate-pulse");
    expect(skeletons.length).toBeGreaterThan(0);
    expect(queryByText("Active Agents")).toBeNull();
  });
});
