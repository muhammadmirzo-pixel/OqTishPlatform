// Thin fetch wrapper. Every non-2xx response becomes an ApiError with an explicit `kind`, so screens
// can render the required states (validation, permission denied, not found, conflict, rate limit,
// server error) without parsing HTTP details themselves. The server stays authoritative for all rules.

export type ApiErrorKind =
  | 'validation'
  | 'unauthorized'
  | 'forbidden'
  | 'notFound'
  | 'conflict'
  | 'rateLimited'
  | 'server'
  | 'network'

/** RFC 7807 body as produced by the API (ProblemDetails + traceId + optional field errors). */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
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

  /** Correlation id to show users on server errors, so support can find the log entry. */
  get traceId(): string | undefined {
    return this.problem?.traceId
  }
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

export interface ApiClient {
  get<T>(path: string, init?: RequestInit): Promise<T>
  send<T>(method: 'POST' | 'PUT' | 'PATCH' | 'DELETE', path: string, body?: unknown, init?: RequestInit): Promise<T>
}

export function createApiClient(baseUrl: string, fetchImpl: typeof fetch = fetch): ApiClient {
  const base = baseUrl.replace(/\/+$/, '')

  async function request<T>(path: string, init: RequestInit): Promise<T> {
    let response: Response
    try {
      response = await fetchImpl(`${base}${path}`, {
        credentials: 'same-origin',
        ...init,
        headers: { Accept: 'application/json', ...init.headers },
      })
    } catch {
      throw new ApiError('network', undefined, undefined)
    }

    if (!response.ok) {
      throw new ApiError(kindForStatus(response.status), response.status, await readProblem(response))
    }
    if (response.status === 204) {
      return undefined as T
    }
    return (await response.json()) as T
  }

  return {
    get: (path, init) => request(path, { ...init, method: 'GET' }),
    send: (method, path, body, init) =>
      request(path, {
        ...init,
        method,
        headers: { 'Content-Type': 'application/json', ...init?.headers },
        body: body === undefined ? undefined : JSON.stringify(body),
      }),
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
