import { Dialog } from "@kobalte/core/dialog";

export interface ConfirmDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel?: string;
  variant?: "default" | "danger";
  onConfirm: () => void;
}

export default function ConfirmDialog(props: ConfirmDialogProps) {
  const confirmLabel = () => props.confirmLabel ?? "Confirm";
  const isDanger = () => props.variant === "danger";

  const handleConfirm = () => {
    props.onConfirm();
    props.onOpenChange(false);
  };

  return (
    <Dialog open={props.open} onOpenChange={props.onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay class="fixed inset-0 z-50 bg-black/50" />
        <Dialog.Content class="fixed left-1/2 top-1/2 z-50 w-full max-w-md -translate-x-1/2 -translate-y-1/2 rounded-lg border border-border-default bg-surface-raised p-6 shadow-lg">
          <Dialog.Title class="text-lg font-semibold text-text-primary">
            {props.title}
          </Dialog.Title>
          <Dialog.Description class="mt-2 text-sm text-text-secondary">
            {props.description}
          </Dialog.Description>

          <div class="mt-6 flex justify-end gap-3">
            <Dialog.CloseButton class="rounded-md border border-border-default px-4 py-2 text-sm text-text-secondary transition-colors hover:bg-surface-overlay">
              Cancel
            </Dialog.CloseButton>
            <button
              onClick={handleConfirm}
              class={`rounded-md px-4 py-2 text-sm font-medium text-white transition-colors ${
                isDanger()
                  ? "bg-severity-critical hover:bg-severity-critical/80"
                  : "bg-interactive-default hover:bg-interactive-hover"
              }`}
            >
              {confirmLabel()}
            </button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog>
  );
}
