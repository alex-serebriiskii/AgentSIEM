import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render } from "@solidjs/testing-library";
import RelativeTimestamp from "~/components/RelativeTimestamp";

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("RelativeTimestamp", () => {
  it("shows 'just now' for timestamps < 1 minute ago", () => {
    const now = new Date();
    const { container } = render(() => <RelativeTimestamp timestamp={now} />);
    expect(container.textContent).toContain("just now");
  });

  it("shows minutes for timestamps < 1 hour ago", () => {
    const fiveMinAgo = new Date(Date.now() - 5 * 60 * 1000);
    const { container } = render(() => (
      <RelativeTimestamp timestamp={fiveMinAgo} />
    ));
    expect(container.textContent).toContain("5m ago");
  });

  it("shows hours for timestamps < 24 hours ago", () => {
    const twoHoursAgo = new Date(Date.now() - 2 * 60 * 60 * 1000);
    const { container } = render(() => (
      <RelativeTimestamp timestamp={twoHoursAgo} />
    ));
    expect(container.textContent).toContain("2h ago");
  });

  it("shows 'yesterday' for timestamps 24-48 hours ago", () => {
    const yesterday = new Date(Date.now() - 30 * 60 * 60 * 1000);
    const { container } = render(() => (
      <RelativeTimestamp timestamp={yesterday} />
    ));
    expect(container.textContent).toContain("yesterday");
  });

  it("shows days for timestamps > 48 hours ago but < 30 days", () => {
    const threeDaysAgo = new Date(Date.now() - 3 * 24 * 60 * 60 * 1000);
    const { container } = render(() => (
      <RelativeTimestamp timestamp={threeDaysAgo} />
    ));
    expect(container.textContent).toContain("3d ago");
  });

  it("accepts ISO string input", () => {
    const fiveMinAgo = new Date(Date.now() - 5 * 60 * 1000);
    const { container } = render(() => (
      <RelativeTimestamp timestamp={fiveMinAgo.toISOString()} />
    ));
    expect(container.textContent).toContain("5m ago");
  });
});
