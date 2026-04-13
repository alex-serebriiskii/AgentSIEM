import { describe, it, expect, vi } from "vitest";
import { render, fireEvent, screen } from "@solidjs/testing-library";
import Pagination from "~/components/Pagination";

describe("Pagination", () => {
  it("displays current page and total pages", () => {
    render(() => (
      <Pagination page={3} totalPages={10} onPageChange={() => {}} />
    ));

    expect(screen.getByText("Page 3 of 10")).toBeInTheDocument();
  });

  it("calls onPageChange with previous page", () => {
    const onChange = vi.fn();
    render(() => (
      <Pagination page={5} totalPages={10} onPageChange={onChange} />
    ));

    fireEvent.click(screen.getByLabelText("Previous page"));
    expect(onChange).toHaveBeenCalledWith(4);
  });

  it("calls onPageChange with next page", () => {
    const onChange = vi.fn();
    render(() => (
      <Pagination page={5} totalPages={10} onPageChange={onChange} />
    ));

    fireEvent.click(screen.getByLabelText("Next page"));
    expect(onChange).toHaveBeenCalledWith(6);
  });

  it("disables Previous button on first page", () => {
    render(() => (
      <Pagination page={1} totalPages={5} onPageChange={() => {}} />
    ));

    expect(screen.getByLabelText("Previous page")).toBeDisabled();
  });

  it("disables Next button on last page", () => {
    render(() => (
      <Pagination page={5} totalPages={5} onPageChange={() => {}} />
    ));

    expect(screen.getByLabelText("Next page")).toBeDisabled();
  });

  it("enables both buttons on a middle page", () => {
    render(() => (
      <Pagination page={3} totalPages={5} onPageChange={() => {}} />
    ));

    expect(screen.getByLabelText("Previous page")).not.toBeDisabled();
    expect(screen.getByLabelText("Next page")).not.toBeDisabled();
  });

  it("has aria-label for accessibility", () => {
    render(() => (
      <Pagination page={1} totalPages={1} onPageChange={() => {}} />
    ));

    expect(screen.getByLabelText("Pagination")).toBeInTheDocument();
  });
});
