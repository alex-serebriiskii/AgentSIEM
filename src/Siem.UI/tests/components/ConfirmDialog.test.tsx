import { describe, it, expect, vi } from "vitest";
import { render, fireEvent, screen } from "@solidjs/testing-library";
import { createSignal } from "solid-js";
import ConfirmDialog from "~/components/ConfirmDialog";

function renderDialog(overrides: Partial<{
  title: string;
  description: string;
  confirmLabel: string;
  variant: "default" | "danger";
  onConfirm: () => void;
}> = {}) {
  const onConfirm = overrides.onConfirm ?? vi.fn();
  const [open, setOpen] = createSignal(true);

  const result = render(() => (
    <ConfirmDialog
      open={open()}
      onOpenChange={setOpen}
      title={overrides.title ?? "Delete rule?"}
      description={overrides.description ?? "This action cannot be undone."}
      confirmLabel={overrides.confirmLabel}
      variant={overrides.variant}
      onConfirm={onConfirm}
    />
  ));

  return { ...result, onConfirm, open, setOpen };
}

describe("ConfirmDialog", () => {
  it("renders title and description when open", () => {
    renderDialog();

    expect(screen.getByText("Delete rule?")).toBeInTheDocument();
    expect(screen.getByText("This action cannot be undone.")).toBeInTheDocument();
  });

  it("uses default confirm label when none provided", () => {
    renderDialog();

    expect(screen.getByText("Confirm")).toBeInTheDocument();
  });

  it("uses custom confirm label", () => {
    renderDialog({ confirmLabel: "Yes, delete" });

    expect(screen.getByText("Yes, delete")).toBeInTheDocument();
  });

  it("calls onConfirm and closes when confirm button is clicked", () => {
    const onConfirm = vi.fn();
    const { open } = renderDialog({ onConfirm });

    fireEvent.click(screen.getByText("Confirm"));

    expect(onConfirm).toHaveBeenCalledOnce();
    expect(open()).toBe(false);
  });

  it("closes when cancel button is clicked without calling onConfirm", () => {
    const onConfirm = vi.fn();
    const { open } = renderDialog({ onConfirm });

    fireEvent.click(screen.getByText("Cancel"));

    expect(onConfirm).not.toHaveBeenCalled();
    expect(open()).toBe(false);
  });

  it("does not render content when closed", () => {
    const [open] = createSignal(false);
    render(() => (
      <ConfirmDialog
        open={open()}
        onOpenChange={() => {}}
        title="Hidden"
        description="Should not appear"
        onConfirm={() => {}}
      />
    ));

    expect(screen.queryByText("Hidden")).not.toBeInTheDocument();
  });
});
