import { describe, expect, it } from 'vitest'
import { latinToCyrillic } from './translit'

describe('latinToCyrillic', () => {
  it.each([
    ["Bo'sh vaqt", 'Бўш вақт'],
    ['Yo‘q', 'Йўқ'],
    ["Ma'lumot", 'Маълумот'],
    ['Eslatma', 'Эслатма'],
    ['Shifokor tanlang', 'Шифокор танланг'],
    ['Qayerda', 'Қаерда'],
    ['Yangi yozuv', 'Янги ёзув'],
    ['Diagnostika va konsultatsiya', 'Диагностика ва консультация'],
    ['G‘alaba choy', 'Ғалаба чой'],
    ['Ketsangiz', 'Кетсангиз'],
  ])('%s → %s', (latin, cyr) => {
    expect(latinToCyrillic(latin)).toBe(cyr)
  })

  it('keeps placeholders, the brand and abbreviations', () => {
    expect(latinToCyrillic('{count} ta klinika OqTish SMS')).toBe('{count} та клиника OqTish SMS')
  })
})
