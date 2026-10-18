import { useState } from 'react'
import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatDate, formatMoney } from '../../lib/format'
import { sumMoney } from '../../lib/money'
import { addDays, isValidDate, localDate } from '../../lib/time'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, Details, PageHeader } from '../../ui/widgets'
import { useBranchParam } from '../clinic/branchParam'
import { useClinic } from '../clinic/clinicContext'
import { useNow } from '../../hooks/useNow'

export function ReportsPage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.reports'))
  return (
    <div className="stack">
      <PageHeader title={t('clinicNav.reports')} subtitle={t('rep.note')} />
      <DailyReport />
      <MonthlyReport />
    </div>
  )
}

function DailyReport() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId, timeZoneFor, branchName } = useClinic()
  const [branch] = useBranchParam()
  const today = localDate(now, timeZoneFor(branch))
  const [from, setFrom] = useState(addDays(today, -6))
  const [to, setTo] = useState(today)
  const valid = isValidDate(from) && isValidDate(to) && from <= to
  const report = useQuery(valid ? `daily:${clinicId}:${branch ?? ''}:${from}:${to}` : null, () => api.finance.daily(clinicId, { branchId: branch, from, to }))

  return (
    <section className="card stack" aria-labelledby="daily-title">
      <h2 id="daily-title">{t('rep.daily')}</h2>
      <div className="filters">
        <label className="field">
          <span>{t('common.from')}</span>
          <input type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label className="field">
          <span>{t('common.to')}</span>
          <input type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />
        </label>
      </div>
      {!valid ? (
        <p className="field__error">{t('rep.rangeInvalid')}</p>
      ) : (
        <QueryView query={report}>
          {(rows) =>
            rows.length === 0 ? (
              <EmptyState title={t('rep.empty')} />
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.date')}</th>
                      {!branch && <th scope="col">{t('common.branch')}</th>}
                      <th scope="col" className="num">
                        {t('rep.visits')}
                      </th>
                      <th scope="col" className="num">
                        {t('pay.charged')}
                      </th>
                      <th scope="col" className="num">
                        {t('rep.collected')}
                      </th>
                      <th scope="col" className="num">
                        {t('pay.refunded')}
                      </th>
                      <th scope="col" className="num">
                        {t('rep.commission')}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r, i) => (
                      <tr key={`${r.date}-${r.branchId ?? ''}-${i}`}>
                        <td>{formatDate(r.date, locale)}</td>
                        {!branch && <td>{r.branchId ? branchName(r.branchId) : t('common.allBranches')}</td>}
                        <td className="num">{r.visits}</td>
                        <td className="num">{formatMoney(r.charged, locale)}</td>
                        <td className="num">{formatMoney(r.collected, locale)}</td>
                        <td className="num">{formatMoney(r.refunded, locale)}</td>
                        <td className="num">{formatMoney(r.commission, locale)}</td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th scope="row" colSpan={branch ? 1 : 2}>
                        {t('common.total')}
                      </th>
                      <td className="num">{rows.reduce((n, r) => n + r.visits, 0)}</td>
                      <td className="num">{formatMoney(sumMoney(rows.map((r) => r.charged)), locale)}</td>
                      <td className="num">{formatMoney(sumMoney(rows.map((r) => r.collected)), locale)}</td>
                      <td className="num">{formatMoney(sumMoney(rows.map((r) => r.refunded)), locale)}</td>
                      <td className="num">{formatMoney(sumMoney(rows.map((r) => r.commission)), locale)}</td>
                    </tr>
                  </tfoot>
                </table>
              </div>
            )
          }
        </QueryView>
      )}
    </section>
  )
}

function MonthlyReport() {
  const now = useNow(60_000)
  const { t, locale } = useI18n()
  const api = useApi()
  const { clinicId, timeZoneFor } = useClinic()
  const [branch] = useBranchParam()
  const today = localDate(now, timeZoneFor(branch))
  const [month, setMonth] = useState(today.slice(0, 7))
  const [y, m] = month.split('-').map(Number)
  const valid = Boolean(y && m)
  const report = useQuery(valid ? `monthly:${clinicId}:${branch ?? ''}:${month}` : null, () => api.finance.monthly(clinicId, { year: y!, month: m!, branchId: branch }))

  return (
    <section className="card stack" aria-labelledby="monthly-title">
      <h2 id="monthly-title">{t('rep.monthly')}</h2>
      <label className="field field--inline">
        <span>{t('common.month')}</span>
        <input type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
      </label>
      <QueryView query={report}>
        {(r) => (
          <div className="stack">
            <div className="row">
              {r.reconciled ? <Badge tone="success">{t('rep.reconciled')}</Badge> : <Badge tone="danger">{t('rep.notReconciled')}</Badge>}
              {!r.commissionRuleConfigured && <Badge tone="warning">{t('rep.noContract')}</Badge>}
            </div>
            {!r.reconciled && <p className="alert alert--warning">{t('rep.notReconciledNote')}</p>}
            <Details
              items={[
                [t('rep.visits'), r.visits],
                [t('pay.charged'), formatMoney(r.charged, locale)],
                [t('rep.collected'), formatMoney(r.collected, locale)],
                [t('pay.refunded'), formatMoney(r.refunded, locale)],
                [t('rep.commission'), formatMoney(r.commission, locale)],
              ]}
            />
          </div>
        )}
      </QueryView>
    </section>
  )
}
