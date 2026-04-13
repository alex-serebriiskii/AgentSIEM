import { createMemo } from "solid-js";

export interface SparklineProps {
  data: number[];
  width?: number;
  height?: number;
  color?: string;
}

export default function Sparkline(props: SparklineProps) {
  const width = () => props.width ?? 120;
  const height = () => props.height ?? 32;
  const color = () => props.color ?? "var(--color-text-muted)";

  const points = createMemo(() => {
    const d = props.data;
    if (d.length === 0) return "";

    const min = Math.min(...d);
    const max = Math.max(...d);
    const range = max - min || 1;
    const w = width();
    const h = height();
    const padding = 2;

    return d
      .map((v, i) => {
        const x = d.length === 1 ? w / 2 : (i / (d.length - 1)) * w;
        const y = h - padding - ((v - min) / range) * (h - padding * 2);
        return `${x},${y}`;
      })
      .join(" ");
  });

  return (
    <svg
      width={width()}
      height={height()}
      viewBox={`0 0 ${width()} ${height()}`}
      class="inline-block"
    >
      {points() && (
        <polyline
          points={points()}
          fill="none"
          stroke={color()}
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      )}
    </svg>
  );
}
