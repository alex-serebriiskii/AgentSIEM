import { describe, it, expect, beforeEach } from "vitest";
import { render, waitFor } from "@solidjs/testing-library";
import { Route, Router } from "@solidjs/router";
import FilterToolbar, { useFilterContext } from "~/components/FilterToolbar";

function FilterDisplay(props: { filterKey: string }) {
  const ctx = useFilterContext();
  return <span data-testid="value">{ctx.getFilter(props.filterKey) ?? "none"}</span>;
}

function FilterSetter(props: { filterKey: string; value: string }) {
  const ctx = useFilterContext();
  return (
    <button data-testid="set" onClick={() => ctx.setFilter(props.filterKey, props.value)}>
      Set
    </button>
  );
}

function FilterClearer(props: { keys: string[] }) {
  const ctx = useFilterContext();
  return (
    <button data-testid="clear" onClick={() => ctx.clearFilters(props.keys)}>
      Clear
    </button>
  );
}

function renderInRoute(ui: () => import("solid-js").JSX.Element, initialUrl = "/") {
  window.history.pushState({}, "", initialUrl);
  return render(() => (
    <Router>
      <Route path="*" component={() => ui()} />
    </Router>
  ));
}

beforeEach(() => {
  window.history.pushState({}, "", "/");
});

describe("FilterToolbar", () => {
  it("reads initial filter value from URL search params", async () => {
    const { getByTestId } = renderInRoute(
      () => (
        <FilterToolbar>
          <FilterDisplay filterKey="severity" />
        </FilterToolbar>
      ),
      "/?severity=high",
    );

    await waitFor(() => {
      expect(getByTestId("value").textContent).toBe("high");
    });
  });

  it("updates URL params on setFilter", async () => {
    const { getByTestId } = renderInRoute(() => (
      <FilterToolbar>
        <FilterDisplay filterKey="severity" />
        <FilterSetter filterKey="severity" value="critical" />
      </FilterToolbar>
    ));

    await waitFor(() => {
      expect(getByTestId("value").textContent).toBe("none");
    });

    getByTestId("set").click();
    await waitFor(() => {
      expect(window.location.search).toContain("severity=critical");
    });
  });

  it("clears filters", async () => {
    const { getByTestId } = renderInRoute(
      () => (
        <FilterToolbar>
          <FilterDisplay filterKey="severity" />
          <FilterClearer keys={["severity", "status"]} />
        </FilterToolbar>
      ),
      "/?severity=high&status=open",
    );

    await waitFor(() => {
      expect(getByTestId("value").textContent).toBe("high");
    });

    getByTestId("clear").click();
    await waitFor(() => {
      expect(window.location.search).not.toContain("severity");
      expect(window.location.search).not.toContain("status");
    });
  });

  it("returns undefined for missing filter keys", async () => {
    const { getByTestId } = renderInRoute(() => (
      <FilterToolbar>
        <FilterDisplay filterKey="nonexistent" />
      </FilterToolbar>
    ));

    await waitFor(() => {
      expect(getByTestId("value").textContent).toBe("none");
    });
  });
});
