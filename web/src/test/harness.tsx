import { render } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router'
import { vi } from 'vitest'
import { createApi } from '../api'
import { createApiClient } from '../api/client'
import type { Me } from '../api/types'
import { AppProviders } from '../app/AppProviders'
import { routes } from '../app/routes'
import type { Locale } from '../i18n/messages'

export interface Call {
  method: string
  path: string
  search: URLSearchParams
  headers: Record<string, string>
  body: unknown
}

type Reply = Response | { status?: number; body?: unknown } | (() => Response | Promise<Response>)
export type Handler = [method: string, path: string | RegExp, reply: Reply | ((call: Call) => Reply)]

export function json(body: unknown, status = 200): Response {
  return new Response(body === undefined ? null : JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

export function problem(status: number, code: string): Response {
  return new Response(JSON.stringify({ title: code, status, code, traceId: 'trace-1' }), { status, headers: { 'Content-Type': 'application/problem+json' } })
}

/**
 * fetch mock matching "METHOD /path" (query ignored for matching, available in the call). Later
 * handlers win, so tests can override defaults. Unmatched requests answer 404 common.not_found.
 */
export function mockFetch(handlers: Handler[]) {
  const calls: Call[] = []
  const fn = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(input), 'http://localhost')
    const path = url.pathname.replace(/^\/api/, '')
    const method = init?.method ?? 'GET'
    const headers = { ...(init?.headers as Record<string, string>) }
    const call: Call = { method, path, search: url.searchParams, headers, body: init?.body ? JSON.parse(String(init.body)) : undefined }
    calls.push(call)
    for (let i = handlers.length - 1; i >= 0; i--) {
      const [m, p, reply] = handlers[i]!
      if (m !== method) continue
      if (typeof p === 'string' ? p !== path : !p.test(path)) continue
      let r: unknown = typeof reply === 'function' ? (reply as (c: Call) => Reply)(call) : reply
      if (typeof r === 'function') r = await (r as () => Response | Promise<Response>)()
      if (r instanceof Response) return r
      const { status = 200, body } = r as { status?: number; body?: unknown }
      return status === 204 ? new Response(null, { status }) : json(body, status)
    }
    return problem(404, 'common.not_found')
  })
  return Object.assign(fn, {
    calls,
    find: (method: string, path: string | RegExp) => calls.filter((c) => c.method === method && (typeof path === 'string' ? c.path === path : path.test(c.path))),
  })
}

export type FetchMock = ReturnType<typeof mockFetch>

export function meHandler(me: Me | null): Handler {
  return ['GET', '/me', me ? { body: me } : () => problem(401, 'auth.unauthenticated')]
}

export function renderApp(path: string, fetchMock: FetchMock, options: { locale?: Locale | null } = {}) {
  const client = createApiClient('/api', fetchMock as unknown as typeof fetch, { clientKind: path.startsWith('/tg') ? 'tg-miniapp' : 'web' })
  const router = createMemoryRouter(routes, { initialEntries: [path] })
  const utils = render(
    <AppProviders api={createApi(client)} locale={options.locale === null ? undefined : (options.locale ?? 'en')}>
      <RouterProvider router={router} />
    </AppProviders>,
  )
  return { ...utils, router }
}
