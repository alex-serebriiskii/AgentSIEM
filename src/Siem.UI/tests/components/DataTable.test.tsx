import { describe, it, expect, vi } from "vitest";
import { render, fireEvent, screen } from "@solidjs/testing-library";
import type { ColumnDef } from "@tanstack/solid-table";
import DataTable from "~/components/DataTable";

interface TestRow {
  id: number;
  name: string;
  value: number;
}

const columns: ColumnDef<TestRow, unknown>[] = [
  { accessorKey: "id", header: "ID" },
  { accessorKey: "name", header: "Name" },
  { accessorKey: "value", header: "Value" },
];

const testData: TestRow[] = [
  { id: 1, name: "Alpha", value: 30 },
  { id: 2, name: "Beta", value: 10 },
  { id: 3, name: "Gamma", value: 20 },
];

describe("DataTable", () => {
  it("renders column headers", () => {
    render(() => <DataTable columns={columns} data={testData} />);

    expect(screen.getByText("ID")).toBeInTheDocument();
    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("Value")).toBeInTheDocument();
  });

  it("renders data rows", () => {
    render(() => <DataTable columns={columns} data={testData} />);

    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("Beta")).toBeInTheDocument();
    expect(screen.getByText("Gamma")).toBeInTheDocument();
  });

  it("shows empty state when data is empty", () => {
    render(() => (
      <DataTable
        columns={columns}
        data={[]}
        emptyTitle="Nothing here"
        emptyDescription="Try a different filter"
      />
    ));

    expect(screen.getByText("Nothing here")).toBeInTheDocument();
    expect(screen.getByText("Try a different filter")).toBeInTheDocument();
  });

  it("shows default empty state text", () => {
    render(() => <DataTable columns={columns} data={[]} />);

    expect(screen.getByText("No data")).toBeInTheDocument();
  });

  it("shows loading skeleton when loading", () => {
    const { container } = render(() => (
      <DataTable columns={columns} data={[]} loading={true} />
    ));

    // Skeleton renders pulse divs, not a table
    const pulseElements = container.querySelectorAll(".animate-pulse");
    expect(pulseElements.length).toBeGreaterThan(0);

    // Data should not be rendered
    expect(screen.queryByText("Alpha")).not.toBeInTheDocument();
  });

  it("calls onRowClick when a row is clicked", () => {
    const onClick = vi.fn();
    render(() => (
      <DataTable columns={columns} data={testData} onRowClick={onClick} />
    ));

    fireEvent.click(screen.getByText("Alpha"));
    expect(onClick).toHaveBeenCalledWith(testData[0]);
  });

  it("calls onRowClick on Enter key", () => {
    const onClick = vi.fn();
    render(() => (
      <DataTable columns={columns} data={testData} onRowClick={onClick} />
    ));

    const row = screen.getByText("Alpha").closest("tr")!;
    fireEvent.keyDown(row, { key: "Enter" });
    expect(onClick).toHaveBeenCalledWith(testData[0]);
  });

  it("rows have role=button and tabIndex when onRowClick is set", () => {
    render(() => (
      <DataTable columns={columns} data={testData} onRowClick={() => {}} />
    ));

    const row = screen.getByText("Alpha").closest("tr")!;
    expect(row.getAttribute("role")).toBe("button");
    expect(row.getAttribute("tabindex")).toBe("0");
  });

  it("rows do not have role=button when onRowClick is not set", () => {
    render(() => <DataTable columns={columns} data={testData} />);

    const row = screen.getByText("Alpha").closest("tr")!;
    expect(row.getAttribute("role")).toBeNull();
  });

  it("sorts columns when header is clicked", async () => {
    render(() => <DataTable columns={columns} data={testData} />);

    const nameHeader = screen.getByText("Name");
    fireEvent.click(nameHeader);

    // After ascending sort, first cell in the name column should be Alpha
    const cells = screen.getAllByRole("cell");
    const nameColumnCells = cells.filter((_, i) => i % 3 === 1);
    expect(nameColumnCells[0].textContent).toBe("Alpha");
    expect(nameColumnCells[1].textContent).toBe("Beta");
    expect(nameColumnCells[2].textContent).toBe("Gamma");
  });
});
