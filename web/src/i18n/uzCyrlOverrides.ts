import type { MessageKey } from './messages'

/**
 * Hand-written Uzbek Cyrillic for keys where automatic transliteration is wrong. Keep this list
 * short: fix the Latin source first when the Latin text itself is the problem.
 */
export const uzCyrlOverrides: Partial<Record<MessageKey, string>> = {}
