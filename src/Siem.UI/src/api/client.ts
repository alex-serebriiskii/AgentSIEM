// ---------------------------------------------------------------------------
// Fetch-based API client with RFC 7807 Problem Details error parsing
// ---------------------------------------------------------------------------

const DEFAULT_TIMEOUT_MS = 30_000;

export class ApiError extends Error {
  status: number;
  detail?: string;
  errors?: Record<string, string[]>;

  constructor(
    status: number,
    message: string,
    detail?: string,
    errors?: Record<string, string[]>,
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.detail = detail;
    this.errors = errors;
  }
}

interface RequestOptions {
  body?: unknown;
  signal?: AbortSignal;
  params?: Record<string, string | number | boolean | null | undefined>;
}

function buildQueryString(
  params: Record<string, string | number | boolean | null | undefined>,
): string {
  const searchParams = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (value != null && value !== "") {
      searchParams.set(key, String(value));
    }
  }
  const qs = searchParams.toString();
  return qs ? `?${qs}` : "";
}

async function parseError(response: Response): Promise<ApiError> {
  try {
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      const body = await response.json();
      return new ApiError(
        response.status,
        body.title ?? response.statusText,
        body.detail,
        body.errors,
      );
    }
  } catch {
    // Fall through to generic error
  }
  return new ApiError(response.status, response.statusText);
}

async function request<T>(
  method: string,
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const url = path + (options.params ? buildQueryString(options.params) : "");

  const headers: Record<string, string> = {};
  if (options.body !== undefined) {
    headers["Content-Type"] = "application/json";
  }

  const signal =
    options.signal ?? AbortSignal.timeout(DEFAULT_TIMEOUT_MS);

  const response = await fetch(url, {
    method,
    headers,
    body: options.body !== undefined ? JSON.stringify(options.body) : undefined,
    signal,
  });

  if (!response.ok) {
    throw await parseError(response);
  }

  if (response.status === 204) {
    return undefined as unknown as T;
  }

  return response.json() as Promise<T>;
}

export function get<T>(
  path: string,
  options?: Omit<RequestOptions, "body">,
): Promise<T> {
  return request<T>("GET", path, options);
}

export function post<T = void>(
  path: string,
  body?: unknown,
  options?: Omit<RequestOptions, "body">,
): Promise<T> {
  return request<T>("POST", path, { ...options, body });
}

export function put<T = void>(
  path: string,
  body?: unknown,
  options?: Omit<RequestOptions, "body">,
): Promise<T> {
  return request<T>("PUT", path, { ...options, body });
}

export function del(
  path: string,
  options?: Omit<RequestOptions, "body">,
): Promise<void> {
  return request<void>("DELETE", path, options);
}
