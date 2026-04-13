import { describe, it, expect } from "vitest";
import { render } from "@solidjs/testing-library";
import Sparkline from "~/components/Sparkline";

describe("Sparkline", () => {
  it("renders an SVG with correct dimensions", () => {
    const { container } = render(() => (
      <Sparkline data={[1, 3, 2, 5, 4]} width={100} height={24} />
    ));
    const svg = container.querySelector("svg");
    expect(svg).toBeTruthy();
    expect(svg!.getAttribute("width")).toBe("100");
    expect(svg!.getAttribute("height")).toBe("24");
  });

  it("renders polyline with correct number of points", () => {
    const data = [1, 3, 2, 5, 4];
    const { container } = render(() => <Sparkline data={data} />);
    const polyline = container.querySelector("polyline");
    expect(polyline).toBeTruthy();
    const points = polyline!.getAttribute("points")!;
    // Each data point becomes one "x,y" pair separated by spaces
    const pairs = points.trim().split(" ");
    expect(pairs.length).toBe(data.length);
  });

  it("handles empty data array", () => {
    const { container } = render(() => <Sparkline data={[]} />);
    const svg = container.querySelector("svg");
    expect(svg).toBeTruthy();
    // No polyline or empty points
    const polyline = container.querySelector("polyline");
    expect(polyline).toBeNull();
  });
});
