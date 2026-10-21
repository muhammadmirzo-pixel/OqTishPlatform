// Thin fetch wrapper. Every non-2xx response becomes an ApiError with an explicit `kind` and the
// server's machine-readable `code`, so screens can render the right state and a localised message
// without parsing HTTP details. The server stays authoritative for all rules.
//
// CSRF (CONVENTIONS.md §5, OD-23): every non-GET request carries `X-OqTish-Client` ("web", or
// "tg-miniapp" inside the Telegram Mini App). Cookies are sent same-origin only.

export type ApiErrorKind =
  | 'validation'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'conflict'
  | 'rateLimited'
  | 'server'
  | 'network'

export type ClientKind = 'web' | 'tg-miniapp'

/** RFC 7807 body as produced by the API (ProblemDetails + code + traceId + optional field errors). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number | undefined
  readonly problem: ProblemDetails | undefined

  constructor(kind: ApiErrorKind, status: number | undefined, problem: ProblemDetails | undefined) {
    super(problem?.title ?? kind)
    this.name = 'ApiError'
    this.kind = kind
    this.status = status
    this.problem = problem
  }

  /** Stable error code from docs/API.md §9 (e.g. "booking.slot_unavailable"). */
  get code(): string | undefined {
    return this.problem?.code
  }

  /** Correlation id to show users on server errors, so support can find the log entry. */
  get traceId(): string | undefined {
    return this.problem?.traceId
  }

  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {}
  }
}

export function isApiError(value: unknown, code?: string): value is ApiError {
  return value instanceof ApiError && (code === undefined || value.code === code)
}

export function kindForStatus(status: number): ApiErrorKind {
  if (status === 400 || status === 422) return 'validation'
  if (status === 401) return 'unauthorized'
  if (status === 403) return 'forbidden'
  if (status === 404) return 'notFound'
  if (status === 409) return 'conflict'
  if (status === 429) return 'rateLimited'
  return 'server'
}

export interface RequestOptions {
  headers?: Record<string, string>
  /** Sent as `Idempotency-Key`; callers keep it stable across retries of one submission. */
  idempotencyKey?: string
  signal?: AbortSignal
}

export type Method = 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export interface ApiClient {
  get<T>(path: string, options?: RequestOptions): Promise<T>
  send<T>(method: Method, path: string, body?: unknown, options?: RequestOptions): Promise<T>
  /** Absolute URL of an image served by GET /files/images/{id} (clinic logos, avatars). */
  imageUrl(id: string): string
  readonly clientKind: ClientKind
  setClientKind(kind: ClientKind): void
  /** Called when any request answers 401 `auth.unauthenticated` (session expired or revoked). */
  onUnauthenticated(listener: () => void): () => void
}

export function createApiClient(baseUrl: string, fetchImpl: typeof fetch = fetch, options: { clientKind?: ClientKind } = {}): ApiClient {
  const base = baseUrl.replace(/\/+$/, '')
  let clientKind: ClientKind = options.clientKind ?? 'web'
  const listeners = new Set<() => void>()

  async function request<T>(path: string, method: 'GET' | Method, body: unknown, opts: RequestOptions | undefined): Promise<T> {
    const headers: Record<string, string> = { Accept: 'application/json' }
    if (method !== 'GET') {
      headers['X-OqTish-Client'] = clientKind
      // FormData (file uploads) sets its own multipart boundary.
      if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json'
      if (opts?.idempotencyKey) headers['Idempotency-Key'] = opts.idempotencyKey
    }
    Object.assign(headers, opts?.headers)

    let response: Response
    try {
      response = await fetchImpl(`${base}${path}`, {
        method,
        credentials: 'same-origin',
        headers,
        body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body),
        signal: opts?.signal,
      })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') throw error
      throw new ApiError('network', undefined, undefined)
    }

    if (!response.ok) {
      const problem = await readProblem(response)
      const error = new ApiError(kindForStatus(response.status), response.status, problem)
      if (response.status === 401 && error.code === 'auth.unauthenticated') listeners.forEach((l) => l())
      throw error
    }
    if (response.status === 204 || response.status === 202 || response.headers.get('Content-Length') === '0') {
      return (await readOptionalJson(response)) as T
    }
    return (await response.json()) as T
  }

  return {
    get: (path, opts) => request(path, 'GET', undefined, opts),
    send: (method, path, body, opts) => request(path, method, body, opts),
    imageUrl: (id) => `${base}/files/images/${encodeURIComponent(id)}`,
    get clientKind() {
      return clientKind
    },
    setClientKind(kind) {
      clientKind = kind
    },
    onUnauthenticated(listener) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
  }
}

async function readOptionalJson(response: Response): Promise<unknown> {
  const text = await response.text().catch(() => '')
  if (!text) return undefined
  try {
    return JSON.parse(text)
  } catch {
    return undefined
  }
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  const type = response.headers.get('Content-Type') ?? ''
  if (!type.includes('json')) return undefined
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

/** Builds "?a=1&b=x", skipping undefined, null and empty values. */
/** Image upload body (multipart field `file`). */
export function imageForm(file: File): FormData {
  const form = new FormData()
  form.append('file', file)
  return form
}

export function query(params: object): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params) as [string, unknown][]) {
    if (value === undefined || value === null || value === '') continue
    search.set(key, String(value))
  }
  const text = search.toString()
  return text ? `?${text}` : ''
}

/** Encodes a path segment (ids come from the URL and must not alter the request path). */
export function seg(value: string): string {
  return encodeURIComponent(value)
}
