import { describe, expect, it } from 'vitest'
import apiDoc from '../../../docs/API.md?raw'
import { branchTimeZones, serviceCategories } from '../api/types'
import { catalog, locales } from '../i18n/messages'
import { categoryLabel } from './categories'

describe('fixed service categories', () => {
  it('match the keys documented in docs/API.md §2.1', () => {
    const line = apiDoc.split('\n').find((l) => l.startsWith('Service category keys:'))
    expect(line).toBeDefined()
    const documented = [...line!.matchAll(/`([a-z_]+)`/g)].map((m) => m[1])
    expect([...serviceCategories]).toEqual(documented)
  })

  it('have a label in every language and fall back for unknown keys', () => {
    for (const locale of locales) for (const key of serviceCategories) expect(catalog[locale][`category.${key}`].trim(), `${locale}:${key}`).not.toBe('')
    const t = (k: keyof typeof catalog.ru) => catalog.ru[k]
    expect(categoryLabel(t, 'orthodontics')).toBe(catalog.ru['category.orthodontics'])
    expect(categoryLabel(t, 'future_key', 'Kelajak')).toBe('Kelajak')
    expect(categoryLabel(t, 'future_key')).toBe('future_key')
  })

  it('time zones match the allow-list in docs/API.md §4.1', () => {
    for (const zone of branchTimeZones) expect(apiDoc).toContain(`'${zone}'`)
    for (const locale of locales) for (const zone of branchTimeZones) expect(catalog[locale][`tz.${zone}`]).toContain(zone)
  })
})
