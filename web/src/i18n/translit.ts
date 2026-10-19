// Uzbek Latin → Uzbek Cyrillic transliteration for UI strings. The Cyrillic catalogue is derived
// from the Latin one, so every key exists in both and the texts never drift apart. Placeholders
// ({name}), the brand name, all-caps abbreviations (SMS, TOTP) and URLs are kept as they are.

const APOS = /['‘’ʻʼ`]/

/** Longest-first multi-letter rules (lower-case Latin after apostrophe normalisation). */
const multi: [string, string][] = [
  ['tsiya', 'ция'],
  ['tsion', 'цион'],
  ['tsent', 'цент'],
  ['yoʻ', 'йў'],
  ['yo', 'ё'],
  ['yu', 'ю'],
  ['ya', 'я'],
  ['ye', 'е'],
  ['sh', 'ш'],
  ['ch', 'ч'],
  ['oʻ', 'ў'],
  ['gʻ', 'ғ'],
]

/** Loanwords whose Cyrillic spelling keeps the soft sign (word-final unless noted). */
const softSign: [RegExp, string][] = [
  [/konsult/giu, 'консульт'],
  [/\bparol\b/giu, 'пароль'],
  [/\brol\b/giu, 'роль'],
  [/\b(fevral|aprel|iyul|iyun|sentyabr|oktyabr|noyabr|dekabr)\b/giu, ''],
]

const softMonths: Record<string, string> = {
  fevral: 'февраль', aprel: 'апрель', iyul: 'июль', iyun: 'июнь', sentyabr: 'сентябрь', oktyabr: 'октябрь', noyabr: 'ноябрь', dekabr: 'декабрь',
}

function preSoftSign(text: string): string {
  let s = text
  for (const [re, cyr] of softSign) {
    s = s.replace(re, (m: string) => {
      const out = cyr || softMonths[m.toLowerCase()] || m
      return m.charAt(0) === m.charAt(0).toUpperCase() ? out.charAt(0).toUpperCase() + out.slice(1) : out
    })
  }
  return s
}

const single: Record<string, string> = {
  a: 'а', b: 'б', c: 'ц', d: 'д', f: 'ф', g: 'г', h: 'ҳ', i: 'и', j: 'ж', k: 'к', l: 'л', m: 'м',
  n: 'н', o: 'о', p: 'п', q: 'қ', r: 'р', s: 'с', t: 'т', u: 'у', v: 'в', w: 'в', x: 'х', y: 'й', z: 'з',
}

const VOWELS = 'aeiouʻ'
const isLetter = (c: string | undefined) => !!c && /\p{L}/u.test(c)

/** Tokens copied verbatim: placeholders, URLs, e-mail-ish text, brand, abbreviations. */
const KEEP = /(\{\w+\}|https?:\/\/\S+|\S+@\S+|OqTish|Telegram Mini App|Mini App|\b[A-Z]{2,}\b|\bIANA\b|\bUTC\b)/

function convertPlain(text: string): string {
  // o' / g' (any apostrophe) → canonical modifier letter so the rules above match.
  const src = preSoftSign(text).replace(/([oOgG])['‘’ʻʼ`]/g, '$1ʻ')
  let out = ''
  let i = 0
  while (i < src.length) {
    const ch = src.charAt(i)
    const lower = src.slice(i, i + 5).toLowerCase()
    let matched = false
    for (const [lat, cyr] of multi) {
      if (lower.startsWith(lat)) {
        out += applyCase(src.slice(i, i + lat.length), cyr)
        i += lat.length
        matched = true
        break
      }
    }
    if (matched) continue
    const lc = ch.toLowerCase()
    if (lc === 'e') {
      const prev = src[i - 1]?.toLowerCase()
      const wordStart = !isLetter(prev) && prev !== 'ʻ'
      const cyr = wordStart || (prev !== undefined && VOWELS.includes(prev)) ? 'э' : 'е'
      out += ch === lc ? cyr : cyr.toUpperCase()
    } else if (single[lc] !== undefined) {
      const cyr = single[lc]
      out += ch === lc ? cyr : cyr.toUpperCase()
    } else if (APOS.test(ch) && isLetter(src[i - 1]) && isLetter(src[i + 1])) {
      out += 'ъ' // tutuq belgisi: ma’lumot → маълумот
    } else {
      out += ch
    }
    i += 1
  }
  return out
}

/** Keeps the case of the Latin source: "Sh" → "Ш", "SH" → "Ш", "sh" → "ш". */
function applyCase(source: string, cyr: string): string {
  if (source.charAt(0) === source.charAt(0).toLowerCase()) return cyr
  return cyr.charAt(0).toUpperCase() + cyr.slice(1)
}

export function latinToCyrillic(text: string): string {
  return text
    .split(KEEP)
    .map((part, idx) => (idx % 2 === 1 ? part : convertPlain(part)))
    .join('')
}
