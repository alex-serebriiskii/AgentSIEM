import { For } from "solid-js";

export interface TimeRangeSelectorProps {
  hours: number;
  onChange: (hours: number) => void;
}

const presets = [
  { label: "1h", value: 1 },
  { label: "6h", value: 6 },
  { label: "24h", value: 24 },
  { label: "7d", value: 168 },
] as const;

export default function TimeRangeSelector(props: TimeRangeSelectorProps) {
  const handleKeyDown = (e: KeyboardEvent) => {
    const idx = presets.findIndex((p) => p.value === props.hours);
    let next = -1;
    if (e.key === "ArrowRight" || e.key === "ArrowDown") {
      next = (idx + 1) % presets.length;
    } else if (e.key === "ArrowLeft" || e.key === "ArrowUp") {
      next = (idx - 1 + presets.length) % presets.length;
    }
    if (next >= 0) {
      e.preventDefault();
      props.onChange(presets[next].value);
      const group = (e.currentTarget as HTMLElement).closest("[role='radiogroup']");
      const buttons = group?.querySelectorAll<HTMLElement>("[role='radio']");
      buttons?.[next]?.focus();
    }
  };

  return (
    <div
      role="radiogroup"
      aria-label="Time range"
      class="flex gap-1 rounded-lg border border-border-default bg-surface-base p-1"
    >
      <For each={presets}>
        {(preset) => (
          <button
            role="radio"
            aria-checked={props.hours === preset.value}
            tabIndex={props.hours === preset.value ? 0 : -1}
            class="rounded-md px-3 py-1 text-sm font-medium transition-colors"
            classList={{
              "bg-surface-overlay text-text-primary": props.hours === preset.value,
              "text-text-muted hover:text-text-secondary hover:bg-surface-overlay/50":
                props.hours !== preset.value,
            }}
            onClick={() => props.onChange(preset.value)}
            onKeyDown={handleKeyDown}
          >
            {preset.label}
          </button>
        )}
      </For>
    </div>
  );
}
