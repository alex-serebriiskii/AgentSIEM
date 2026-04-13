import { useSearchParams } from "@solidjs/router";
import { createContext, type JSX, useContext } from "solid-js";

// ---------------------------------------------------------------------------
// Filter context — provides URL ↔ filter sync to children
// ---------------------------------------------------------------------------

interface FilterContextValue {
  getFilter: (key: string) => string | undefined;
  setFilter: (key: string, value: string | null) => void;
  clearFilters: (keys: string[]) => void;
}

const FilterContext = createContext<FilterContextValue>();

export function useFilterContext(): FilterContextValue {
  const ctx = useContext(FilterContext);
  if (!ctx)
    throw new Error("useFilterContext must be used within a FilterToolbar");
  return ctx;
}

// ---------------------------------------------------------------------------
// FilterToolbar component
// ---------------------------------------------------------------------------

export interface FilterToolbarProps {
  children: JSX.Element;
}

export default function FilterToolbar(props: FilterToolbarProps) {
  const [searchParams, setSearchParams] = useSearchParams();

  const ctx: FilterContextValue = {
    getFilter(key: string) {
      const val = searchParams[key];
      return typeof val === "string" ? val : undefined;
    },

    setFilter(key: string, value: string | null) {
      setSearchParams({ [key]: value ?? undefined }, { replace: true });
    },

    clearFilters(keys: string[]) {
      const reset: Record<string, undefined> = {};
      for (const key of keys) {
        reset[key] = undefined;
      }
      setSearchParams(reset, { replace: true });
    },
  };

  return (
    <FilterContext.Provider value={ctx}>
      <div class="flex flex-wrap items-center gap-3">{props.children}</div>
    </FilterContext.Provider>
  );
}
