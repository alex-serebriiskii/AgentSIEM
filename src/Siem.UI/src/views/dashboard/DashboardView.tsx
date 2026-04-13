import { useSearchParams } from "@solidjs/router";
import { createMemo, createResource, Show } from "solid-js";
import {
  fetchAlertDistribution,
  fetchEventVolume,
  fetchToolUsage,
  fetchTopAgents,
} from "~/api/endpoints";
import type { MetricWidget } from "./MetricsRow";
import AlertDistribution from "./AlertDistribution";
import MetricsRow from "./MetricsRow";
import TimeRangeSelector from "./TimeRangeSelector";
import ToolUsageList from "./ToolUsageList";
import TopAgentsTable from "./TopAgentsTable";

const VALID_HOURS = new Set([1, 6, 24, 168]);

export default function DashboardView() {
  const [searchParams, setSearchParams] = useSearchParams();

  const hours = createMemo(() => {
    const h = Number(searchParams.hours);
    return VALID_HOURS.has(h) ? h : 24;
  });

  const handleTimeRangeChange = (h: number) => {
    setSearchParams({ hours: h === 24 ? undefined : String(h) }, { replace: true });
  };

  const [topAgents] = createResource(hours, (h) => fetchTopAgents(h, 20));
  const [eventVolume] = createResource(hours, (h) => fetchEventVolume(h));
  const [alertDist] = createResource(hours, (h) => fetchAlertDistribution(h));
  const [toolUsage] = createResource(hours, (h) => fetchToolUsage(h, 15));

  const metrics = createMemo<MetricWidget[]>(() => {
    const vol = eventVolume() ?? [];
    const alerts = alertDist() ?? [];
    const agents = topAgents() ?? [];

    const eventCounts = vol.map((b) => b.eventCount);
    const tokenCounts = vol.map((b) => b.totalTokens);

    return [
      {
        label: "Top Agents",
        value: agents.length,
        sparklineData: [],
      },
      {
        label: "Events / Hour",
        value: eventCounts.length > 0 ? eventCounts[eventCounts.length - 1] : 0,
        sparklineData: eventCounts,
      },
      {
        label: "Open Alerts",
        value: alerts
          .filter((a) => a.status === "open")
          .reduce((sum, a) => sum + a.count, 0),
        sparklineData: [],
      },
      {
        label: "Tokens / Min",
        value:
          tokenCounts.length > 0
            ? Math.round(tokenCounts[tokenCounts.length - 1] / 60)
            : 0,
        sparklineData: tokenCounts.map((t) => Math.round(t / 60)),
        formatValue: (n: number) =>
          n >= 1_000 ? `${(n / 1_000).toFixed(1)}k` : String(n),
      },
    ];
  });

  const metricsLoading = () =>
    eventVolume.loading || topAgents.loading || alertDist.loading;

  const errors = createMemo(() => {
    const errs: string[] = [];
    if (topAgents.error) errs.push("top agents");
    if (eventVolume.error) errs.push("event volume");
    if (alertDist.error) errs.push("alert distribution");
    if (toolUsage.error) errs.push("tool usage");
    return errs;
  });

  return (
    <div class="space-y-8">
      <div class="flex items-center justify-between">
        <h1 class="text-2xl font-light text-text-primary">Dashboard</h1>
        <TimeRangeSelector hours={hours()} onChange={handleTimeRangeChange} />
      </div>

      <Show when={errors().length > 0}>
        <div
          role="alert"
          class="rounded-lg border border-severity-high/30 bg-severity-high/10 px-4 py-3 text-sm text-severity-high"
        >
          Failed to load {errors().join(", ")}. Dashboard may show incomplete data.
        </div>
      </Show>

      <MetricsRow metrics={metrics()} loading={metricsLoading()} />

      <div class="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <AlertDistribution data={alertDist() ?? []} loading={alertDist.loading} />
        <ToolUsageList data={toolUsage() ?? []} loading={toolUsage.loading} />
      </div>

      <div>
        <h2 class="mb-4 text-sm font-medium uppercase tracking-wider text-text-secondary">
          Top Agents
        </h2>
        <TopAgentsTable data={topAgents() ?? []} loading={topAgents.loading} />
      </div>
    </div>
  );
}
