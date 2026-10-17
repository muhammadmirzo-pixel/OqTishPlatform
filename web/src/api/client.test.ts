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
