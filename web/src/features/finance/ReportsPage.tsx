import { ReceiptText } from 'lucide-react'
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
import { FinanceTerm, PeriodState } from './FinanceTerm'
import { useNow } from '../../hooks/useNow'

export function ReportsPage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.reports'))
  return (
    <div className="stack">
      <PageHeader icon={ReceiptText} title={t('clinicNav.reports')} subtitle={t('rep.note')} />
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
              <EmptyState title={t('rep.empty')} icon={ReceiptText} />
            ) : (
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.date')}</th>
                      {!branch && <th scope="col">{t('common.branch')}</th>}
                      <th scope="col" className="num">
                        {t('rep.visits')}
                      </th>
                      <th scope="col" className="num">
                        <FinanceTerm term="charged" />
                      </th>
                      <th scope="col" className="num">
                        <FinanceTerm term="collected" />
                      </th>
                      <th scope="col" className="num">
                        <FinanceTerm term="refunded" />
                      </th>
                      <th scope="col" className="num">
                        <FinanceTerm term="commission" />
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r, i) => (
                      <tr key={`${r.date}-${r.branchId ?? ''}-${i}`}>
                        <td data-label={t('common.date')} className="nowrap">
                          {formatDate(r.date, locale)}
                        </td>
                        {!branch && <td data-label={t('common.branch')}>{r.branchId ? branchName(r.branchId) : t('common.allBranches')}</td>}
                        <td className="num" data-label={t('rep.visits')}>
                          {r.visits}
                        </td>
                        <td className="num" data-label={t('pay.charged')}>
                          {formatMoney(r.charged, locale)}
                        </td>
                        <td className="num" data-label={t('rep.collected')}>
                          {formatMoney(r.collected, locale)}
                        </td>
                        <td className="num" data-label={t('pay.refunded')}>
                          {formatMoney(r.refunded, locale)}
                        </td>
                        <td className="num" data-label={t('rep.commission')}>
                          {formatMoney(r.commission, locale)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                  <tfoot>
                    <tr>
                      <th scope="row" colSpan={branch ? 1 : 2}>
                        {t('common.total')}
                      </th>
                      <td className="num" data-label={t('rep.visits')}>
                        {rows.reduce((n, r) => n + r.visits, 0)}
                      </td>
                      <td className="num" data-label={t('pay.charged')}>
                        {formatMoney(sumMoney(rows.map((r) => r.charged)), locale)}
                      </td>
                      <td className="num" data-label={t('rep.collected')}>
                        {formatMoney(sumMoney(rows.map((r) => r.collected)), locale)}
                      </td>
                      <td className="num" data-label={t('pay.refunded')}>
                        {formatMoney(sumMoney(rows.map((r) => r.refunded)), locale)}
                      </td>
                      <td className="num" data-label={t('rep.commission')}>
                        {formatMoney(sumMoney(rows.map((r) => r.commission)), locale)}
                      </td>
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
              <PeriodState locked={r.locked} />
              {r.reconciled ? <Badge tone="success">{t('rep.reconciled')}</Badge> : <Badge tone="danger">{t('rep.notReconciled')}</Badge>}
              {!r.commissionRuleConfigured && <Badge tone="warning">{t('rep.noContract')}</Badge>}
            </div>
            {!r.reconciled && <p className="alert alert--warning">{t('rep.notReconciledNote')}</p>}
            <Details
              items={[
                [t('rep.visits'), r.visits],
                [<FinanceTerm key="c" term="charged" />, formatMoney(r.charged, locale)],
                [<FinanceTerm key="k" term="collected" />, formatMoney(r.collected, locale)],
                [<FinanceTerm key="r" term="refunded" />, formatMoney(r.refunded, locale)],
                [<FinanceTerm key="m" term="commission" />, formatMoney(r.commission, locale)],
              ]}
            />
          </div>
        )}
      </QueryView>
    </section>
  )
}
