import { HandCoins } from 'lucide-react'
import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate, formatMoney } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { EmptyState, QueryView } from '../../ui/states'
import { InfoTip } from '../../ui/Tooltip'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from '../clinic/clinicContext'
import { FinanceTerm } from './FinanceTerm'
import { periodLabel, settlementTone } from './settlementFormat'

/** Clinic's read-only view of its monthly settlements with the platform. */
export function SettlementsPage() {
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.settlements'))
  const api = useApi()
  const { clinicId } = useClinic()
  const list = useQuery(`settlements:${clinicId}`, () => api.finance.settlements(clinicId))
  return (
    <div className="stack">
      <PageHeader icon={HandCoins} title={t('clinicNav.settlements')} subtitle={t('settle.clinicIntro')} />
      <QueryView query={list}>
        {(items) =>
          items.length === 0 ? (
            <EmptyState title={t('settle.none')} icon={HandCoins} />
          ) : (
            <div className="table-wrap">
              <table className="table table--stack">
                <thead>
                  <tr>
                    <th scope="col">{t('settle.period')}</th>
                    <th scope="col">
                      <span className="term">
                        <span>{t('common.status')}</span>
                        <InfoTip label={t('tip.about', { term: t('common.status') })}>{t('settle.statusInfo')}</InfoTip>
                      </span>
                    </th>
                    <th scope="col" className="num">
                      <FinanceTerm term="amountDue" />
                    </th>
                    <th scope="col">{t('settle.paidAt')}</th>
                    <th scope="col">{t('settle.reference')}</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((s) => (
                    <tr key={s.id}>
                      <td data-label={t('settle.period')}>{periodLabel(s.year, s.month, locale)}</td>
                      <td data-label={t('common.status')}>
                        <Badge tone={settlementTone[s.status]}>{t(`settle.${s.status}`)}</Badge>
                      </td>
                      <td className="num" data-label={t('settle.amountDue')}>
                        {formatMoney(s.amountDue, locale)}
                      </td>
                      <td data-label={t('settle.paidAt')}>{s.paidAt ? formatInstantDate(s.paidAt, DEFAULT_TIME_ZONE, locale) : '—'}</td>
                      <td data-label={t('settle.reference')}>{s.paidReference ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        }
      </QueryView>
    </div>
  )
}
