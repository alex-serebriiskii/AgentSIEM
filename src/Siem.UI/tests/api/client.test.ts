import { describe, it, expect, vi, beforeEach } from "vitest";
import { get, post, put, del, ApiError } from "~/api/client";

beforeEach(() => {
  vi.restoreAllMocks();
});

function mockFetch(overrides: Partial<Response> = {}) {
  const mock = vi.fn().mockResolvedValue({
    ok: true,
    status: 200,
    headers: new Headers(),
    json: () => Promise.resolve({ id: 1 }),
    ...overrides,
  });
  vi.stubGlobal("fetch", mock);
  return mock;
}

describe("API client", () => {
  // -------------------------------------------------------------------------
  // GET
  // -------------------------------------------------------------------------

  it("parses problem details response on 400", async () => {
    const problemDetails = {
      title: "Validation failed",
      status: 400,
      detail: "Name is required",
      errors: { name: ["Name is required"] },
    };

    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 400,
        statusText: "Bad Request",
        headers: new Headers({ "content-type": "application/problem+json" }),
        json: () => Promise.resolve(problemDetails),
      }),
    );

    const err = await get("/api/test").catch((e) => e as ApiError);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(400);
    expect(err.message).toBe("Validation failed");
    expect(err.detail).toBe("Name is required");
    expect(err.errors).toEqual({ name: ["Name is required"] });
  });

  it("attaches AbortSignal to fetch call", async () => {
    const fetchMock = mockFetch();

    const controller = new AbortController();
    await get("/api/test", { signal: controller.signal });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/test",
      expect.objectContaining({ signal: controller.signal }),
    );
  });

  it("builds query params correctly", async () => {
    const fetchMock = mockFetch({ json: () => Promise.resolve([]) });

    await get("/api/alerts", {
      params: { page: 1, severity: "high", agent_id: null, status: undefined },
    });

    const calledUrl = fetchMock.mock.calls[0][0] as string;
    expect(calledUrl).toContain("page=1");
    expect(calledUrl).toContain("severity=high");
    expect(calledUrl).not.toContain("agent_id");
    expect(calledUrl).not.toContain("status");
  });

  // -------------------------------------------------------------------------
  // POST
  // -------------------------------------------------------------------------

  it("sends POST with JSON body", async () => {
    const fetchMock = mockFetch();
    const body = { name: "Test Rule", description: "desc" };

    await post("/api/rules", body);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/rules",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify(body),
        headers: expect.objectContaining({
          "Content-Type": "application/json",
        }),
      }),
    );
  });

  it("sends POST without body", async () => {
    const fetchMock = mockFetch();

    await post("/api/engine/recompile");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/engine/recompile",
      expect.objectContaining({
        method: "POST",
        body: undefined,
      }),
    );
  });

  // -------------------------------------------------------------------------
  // PUT
  // -------------------------------------------------------------------------

  it("sends PUT with JSON body", async () => {
    const fetchMock = mockFetch();
    const body = { resolutionNote: "resolved" };

    await put("/api/alerts/abc/resolve", body);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/alerts/abc/resolve",
      expect.objectContaining({
        method: "PUT",
        body: JSON.stringify(body),
        headers: expect.objectContaining({
          "Content-Type": "application/json",
        }),
      }),
    );
  });

  it("sends PUT without body", async () => {
    const fetchMock = mockFetch();

    await put("/api/alerts/abc/acknowledge");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/alerts/abc/acknowledge",
      expect.objectContaining({
        method: "PUT",
        body: undefined,
      }),
    );
  });

  // -------------------------------------------------------------------------
  // DELETE
  // -------------------------------------------------------------------------

  it("sends DELETE request", async () => {
    const fetchMock = mockFetch({ status: 204, json: undefined as unknown as () => Promise<unknown> });

    await del("/api/rules/abc");

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/rules/abc",
      expect.objectContaining({ method: "DELETE" }),
    );
  });

  // -------------------------------------------------------------------------
  // 204 No Content
  // -------------------------------------------------------------------------

  it("returns undefined for 204 No Content", async () => {
    mockFetch({ status: 204 });

    const result = await post("/api/something");
    expect(result).toBeUndefined();
  });

  // -------------------------------------------------------------------------
  // Default timeout
  // -------------------------------------------------------------------------

  it("uses default AbortSignal.timeout when no signal provided", async () => {
    const fetchMock = mockFetch();

    await get("/api/test");

    const callOptions = fetchMock.mock.calls[0][1] as RequestInit;
    expect(callOptions.signal).toBeDefined();
  });

  it("uses caller-provided signal instead of default timeout", async () => {
    const fetchMock = mockFetch();
    const controller = new AbortController();

    await get("/api/test", { signal: controller.signal });

    const callOptions = fetchMock.mock.calls[0][1] as RequestInit;
    expect(callOptions.signal).toBe(controller.signal);
  });

  // -------------------------------------------------------------------------
  // Error fallback (non-JSON error body)
  // -------------------------------------------------------------------------

  it("falls back to statusText when error body is not JSON", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        status: 500,
        statusText: "Internal Server Error",
        headers: new Headers({ "content-type": "text/plain" }),
      }),
    );

    const err = await get("/api/broken").catch((e) => e as ApiError);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(500);
    expect(err.message).toBe("Internal Server Error");
  });
});
