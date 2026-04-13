import { A, useLocation } from "@solidjs/router";
import { createEffect, createSignal, For, type JSX, Show } from "solid-js";
import { openAlertCount } from "~/realtime/alerts";

// ---------------------------------------------------------------------------
// Collapse state persisted in localStorage
// ---------------------------------------------------------------------------

const STORAGE_KEY = "siem:sidebar-collapsed";

function readInitialCollapsed(): boolean {
  try {
    return localStorage.getItem(STORAGE_KEY) === "true";
  } catch {
    return false;
  }
}

// ---------------------------------------------------------------------------
// Navigation items
// ---------------------------------------------------------------------------

interface NavItem {
  label: string;
  href: string;
  icon: () => JSX.Element;
  badge?: () => number;
  end?: boolean;
}

// Inline SVG icons — lightweight, no external dependency
function DashboardIcon() {
  return (
    <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
      <rect x="3" y="3" width="7" height="7" rx="1" />
      <rect x="14" y="3" width="7" height="7" rx="1" />
      <rect x="3" y="14" width="7" height="7" rx="1" />
      <rect x="14" y="14" width="7" height="7" rx="1" />
    </svg>
  );
}

function AlertsIcon() {
  return (
    <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
      <path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" />
      <path d="M13.73 21a2 2 0 0 1-3.46 0" />
    </svg>
  );
}

function InvestigateIcon() {
  return (
    <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
      <circle cx="11" cy="11" r="8" />
      <path d="m21 21-4.35-4.35" />
    </svg>
  );
}

function RulesIcon() {
  return (
    <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10" />
    </svg>
  );
}

function AdminIcon() {
  return (
    <svg class="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" />
    </svg>
  );
}

function CollapseIcon(props: { collapsed: boolean }) {
  return (
    <svg class="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
      <Show when={props.collapsed} fallback={<polyline points="15 18 9 12 15 6" />}>
        <polyline points="9 18 15 12 9 6" />
      </Show>
    </svg>
  );
}

const NAV_ITEMS: NavItem[] = [
  { label: "Dashboard", href: "/", icon: DashboardIcon, end: true },
  { label: "Alerts", href: "/alerts", icon: AlertsIcon, badge: openAlertCount },
  { label: "Investigate", href: "/investigate", icon: InvestigateIcon },
  { label: "Rules", href: "/rules", icon: RulesIcon },
  { label: "Admin", href: "/admin", icon: AdminIcon },
];

// ---------------------------------------------------------------------------
// Sidebar component
// ---------------------------------------------------------------------------

export default function Sidebar() {
  const [collapsed, setCollapsed] = createSignal(readInitialCollapsed());
  const location = useLocation();

  createEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY, String(collapsed()));
    } catch {
      // localStorage may be unavailable
    }
  });

  const toggle = () => setCollapsed((c) => !c);

  const isActive = (href: string, end?: boolean) => {
    if (end) return location.pathname === href;
    return location.pathname.startsWith(href);
  };

  return (
    <nav
      aria-label="Main navigation"
      class={`sidebar flex h-full flex-col border-r border-border-default bg-surface-raised transition-[width] duration-200 ${
        collapsed() ? "w-16" : "w-60"
      }`}
    >
      {/* Logo / brand */}
      <div class="flex h-14 items-center gap-2 border-b border-border-default px-4">
        <span class="text-lg font-semibold text-text-primary">
          {collapsed() ? "AS" : "AgentSIEM"}
        </span>
      </div>

      {/* Navigation links */}
      <div class="flex flex-1 flex-col gap-1 p-2">
        <For each={NAV_ITEMS}>
          {(item) => (
            <A
              href={item.href}
              end={item.end}
              class="group relative flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium text-text-secondary transition-colors hover:bg-surface-overlay hover:text-text-primary"
              activeClass="!bg-surface-overlay !text-text-primary"
            >
              <span class="flex-shrink-0">{item.icon()}</span>
              <Show when={!collapsed()}>
                <span class="truncate">{item.label}</span>
              </Show>
              {/* Alert badge */}
              <Show when={item.badge && item.badge() > 0}>
                <span
                  class={`flex h-5 min-w-5 items-center justify-center rounded-full bg-severity-critical px-1 text-xs font-bold text-white ${
                    collapsed() ? "absolute -right-1 -top-1" : "ml-auto"
                  }`}
                >
                  {item.badge!() > 99 ? "99+" : item.badge!()}
                </span>
              </Show>
            </A>
          )}
        </For>
      </div>

      {/* Collapse toggle */}
      <div class="border-t border-border-default p-2">
        <button
          onClick={toggle}
          class="flex w-full items-center justify-center rounded-md p-2 text-text-muted transition-colors hover:bg-surface-overlay hover:text-text-primary"
          aria-label={collapsed() ? "Expand sidebar" : "Collapse sidebar"}
        >
          <CollapseIcon collapsed={collapsed()} />
        </button>
      </div>
    </nav>
  );
}
