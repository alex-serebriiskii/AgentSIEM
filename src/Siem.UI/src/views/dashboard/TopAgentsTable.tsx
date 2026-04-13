import type { ColumnDef } from "@tanstack/solid-table";
import { useNavigate } from "@solidjs/router";
import type { TopAgentResult } from "~/api/types";
import DataTable from "~/components/DataTable";

export interface TopAgentsTableProps {
  data: TopAgentResult[];
  loading: boolean;
}

function formatTokens(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toFixed(1)}M`;
  if (n >= 1_000) return `${(n / 1_000).toFixed(1)}k`;
  return String(n);
}

function formatLatency(ms: number): string {
  if (ms >= 1_000) return `${(ms / 1_000).toFixed(1)}s`;
  return `${Math.round(ms)}ms`;
}

const columns: ColumnDef<TopAgentResult, unknown>[] = [
  {
    accessorKey: "agentName",
    header: "Agent",
  },
  {
    accessorKey: "totalEvents",
    header: "Events",
    cell: (info) => (info.getValue() as number).toLocaleString(),
    meta: { mono: true },
  },
  {
    accessorKey: "totalTokens",
    header: "Tokens",
    cell: (info) => formatTokens(info.getValue() as number),
    meta: { mono: true },
  },
  {
    accessorKey: "maxLatencyMs",
    header: "Max Latency",
    cell: (info) => formatLatency(info.getValue() as number),
    meta: { mono: true },
  },
];

export default function TopAgentsTable(props: TopAgentsTableProps) {
  const navigate = useNavigate();

  return (
    <DataTable
      columns={columns}
      data={props.data}
      loading={props.loading}
      onRowClick={(row) => navigate(`/investigate/agents/${row.agentId}`)}
      emptyTitle="No agent activity"
      emptyDescription="No agents have reported events in this time range"
    />
  );
}
