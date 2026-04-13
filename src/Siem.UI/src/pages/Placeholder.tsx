import { useLocation } from "@solidjs/router";

export default function Placeholder() {
  const location = useLocation();

  return (
    <div class="flex h-full items-center justify-center">
      <div class="text-center">
        <h1 class="text-2xl font-light text-text-primary">
          {routeLabel(location.pathname)}
        </h1>
        <p class="mt-2 font-mono text-sm text-text-muted">
          {location.pathname}
        </p>
      </div>
    </div>
  );
}

function routeLabel(path: string): string {
  if (path === "/") return "Dashboard";
  const segment = path.split("/").filter(Boolean)[0] ?? "";
  return segment.charAt(0).toUpperCase() + segment.slice(1);
}
