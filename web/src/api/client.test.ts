import { describe, expect, it, vi } from 'vitest'
import { ApiError, createApiClient, kindForStatus } from './client'

function jsonResponse(status: number, body: unknown, contentType = 'application/problem+json') {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': contentType } })
}

describe('kindForStatus', () => {
  it.each([
    [400, 'validation'],
    [422, 'validation'],
    [401, 'unauthorized'],
    [403, 'forbidden'],
    [404, 'notFound'],
    [409, 'conflict'],
    [429, 'rateLimited'],
    [500, 'server'],
    [503, 'server'],
  ])('maps %i to %s', (status, kind) => {
    expect(kindForStatus(status)).toBe(kind)
  })
})

describe('createApiClient', () => {
  it('joins base url and path without duplicate slashes', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(200, { ok: true }, 'application/json'))
    const client = createApiClient('https://example.test/api/', fetchMock)

    await client.get('/clinics')

    expect(fetchMock).toHaveBeenCalledWith('https://example.test/api/clinics', expect.objectContaining({ method: 'GET' }))
  })

  it('turns a 409 ProblemDetails into a conflict error carrying the trace id', async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(409, { title: 'Slot unavailable', status: 409, traceId: 'abc-123' }))
    const client = createApiClient('/api', fetchMock)

    const error = await client.send('POST', '/appointments', { any: 1 }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).kind).toBe('conflict')
    expect((error as ApiError).traceId).toBe('abc-123')
  })

  it('tolerates non-JSON error bodies', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response('<html>Bad gateway</html>', { status: 502, headers: { 'Content-Type': 'text/html' } }))
    const client = createApiClient('/api', fetchMock)

    const error = (await client.get('/x').catch((e: unknown) => e)) as ApiError

    expect(error.kind).toBe('server')
    expect(error.problem).toBeUndefined()
  })

  it('reports network failures distinctly', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'))
    const client = createApiClient('/api', fetchMock)

    const error = (await client.get('/x').catch((e: unknown) => e)) as ApiError

    expect(error.kind).toBe('network')
  })

  it('serialises JSON bodies and sends cookies same-origin only', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }))
    const client = createApiClient('/api', fetchMock)

    await client.send('POST', '/x', { a: 1 })

    const init = fetchMock.mock.calls[0]?.[1] as RequestInit
    expect(init.body).toBe('{"a":1}')
    expect(init.credentials).toBe('same-origin')
  })
})

describe('CSRF header, idempotency and session expiry', () => {
  const ok = () => vi.fn().mockImplementation(async () => new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }))
  const headersOf = (mock: ReturnType<typeof vi.fn>, i = 0) => (mock.mock.calls[i]![1] as RequestInit).headers as Record<string, string>

  it('sends X-OqTish-Client: web on every non-GET request but not on GET', async () => {
    const fetchMock = ok()
    const client = createApiClient('/api', fetchMock)
    await client.get('/me')
    for (const method of ['POST', 'PUT', 'DELETE'] as const) await client.send(method, '/x')
    expect(headersOf(fetchMock, 0)['X-OqTish-Client']).toBeUndefined()
    expect([1, 2, 3].map((i) => headersOf(fetchMock, i)['X-OqTish-Client'])).toEqual(['web', 'web', 'web'])
  })

  it('identifies the Telegram Mini App as tg-miniapp', async () => {
    const fetchMock = ok()
    const client = createApiClient('/api', fetchMock)
    client.setClientKind('tg-miniapp')
    await client.send('POST', '/auth/telegram/webapp', { initData: 'x' })
    expect(headersOf(fetchMock)['X-OqTish-Client']).toBe('tg-miniapp')
  })

  it('passes the Idempotency-Key header when given', async () => {
    const fetchMock = ok()
    const client = createApiClient('/api', fetchMock)
    await client.send('POST', '/appointments', { a: 1 }, { idempotencyKey: 'k-123' })
    expect(headersOf(fetchMock)['Idempotency-Key']).toBe('k-123')
    expect(headersOf(fetchMock)['Content-Type']).toBe('application/json')
  })

  it('exposes the problem code and notifies listeners on auth.unauthenticated only', async () => {
    const listener = vi.fn()
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(jsonResponse(401, { code: 'auth.invalid_credentials' }))
      .mockResolvedValueOnce(jsonResponse(401, { code: 'auth.unauthenticated' }))
    const client = createApiClient('/api', fetchMock)
    client.onUnauthenticated(listener)
    const first = (await client.send('POST', '/auth/password/login', {}).catch((e: unknown) => e)) as ApiError
    expect(first.code).toBe('auth.invalid_credentials')
    expect(listener).not.toHaveBeenCalled()
    await client.get('/me').catch(() => undefined)
    expect(listener).toHaveBeenCalledTimes(1)
  })

  it('accepts 202 with a JSON body and 204 without one', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValueOnce(new Response('{"challengeId":"c1"}', { status: 202, headers: { 'Content-Type': 'application/json' } }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
    const client = createApiClient('/api', fetchMock)
    expect(await client.send('POST', '/auth/otp/request', { phone: 'x' })).toEqual({ challengeId: 'c1' })
    expect(await client.send('POST', '/auth/logout')).toBeUndefined()
  })
})
