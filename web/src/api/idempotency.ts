// Idempotency keys for booking, payment and refund POSTs (CONVENTIONS.md §5). One key per logical
// submission: retrying the same body (after a network error or timeout) reuses the key, so the
// server replays the stored response instead of creating a duplicate. A changed body gets a new
// key (reusing it would be 422 common.idempotency_mismatch). After success the key is discarded.

import { useState } from 'react'
import { ApiError } from './client'

export function newKey(): string {
  return crypto.randomUUID()
}

export class IdempotencyKeeper {
  private key: string | null = null
  private fingerprint: string | null = null

  private readonly generate: () => string

  constructor(generate: () => string = newKey) {
    this.generate = generate
  }

  keyFor(body: unknown): string {
    const fingerprint = JSON.stringify(body)
    if (this.key === null || fingerprint !== this.fingerprint) {
      this.key = this.generate()
      this.fingerprint = fingerprint
    }
    return this.key
  }

  /** Call after the submission succeeded. */
  reset(): void {
    this.key = null
    this.fingerprint = null
  }

  /**
   * Keeps the key only when the outcome is unknown (network failure, 5xx): the request may have
   * been applied, so a retry must replay it. A definitive 4xx answer discards the key, so a later
   * deliberate retry is evaluated afresh instead of replaying the stored rejection.
   */
  afterError(error: unknown): void {
    const unknownOutcome = !(error instanceof ApiError) || error.kind === 'network' || error.kind === 'server'
    if (!unknownOutcome) this.reset()
  }
}

export function useIdempotencyKeeper(): IdempotencyKeeper {
  const [keeper] = useState(() => new IdempotencyKeeper())
  return keeper
}
