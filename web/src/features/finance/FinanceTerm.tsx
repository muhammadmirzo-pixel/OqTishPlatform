import { useI18n } from '../../i18n/context'
import type { MessageKey } from '../../i18n/messages'
import { InfoTip } from '../../ui/Tooltip'

export type FinanceTermKey = 'charged' | 'collected' | 'refunded' | 'commission' | 'amountDue' | 'paid' | 'outstanding'

const labels: Record<FinanceTermKey, MessageKey> = {
  charged: 'pay.charged',
  collected: 'rep.collected',
  refunded: 'pay.refunded',
  commission: 'rep.commission',
  amountDue: 'settle.amountDue',
  paid: 'pay.paid',
  outstanding: 'pay.outstanding',
}

/** A money term with its definition in an (i) tip, so figures like "collected" are never ambiguous. */
export function FinanceTerm({ term }: { term: FinanceTermKey }) {
  const { t } = useI18n()
  const label = t(labels[term])
  return (
    <span className="term">
      <span>{label}</span>
      <InfoTip label={t('tip.about', { term: label })}>{t(`fin.def.${term}`)}</InfoTip>
    </span>
  )
}

/** "Taxminiy" / "Yakuniy" badge for figures of a month: final once a Finalized/Paid settlement locks it. */
export function PeriodState({ locked }: { locked: boolean }) {
  const { t } = useI18n()
  return (
    <span className="term">
      <span className={`badge badge--${locked ? 'success' : 'warning'}`}>{t(locked ? 'fin.final' : 'fin.estimated')}</span>
      <InfoTip label={t('tip.about', { term: t(locked ? 'fin.final' : 'fin.estimated') })}>{t(locked ? 'fin.finalInfo' : 'fin.estimatedInfo')}</InfoTip>
    </span>
  )
}
