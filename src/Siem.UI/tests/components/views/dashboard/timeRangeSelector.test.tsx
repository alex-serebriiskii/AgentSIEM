import { describe, it, expect, vi } from "vitest";
import { render, fireEvent } from "@solidjs/testing-library";
import TimeRangeSelector from "~/views/dashboard/TimeRangeSelector";

describe("TimeRangeSelector", () => {
  it("renders all 4 preset buttons", () => {
    const { getByText } = render(() => (
      <TimeRangeSelector hours={24} onChange={() => {}} />
    ));
    expect(getByText("1h")).toBeInTheDocument();
    expect(getByText("6h")).toBeInTheDocument();
    expect(getByText("24h")).toBeInTheDocument();
    expect(getByText("7d")).toBeInTheDocument();
  });

  it("marks the active preset with aria-checked", () => {
    const { getByText } = render(() => (
      <TimeRangeSelector hours={6} onChange={() => {}} />
    ));
    expect(getByText("6h").getAttribute("aria-checked")).toBe("true");
    expect(getByText("24h").getAttribute("aria-checked")).toBe("false");
  });

  it("calls onChange with preset value on click", () => {
    const onChange = vi.fn();
    const { getByText } = render(() => (
      <TimeRangeSelector hours={24} onChange={onChange} />
    ));
    getByText("1h").click();
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it("reflects 7d selection correctly", () => {
    const { getByText } = render(() => (
      <TimeRangeSelector hours={168} onChange={() => {}} />
    ));
    expect(getByText("7d").getAttribute("aria-checked")).toBe("true");
    expect(getByText("1h").getAttribute("aria-checked")).toBe("false");
  });

  it("sets tabindex=0 on active radio and -1 on others", () => {
    const { getByText } = render(() => (
      <TimeRangeSelector hours={6} onChange={() => {}} />
    ));
    expect(getByText("6h").getAttribute("tabindex")).toBe("0");
    expect(getByText("1h").getAttribute("tabindex")).toBe("-1");
    expect(getByText("24h").getAttribute("tabindex")).toBe("-1");
    expect(getByText("7d").getAttribute("tabindex")).toBe("-1");
  });

  it("moves selection right on ArrowRight", () => {
    const onChange = vi.fn();
    const { getByText } = render(() => (
      <TimeRangeSelector hours={6} onChange={onChange} />
    ));
    fireEvent.keyDown(getByText("6h"), { key: "ArrowRight" });
    expect(onChange).toHaveBeenCalledWith(24);
  });

  it("moves selection left on ArrowLeft", () => {
    const onChange = vi.fn();
    const { getByText } = render(() => (
      <TimeRangeSelector hours={6} onChange={onChange} />
    ));
    fireEvent.keyDown(getByText("6h"), { key: "ArrowLeft" });
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it("wraps from last to first on ArrowRight", () => {
    const onChange = vi.fn();
    const { getByText } = render(() => (
      <TimeRangeSelector hours={168} onChange={onChange} />
    ));
    fireEvent.keyDown(getByText("7d"), { key: "ArrowRight" });
    expect(onChange).toHaveBeenCalledWith(1);
  });

  it("wraps from first to last on ArrowLeft", () => {
    const onChange = vi.fn();
    const { getByText } = render(() => (
      <TimeRangeSelector hours={1} onChange={onChange} />
    ));
    fireEvent.keyDown(getByText("1h"), { key: "ArrowLeft" });
    expect(onChange).toHaveBeenCalledWith(168);
  });
});
