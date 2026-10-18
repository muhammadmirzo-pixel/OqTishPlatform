import { describe, expect, it } from 'vitest'
import { interpolate } from './context'
import { catalog, locales, uz } from './messages'

describe('message catalog', () => {
  it.each(locales)('%s has every key with a non-empty value', (locale) => {
    const messages = catalog[locale]
    expect(Object.keys(messages).sort()).toEqual(Object.keys(uz).sort())
    for (const [key, value] of Object.entries(messages)) {
      expect(value.trim(), key).not.toBe('')
    }
  })

  it.each(locales)('%s keeps the same placeholders as Uzbek', (locale) => {
    const placeholders = (s: string) => (s.match(/\{\w+\}/g) ?? []).sort()
    for (const key of Object.keys(uz) as (keyof typeof uz)[]) {
      expect(placeholders(catalog[locale][key]), key).toEqual(placeholders(uz[key]))
    }
  })
})

describe('interpolate', () => {
  it('replaces known placeholders and leaves unknown ones visible', () => {
    expect(interpolate('{count} ta klinika', { count: 3 })).toBe('3 ta klinika')
    expect(interpolate('Salom, {name}!', {})).toBe('Salom, {name}!')
  })
})

describe('catalogue sections', () => {
  it('do not define the same key twice (a later section would silently override it)', async () => {
    const { sections } = await import('./messages')
    const seen = new Map<string, string>()
    for (const [name, section] of Object.entries(sections)) {
      for (const key of Object.keys(section.uz)) {
        expect(seen.get(key), `${key} in ${name}`).toBeUndefined()
        seen.set(key, name)
      }
    }
  })
})
