// UI strings, grouped into section files (src/i18n/sections). Uzbek (Latin) defines the key set of
// each section; Russian and English are typed against it, so a missing key fails the build. The
// tests additionally check completeness, placeholders and that sections don't share keys.
// Placeholders use {name} syntax.

import { account } from './sections/account'
import { admin } from './sections/admin'
import { auth } from './sections/auth'
import { booking } from './sections/booking'
import { clinicOps } from './sections/clinicOps'
import { clinicSetup } from './sections/clinicSetup'
import { common } from './sections/common'
import { errors } from './sections/errors'
import { finance } from './sections/finance'
import { patient } from './sections/patient'
import { publicSite } from './sections/publicSite'
import { shell } from './sections/shell'
import { telegram } from './sections/telegram'
import { ui } from './sections/ui'
import { latinToCyrillic } from './translit'
import { uzCyrlOverrides } from './uzCyrlOverrides'

export const sections = { common, errors, shell, auth, publicSite, booking, patient, account, clinicOps, clinicSetup, finance, admin, telegram, ui }

export const uz = {
  ...common.uz,
  ...errors.uz,
  ...shell.uz,
  ...auth.uz,
  ...publicSite.uz,
  ...booking.uz,
  ...patient.uz,
  ...account.uz,
  ...clinicOps.uz,
  ...clinicSetup.uz,
  ...finance.uz,
  ...admin.uz,
  ...telegram.uz,
  ...ui.uz,
}

export type MessageKey = keyof typeof uz
export type Messages = Record<MessageKey, string>

export const ru: Messages = {
  ...common.ru,
  ...errors.ru,
  ...shell.ru,
  ...auth.ru,
  ...publicSite.ru,
  ...booking.ru,
  ...patient.ru,
  ...account.ru,
  ...clinicOps.ru,
  ...clinicSetup.ru,
  ...finance.ru,
  ...admin.ru,
  ...telegram.ru,
  ...ui.ru,
}

export const en: Messages = {
  ...common.en,
  ...errors.en,
  ...shell.en,
  ...auth.en,
  ...publicSite.en,
  ...booking.en,
  ...patient.en,
  ...account.en,
  ...clinicOps.en,
  ...clinicSetup.en,
  ...finance.en,
  ...admin.en,
  ...telegram.en,
  ...ui.en,
}

/**
 * Uzbek Cyrillic is derived from the Latin catalogue (i18n/translit.ts); `uzCyrlOverrides` fixes the
 * few words a rule-based conversion gets wrong (mostly Russian loanwords).
 */
export const uzCyrl: Messages = Object.fromEntries(
  (Object.keys(uz) as MessageKey[]).map((key) => [key, uzCyrlOverrides[key] ?? latinToCyrillic(uz[key])]),
) as Messages

export const locales = ['uz', 'uz-Cyrl', 'ru', 'en'] as const
export type Locale = (typeof locales)[number]

export function isLocale(value: string): value is Locale {
  return (locales as readonly string[]).includes(value)
}

export const catalog: Record<Locale, Messages> = { uz, 'uz-Cyrl': uzCyrl, ru, en }

export const localeNames: Record<Locale, string> = { uz: 'O‘zbekcha', 'uz-Cyrl': 'Ўзбекча', ru: 'Русский', en: 'English' }
