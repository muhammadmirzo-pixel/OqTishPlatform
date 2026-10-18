import { describe, expect, it } from 'vitest'
import { ApiError } from './client'
import { IdempotencyKeeper } from './idempotency'

function keeper() {
  let n = 0
  return new IdempotencyKeeper(() => `key-${++n}`)
}

describe('IdempotencyKeeper', () => {
  it('reuses the key for a retry of the same body and issues a new one for a different body', () => {
    const k = keeper()
    expect(k.keyFor({ a: 1 })).toBe('key-1')
    expect(k.keyFor({ a: 1 })).toBe('key-1')
    expect(k.keyFor({ a: 2 })).toBe('key-2')
  })

  it('keeps the key after an unknown outcome (network/5xx) and drops it after success or a definitive 4xx', () => {
    const k = keeper()
    k.keyFor({ a: 1 })
    k.afterError(new ApiError('network', undefined, undefined))
    expect(k.keyFor({ a: 1 })).toBe('key-1')
    k.afterError(new ApiError('server', 503, undefined))
    expect(k.keyFor({ a: 1 })).toBe('key-1')
    k.afterError(new ApiError('conflict', 409, { code: 'booking.slot_unavailable' }))
    expect(k.keyFor({ a: 1 })).toBe('key-2')
    k.reset()
    expect(k.keyFor({ a: 1 })).toBe('key-3')
  })

  it('generates UUIDs by default', () => {
    expect(new IdempotencyKeeper().keyFor({})).toMatch(/^[0-9a-f-]{36}$/)
  })
})
