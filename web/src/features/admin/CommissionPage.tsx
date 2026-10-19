import { CalendarX2, Percent, Plus } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router'
import { useApi } from '../../api/context'
import { commissionBases, rateBases, type CommissionBasis, type CommissionRuleBody, type CommissionRuleDto } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate, formatMoney } from '../../lib/format'
import { fractionToPercent, isPositive, parseAmount, percentToFraction, uzs } from '../../lib/money'
import { DEFAULT_TIME_ZONE, isValidDate, localDate, zonedToUtc } from '../../lib/time'
import { Dialog } from '../../ui/Dialog'
import { Checkbox, Field, FormError, SubmitButton } from '../../ui/forms'
import { DangerNote } from '../../ui/ReasonDialog'
import { EmptyState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { ClinicPicker } from './ClinicPicker'
import { useNow } from '../../hooks/useNow'

/** Commission contracts per clinic. Rules are never edited, only ended (FINANCIAL_RULES.md §4). */
export function CommissionPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.commission'))
  const api = useApi()
  const [params, setParams] = useSearchParams()
  const clinicId = params.get('clinic') ?? ''
  const rules = useQuery(clinicId ? `commission:${clinicId}` : null, () => api.admin.commissionRules(clinicId))
  const [creating, setCreating] = useState(false)
  const [ending, setEnding] = useState<CommissionRuleDto | null>(null)
  const toast = useToast()
  const tz = DEFAULT_TIME_ZONE

  return (
    <div className="stack">
      <PageHeader icon={Percent} title={t('adminNav.commission')} subtitle={t('comm.intro')} />
      <ClinicPicker value={clinicId} onChange={(id) => setParams(id ? { clinic: id } : {})} />
      {clinicId && (
        <>
          <div>
            <button type="button" className="btn" onClick={() => setCreating(true)}>
              <Plus className="icon" aria-hidden="true" />
              {t('comm.add')}
            </button>
          </div>
          <QueryView query={rules}>
            {(list) =>
              list.length === 0 ? (
                <EmptyState title={t('comm.none')} icon={Percent} />
              ) : (
                <div className="table-wrap">
                  <table className="table table--stack">
                    <thead>
                      <tr>
                        <th scope="col">{t('comm.basis')}</th>
                        <th scope="col">{t('comm.value')}</th>
                        <th scope="col">{t('comm.from')}</th>
                        <th scope="col">{t('comm.to')}</th>
                        <th scope="col">{t('comm.contract')}</th>
                        <th scope="col">{t('common.actions')}</th>
                      </tr>
                    </thead>
                    <tbody>
                      {list.map((r) => (
                        <tr key={r.id}>
                          <td data-label={t('comm.basis')}>{t(`comm.basis.${r.basis}`)}</td>
                          <td data-label={t('comm.value')}>{r.rate !== null ? `${fractionToPercent(r.rate)} %` : r.fixedFee ? formatMoney(r.fixedFee, locale) : '—'}</td>
                          <td data-label={t('comm.from')}>{formatInstantDate(r.effectiveFrom, tz, locale)}</td>
                          <td data-label={t('comm.to')}>{r.effectiveTo ? formatInstantDate(r.effectiveTo, tz, locale) : <Badge tone="success">{t('comm.open')}</Badge>}</td>
                          <td data-label={t('comm.contract')}>
                            <code>{r.contractReference}</code>
                          </td>
                          <td data-label={t('common.actions')}>
                            {!r.effectiveTo && (
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setEnding(r)}>
                                <CalendarX2 className="icon" aria-hidden="true" />
                                {t('comm.end')}
                              </button>
                            )}
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )
            }
          </QueryView>
        </>
      )}
      {creating && (
        <RuleDialog
          clinicId={clinicId}
          onDone={() => {
            setCreating(false)
            toast({ title: t('common.saved') })
            rules.reload()
          }}
          onClose={() => setCreating(false)}
        />
      )}
      {ending && (
        <EndDialog
          clinicId={clinicId}
          rule={ending}
          onDone={() => {
            setEnding(null)
            toast({ title: t('common.saved') })
            rules.reload()
          }}
          onClose={() => setEnding(null)}
        />
      )}
    </div>
  )
}

function RuleDialog({ clinicId, onDone, onClose }: { clinicId: string; onDone: () => void; onClose: () => void }) {
  const now = useNow(60_000)
  const { t } = useI18n()
  const api = useApi()
  const tz = DEFAULT_TIME_ZONE
  const [basis, setBasis] = useState<CommissionBasis>('CollectedPayment')
  const [rate, setRate] = useState('')
  const [fee, setFee] = useState('')
  const [from, setFrom] = useState(localDate(now, tz))
  const [to, setTo] = useState('')
  const [contract, setContract] = useState('')
  const [touched, setTouched] = useState(false)
  const create = useAction((body: CommissionRuleBody) => api.admin.createCommissionRule(clinicId, body))
  const isRate = rateBases.includes(basis)
  const fraction = percentToFraction(rate)
  const parsedFee = parseAmount(fee)
  const errors = {
    value: isRate ? (fraction ? null : t('valid.percent')) : parsedFee && isPositive(parsedFee) ? null : t('valid.amount'),
    from: isValidDate(from) ? null : t('valid.required'),
    to: to && (!isValidDate(to) || to <= from) ? t('valid.timeRange') : null,
    contract: !contract.trim() ? t('valid.required') : contract.length > 100 ? t('valid.maxLength', { max: 100 }) : null,
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (Object.values(errors).some(Boolean)) return
    const r = await create.run({
      basis,
      ...(isRate ? { rate: fraction! } : { fixedFee: uzs(parsedFee!) }),
      effectiveFrom: zonedToUtc(from, '00:00', tz),
      ...(to ? { effectiveTo: zonedToUtc(to, '00:00', tz) } : {}),
      contractReference: contract.trim(),
    })
    if (r.ok) onDone()
  }

  return (
    <Dialog title={t('comm.add')} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        <Field label={t('comm.basis')}>
          {(p) => (
            <select {...p} value={basis} onChange={(e) => setBasis(e.target.value as CommissionBasis)}>
              {commissionBases.map((b) => (
                <option key={b} value={b}>
                  {t(`comm.basis.${b}`)}
                </option>
              ))}
            </select>
          )}
        </Field>
        {isRate ? (
          <Field label={t('comm.rate')} hint={t('comm.rateHint')} error={touched ? errors.value : null}>
            {(p) => <input {...p} inputMode="decimal" value={rate} onChange={(e) => setRate(e.target.value)} />}
          </Field>
        ) : (
          <Field label={t('comm.fixedFee')} error={touched ? errors.value : null}>
            {(p) => <input {...p} inputMode="decimal" value={fee} onChange={(e) => setFee(e.target.value)} />}
          </Field>
        )}
        <div className="grid-2">
          <Field label={t('comm.from')} error={touched ? errors.from : null}>
            {(p) => <input {...p} type="date" value={from} onChange={(e) => setFrom(e.target.value)} />}
          </Field>
          <Field label={t('comm.to')} error={touched ? errors.to : null} optional>
            {(p) => <input {...p} type="date" value={to} min={from} onChange={(e) => setTo(e.target.value)} />}
          </Field>
        </div>
        <Field label={t('comm.contract')} hint={t('comm.contractHint')} error={touched ? errors.contract : null}>
          {(p) => <input {...p} maxLength={100} value={contract} onChange={(e) => setContract(e.target.value)} />}
        </Field>
        <p className="muted small">{t('comm.tzNote', { tz })}</p>
        <FormError error={create.error} />
        <SubmitButton pending={create.pending}>{t('common.create')}</SubmitButton>
      </form>
    </Dialog>
  )
}

function EndDialog({ clinicId, rule, onDone, onClose }: { clinicId: string; rule: CommissionRuleDto; onDone: () => void; onClose: () => void }) {
  const now = useNow(60_000)
  const { t } = useI18n()
  const api = useApi()
  const tz = DEFAULT_TIME_ZONE
  const [date, setDate] = useState(localDate(now, tz))
  const [acknowledged, setAcknowledged] = useState(false)
  const end = useAction((effectiveTo: string) => api.admin.endCommissionRule(clinicId, rule.id, effectiveTo))
  // Rules are never edited, only ended, so ending is irreversible: it needs an explicit acknowledgement.
  return (
    <Dialog title={`${t('comm.end')}: ${rule.contractReference}`} onClose={onClose} tone="danger">
      <form
        className="stack"
        onSubmit={async (e) => {
          e.preventDefault()
          if (!isValidDate(date) || !acknowledged) return
          const r = await end.run(zonedToUtc(date, '00:00', tz))
          if (r.ok) onDone()
        }}
      >
        <DangerNote>
          <p>{t('comm.endIntro', { contract: rule.contractReference })}</p>
        </DangerNote>
        <Field label={t('comm.to')}>{(p) => <input {...p} type="date" value={date} onChange={(e) => setDate(e.target.value)} />}</Field>
        <Checkbox label={t('adm.ack')} checked={acknowledged} onChange={setAcknowledged} />
        <FormError error={end.error} />
        <SubmitButton pending={end.pending} className="btn btn--danger" disabled={!acknowledged}>
          {t('comm.end')}
        </SubmitButton>
      </form>
    </Dialog>
  )
}
