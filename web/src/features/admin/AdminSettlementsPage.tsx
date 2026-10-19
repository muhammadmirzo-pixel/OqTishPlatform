import { HandCoins } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import { settlementStatuses, type SettlementDto, type SettlementStatus } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate, formatMoney } from '../../lib/format'
import { DEFAULT_TIME_ZONE, isValidDate, localDate, zonedToUtc } from '../../lib/time'
import { Dialog } from '../../ui/Dialog'
import { SelectField } from '../../ui/Select'
import { Field, FormError, SubmitButton } from '../../ui/forms'
import { ConfirmDialog, ReasonDialog } from '../../ui/ReasonDialog'
import { useToast } from '../../ui/toastContext'
import { EmptyState, QueryView } from '../../ui/states'
import { Badge, PageHeader } from '../../ui/widgets'
import { FinanceTerm } from '../finance/FinanceTerm'
import { periodLabel, settlementTone } from '../finance/settlementFormat'
import { ClinicPicker } from './ClinicPicker'
import { useNow } from '../../hooks/useNow'

type Action = { kind: 'finalize' | 'pay' | 'void'; s: SettlementDto }

export function AdminSettlementsPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.settlements'))
  const api = useApi()
  const [clinicId, setClinicId] = useState('')
  const [status, setStatus] = useState<SettlementStatus | ''>('')
  const [action, setAction] = useState<Action | null>(null)
  const toast = useToast()
  const list = useQuery(`admin-settlements:${clinicId}:${status}`, () => api.admin.settlements({ clinicId: clinicId || undefined, status: status || undefined }))
  const tz = DEFAULT_TIME_ZONE
  const done = () => {
    setAction(null)
    toast({ title: t('adm.done') })
    list.reload()
  }

  return (
    <div className="stack">
      <PageHeader icon={HandCoins} title={t('adminNav.settlements')} subtitle={t('settle.adminIntro')} />
      <div className="filters">
        <ClinicPicker value={clinicId} onChange={setClinicId} />
        <SelectField
          label={t('common.status')}
          value={status}
          onChange={(v) => setStatus(v as SettlementStatus | '')}
          options={[{ value: '', label: t('common.all') }, ...settlementStatuses.map((s) => ({ value: s, label: t(`settle.${s}`) }))]}
        />
      </div>
      {clinicId && <CreateSettlement clinicId={clinicId} onCreated={done} />}
      <QueryView query={list}>
        {(items) =>
          items.length === 0 ? (
            <EmptyState title={t('settle.none')} icon={HandCoins} />
          ) : (
            <div className="table-wrap">
              <table className="table table--stack">
                <thead>
                  <tr>
                    <th scope="col">{t('common.clinic')}</th>
                    <th scope="col">{t('settle.period')}</th>
                    <th scope="col">{t('common.status')}</th>
                    <th scope="col" className="num">
                      <FinanceTerm term="amountDue" />
                    </th>
                    <th scope="col">{t('settle.paidAt')}</th>
                    <th scope="col">{t('common.actions')}</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((s) => (
                    <tr key={s.id}>
                      <td data-label={t('common.clinic')}>{s.clinicName}</td>
                      <td data-label={t('settle.period')}>{periodLabel(s.year, s.month, locale)}</td>
                      <td data-label={t('common.status')}>
                        <Badge tone={settlementTone[s.status]}>{t(`settle.${s.status}`)}</Badge>
                        {s.voidReason && <div className="muted small">{s.voidReason}</div>}
                      </td>
                      <td className="num" data-label={t('settle.amountDue')}>
                        {formatMoney(s.amountDue, locale)}
                      </td>
                      <td data-label={t('settle.paidAt')}>{s.paidAt ? `${formatInstantDate(s.paidAt, tz, locale)} · ${s.paidReference ?? ''}` : '—'}</td>
                      <td data-label={t('common.actions')}>
                        <div className="row">
                          {s.status === 'Draft' && (
                            <>
                              <button type="button" className="btn btn--small" onClick={() => setAction({ kind: 'finalize', s })}>
                                {t('settle.finalize')}
                              </button>
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setAction({ kind: 'void', s })}>
                                {t('settle.void')}
                              </button>
                            </>
                          )}
                          {s.status === 'Finalized' && (
                            <button type="button" className="btn btn--small" onClick={() => setAction({ kind: 'pay', s })}>
                              {t('settle.markPaid')}
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )
        }
      </QueryView>
      {action?.kind === 'finalize' && (
        <ConfirmDialog
          title={`${t('settle.finalize')}: ${action.s.clinicName}, ${periodLabel(action.s.year, action.s.month, locale)}`}
          body={<p>{t('settle.finalizeBody')}</p>}
          confirmLabel={t('settle.finalize')}
          danger
          acknowledge={t('adm.ack')}
          onConfirm={() => api.admin.finalizeSettlement(action.s.id)}
          onDone={done}
          onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'void' && (
        <ReasonDialog
          title={`${t('settle.void')}: ${action.s.clinicName}, ${periodLabel(action.s.year, action.s.month, locale)}`}
          label={t('common.reason')}
          danger
          intro={<p>{t('settle.voidBody')}</p>}
          submitLabel={t('settle.void')}
          onSubmit={(reason) => api.admin.voidSettlement(action.s.id, reason)}
          onDone={done}
          onClose={() => setAction(null)}
        />
      )}
      {action?.kind === 'pay' && <MarkPaidDialog settlement={action.s} onDone={done} onClose={() => setAction(null)} />}
    </div>
  )
}

function CreateSettlement({ clinicId, onCreated }: { clinicId: string; onCreated: () => void }) {
  const now = useNow(60_000)
  const { t } = useI18n()
  const api = useApi()
  const today = localDate(now, DEFAULT_TIME_ZONE)
  const [month, setMonth] = useState(today.slice(0, 7))
  const create = useAction((body: { clinicId: string; year: number; month: number }) => api.admin.createSettlement(body))
  return (
    <form
      className="card row"
      onSubmit={async (e: FormEvent) => {
        e.preventDefault()
        const [y, m] = month.split('-').map(Number)
        if (!y || !m) return
        const r = await create.run({ clinicId, year: y, month: m })
        if (r.ok) onCreated()
      }}
    >
      <label className="field field--inline">
        <span>{t('settle.period')}</span>
        <input type="month" value={month} onChange={(e) => setMonth(e.target.value)} />
      </label>
      <SubmitButton pending={create.pending}>{t('settle.create')}</SubmitButton>
      <FormError error={create.error} />
    </form>
  )
}

function MarkPaidDialog({ settlement, onDone, onClose }: { settlement: SettlementDto; onDone: () => void; onClose: () => void }) {
  const now = useNow(60_000)
  const { t } = useI18n()
  const api = useApi()
  const [date, setDate] = useState(localDate(now, DEFAULT_TIME_ZONE))
  const [reference, setReference] = useState('')
  const [touched, setTouched] = useState(false)
  const pay = useAction((body: { paidAt: string; reference: string }) => api.admin.markSettlementPaid(settlement.id, body))
  const error = !reference.trim() ? t('valid.required') : null
  return (
    <Dialog title={t('settle.markPaid')} onClose={onClose}>
      <form
        className="stack"
        noValidate
        onSubmit={async (e) => {
          e.preventDefault()
          setTouched(true)
          if (error || !isValidDate(date)) return
          const r = await pay.run({ paidAt: zonedToUtc(date, '12:00', DEFAULT_TIME_ZONE), reference: reference.trim() })
          if (r.ok) onDone()
        }}
      >
        <Field label={t('settle.paidAt')}>{(p) => <input {...p} type="date" value={date} onChange={(e) => setDate(e.target.value)} />}</Field>
        <Field label={t('settle.reference')} error={touched ? error : null}>
          {(p) => <input {...p} maxLength={100} value={reference} onChange={(e) => setReference(e.target.value)} />}
        </Field>
        <FormError error={pay.error} />
        <SubmitButton pending={pay.pending}>{t('settle.markPaid')}</SubmitButton>
      </form>
    </Dialog>
  )
}
