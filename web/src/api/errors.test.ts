import { describe, expect, it } from 'vitest'
import apiDoc from '../../../docs/API.md?raw'
import { catalog, locales } from '../i18n/messages'
import { ApiError } from './client'
import { errorCodes, errorMessage, messageKeyForCode } from './errors'

/** Codes listed in the "## 9. Error codes" table of docs/API.md (cells like `a.b` / `c.d`). */
function documentedCodes(): string[] {
  const section = apiDoc.slice(apiDoc.indexOf('## 9. Error codes'))
  const codes = new Set<string>()
  for (const line of section.split('\n')) {
    if (!line.startsWith('| `')) continue
    const firstCell = line.split('|')[1] ?? ''
    for (const m of firstCell.matchAll(/`([a-z_]+\.[a-z_]+)`/g)) codes.add(m[1]!)
  }
  return [...codes]
}

describe('error code mapping', () => {
  it('reads the documented codes from docs/API.md §9', () => {
    expect(documentedCodes().length).toBeGreaterThanOrEqual(40)
  })

  it('maps every documented code to a localised message in every language', () => {
    for (const code of documentedCodes()) {
      expect(errorCodes, code).toContain(code)
      const key = messageKeyForCode(code)
      expect(key, code).not.toBeNull()
      for (const locale of locales) expect(catalog[locale][key!].trim(), `${locale}:${code}`).not.toBe('')
    }
  })

  it('has no stale codes that are not documented', () => {
    const documented = new Set(documentedCodes())
    for (const code of errorCodes) expect(documented.has(code), code).toBe(true)
  })

  it('prefers the code, then falls back to the error kind', () => {
    const t = (k: keyof typeof catalog.en) => catalog.en[k]
    expect(errorMessage(t, new ApiError('conflict', 409, { code: 'booking.slot_unavailable' }))).toBe(catalog.en['err.booking.slot_unavailable'])
    expect(errorMessage(t, new ApiError('conflict', 409, { code: 'unknown.code' }))).toBe(catalog.en['err.kind.conflict'])
    expect(errorMessage(t, new ApiError('network', undefined, undefined))).toBe(catalog.en['err.network'])
    expect(errorMessage(t, new Error('boom'))).toBe(catalog.en['err.server'])
  })
})
