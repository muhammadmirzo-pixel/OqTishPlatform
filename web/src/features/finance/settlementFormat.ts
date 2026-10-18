import type { SettlementDto } from '../../api/types'

export const settlementTone: Record<SettlementDto['status'], string> = { Draft: 'neutral', Finalized: 'info', Paid: 'success', Voided: 'neutral' }

export function periodLabel(year: number, month: number, locale: string): string {
  const intl = locale === 'en' ? 'en-GB' : locale === 'ru' ? 'ru-RU' : 'uz-Latn-UZ'
  return new Intl.DateTimeFormat(intl, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(new Date(Date.UTC(year, month - 1, 1)))
}
