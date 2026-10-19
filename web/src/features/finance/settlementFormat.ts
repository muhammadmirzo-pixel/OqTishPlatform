import type { SettlementDto } from '../../api/types'
import type { Locale } from '../../i18n/messages'
import { formatMonthYear } from '../../lib/format'

export const settlementTone: Record<SettlementDto['status'], string> = { Draft: 'neutral', Finalized: 'info', Paid: 'success', Voided: 'neutral' }

export function periodLabel(year: number, month: number, locale: Locale): string {
  return formatMonthYear(year, month, locale)
}
