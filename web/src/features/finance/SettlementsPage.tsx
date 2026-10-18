import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate, formatMoney } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from '../clinic/clinicContext'
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
      <PageHeader title={t('clinicNav.settlements')} subtitle={t('settle.clinicIntro')} />
      <QueryView query={list}>
        {(items) =>
          items.length === 0 ? (
            <EmptyState title={t('settle.none')} />
          ) : (
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th scope="col">{t('settle.period')}</th>
                    <th scope="col">{t('common.status')}</th>
                    <th scope="col" className="num">
                      {t('settle.amountDue')}
                    </th>
                    <th scope="col">{t('settle.paidAt')}</th>
                    <th scope="col">{t('settle.reference')}</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((s) => (
                    <tr key={s.id}>
                      <td>{periodLabel(s.year, s.month, locale)}</td>
                      <td>
                        <Badge tone={settlementTone[s.status]}>{t(`settle.${s.status}`)}</Badge>
                      </td>
                      <td className="num">{formatMoney(s.amountDue, locale)}</td>
                      <td>{s.paidAt ? formatInstantDate(s.paidAt, DEFAULT_TIME_ZONE, locale) : '—'}</td>
                      <td>{s.paidReference ?? '—'}</td>
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
