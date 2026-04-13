import {
  type ColumnDef,
  createSolidTable,
  flexRender,
  getCoreRowModel,
  getSortedRowModel,
  type SortingState,
} from "@tanstack/solid-table";
import { createSignal, For, Show } from "solid-js";
import EmptyState from "./EmptyState";
import { SkeletonTable } from "./LoadingSkeleton";

export interface DataTableProps<T> {
  columns: ColumnDef<T, unknown>[];
  data: T[];
  loading?: boolean;
  onRowClick?: (row: T) => void;
  emptyTitle?: string;
  emptyDescription?: string;
}

export default function DataTable<T>(props: DataTableProps<T>) {
  const [sorting, setSorting] = createSignal<SortingState>([]);

  const table = createSolidTable({
    get data() {
      return props.data;
    },
    get columns() {
      return props.columns;
    },
    state: {
      get sorting() {
        return sorting();
      },
    },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  });

  return (
    <Show
      when={!props.loading}
      fallback={<SkeletonTable rows={5} columns={props.columns.length} />}
    >
      <Show
        when={props.data.length > 0}
        fallback={
          <EmptyState
            title={props.emptyTitle ?? "No data"}
            description={props.emptyDescription}
          />
        }
      >
        <div class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead>
              <For each={table.getHeaderGroups()}>
                {(headerGroup) => (
                  <tr class="border-b border-border-default">
                    <For each={headerGroup.headers}>
                      {(header) => (
                        <th
                          class="px-3 py-2 text-left text-xs font-medium uppercase tracking-wider text-text-muted"
                          classList={{
                            "cursor-pointer select-none hover:text-text-secondary":
                              header.column.getCanSort(),
                          }}
                          onClick={header.column.getToggleSortingHandler()}
                        >
                          <div class="flex items-center gap-1">
                            {header.isPlaceholder
                              ? null
                              : flexRender(
                                  header.column.columnDef.header,
                                  header.getContext(),
                                )}
                            <Show when={header.column.getIsSorted()}>
                              <span class="text-text-secondary">
                                {header.column.getIsSorted() === "asc"
                                  ? " \u2191"
                                  : " \u2193"}
                              </span>
                            </Show>
                          </div>
                        </th>
                      )}
                    </For>
                  </tr>
                )}
              </For>
            </thead>
            <tbody class="divide-y divide-border-muted">
              <For each={table.getRowModel().rows}>
                {(row) => (
                  <tr
                    class="transition-colors hover:bg-surface-overlay"
                    classList={{
                      "cursor-pointer": !!props.onRowClick,
                    }}
                    onClick={() => props.onRowClick?.(row.original)}
                    onKeyDown={(e) => {
                      if (
                        (e.key === "Enter" || e.key === " ") &&
                        props.onRowClick
                      ) {
                        e.preventDefault();
                        props.onRowClick(row.original);
                      }
                    }}
                    tabIndex={props.onRowClick ? 0 : undefined}
                    role={props.onRowClick ? "button" : undefined}
                  >
                    <For each={row.getVisibleCells()}>
                      {(cell) => (
                        <td
                          class="px-3 py-2 text-text-primary"
                          classList={{
                            "font-mono":
                              !!(cell.column.columnDef.meta as Record<string, unknown>)
                                ?.mono,
                          }}
                        >
                          {flexRender(
                            cell.column.columnDef.cell,
                            cell.getContext(),
                          )}
                        </td>
                      )}
                    </For>
                  </tr>
                )}
              </For>
            </tbody>
          </table>
        </div>
      </Show>
    </Show>
  );
}
