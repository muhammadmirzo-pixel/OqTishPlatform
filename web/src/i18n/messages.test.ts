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
