export interface PaginationProps {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

export default function Pagination(props: PaginationProps) {
  const isFirstPage = () => props.page <= 1;
  const isLastPage = () => props.page >= props.totalPages;

  return (
    <nav
      class="flex items-center gap-2"
      aria-label="Pagination"
    >
      <button
        onClick={() => props.onPageChange(props.page - 1)}
        disabled={isFirstPage()}
        class="rounded-md border border-border-default bg-surface-raised px-3 py-1.5 text-sm text-text-secondary transition-colors hover:bg-surface-overlay disabled:cursor-not-allowed disabled:opacity-40"
        aria-label="Previous page"
      >
        Previous
      </button>

      <span class="px-2 text-sm text-text-muted">
        Page {props.page} of {props.totalPages}
      </span>

      <button
        onClick={() => props.onPageChange(props.page + 1)}
        disabled={isLastPage()}
        class="rounded-md border border-border-default bg-surface-raised px-3 py-1.5 text-sm text-text-secondary transition-colors hover:bg-surface-overlay disabled:cursor-not-allowed disabled:opacity-40"
        aria-label="Next page"
      >
        Next
      </button>
    </nav>
  );
}
