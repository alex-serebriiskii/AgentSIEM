import { A, useLocation } from "@solidjs/router";

export default function NotFound() {
  const location = useLocation();

  return (
    <div class="flex h-full items-center justify-center">
      <div class="text-center">
        <h1 class="text-4xl font-bold text-text-primary">404</h1>
        <p class="mt-2 text-text-secondary">Page not found</p>
        <p class="mt-1 font-mono text-sm text-text-muted">
          {location.pathname}
        </p>
        <A
          href="/"
          class="mt-4 inline-block rounded-md bg-interactive-default px-4 py-2 text-sm font-medium text-white hover:bg-interactive-hover"
        >
          Back to Dashboard
        </A>
      </div>
    </div>
  );
}
