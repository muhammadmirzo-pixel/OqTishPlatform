import { KeyRound, ShieldAlert } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import type { AppointmentDto, Page } from '../../api/types'
import { useAction } from '../../hooks/useAction'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatDateTime } from '../../lib/format'
import { formatPhone } from '../../lib/phone'
import { Field, FormError, SubmitButton } from '../../ui/forms'
import { Pagination } from '../../ui/Pagination'
import { EmptyState } from '../../ui/states'
import { PageHeader, StatusBadge } from '../../ui/widgets'
import { ClinicPicker } from './ClinicPicker'

const MIN_REASON = 15

/**
 * Break-glass read of a clinic's appointments. Every request carries the typed reason in
 * X-Access-Reason and is audited server-side. Nothing is cached: changing the clinic or reason
 * clears the result.
 */
export function TenantDataPage() {
  const { t, locale } = useI18n()
  useTitle(t('adminNav.tenantData'))
  const api = useApi()
  const [clinicId, setClinicId] = useState('')
  const [reason, setReason] = useState('')
  const [touched, setTouched] = useState(false)
  const [result, setResult] = useState<Page<AppointmentDto> | null>(null)
  const load = useAction((page: number) => api.admin.tenantAppointments(clinicId, { page }, reason))
  const errors = {
    clinic: !clinicId ? t('valid.required') : null,
    reason: reason.trim().length < MIN_REASON ? t('valid.minLength', { min: MIN_REASON }) : reason.length > 300 ? t('valid.maxLength', { max: 300 }) : null,
  }

  async function fetchPage(page: number) {
    const r = await load.run(page)
    if (r.ok) setResult(r.value)
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.clinic || errors.reason) return
    await fetchPage(1)
  }

  return (
    <div className="stack">
      <PageHeader icon={KeyRound} title={t('adminNav.tenantData')} />
      <div className="danger-note" role="note">
        <ShieldAlert className="icon" aria-hidden="true" />
        <p>{t('adm.breakGlass')}</p>
      </div>
      <form className="card stack" onSubmit={submit} noValidate>
        <ClinicPicker
          value={clinicId}
          onChange={(id) => {
            setClinicId(id)
            setResult(null)
          }}
        />
        {touched && errors.clinic && <p className="field__error">{errors.clinic}</p>}
        <Field label={t('adm.accessReason')} hint={t('adm.accessReasonHint')} error={touched ? errors.reason : null}>
          {(p) => (
            <textarea
              {...p}
              rows={2}
              maxLength={300}
              value={reason}
              onChange={(e) => {
                setReason(e.target.value)
                setResult(null)
              }}
            />
          )}
        </Field>
        <FormError error={load.error} />
        <SubmitButton pending={load.pending}>{t('adm.openData')}</SubmitButton>
      </form>
      {result &&
        (result.items.length === 0 ? (
          <EmptyState icon={KeyRound} />
        ) : (
          <>
            <div className="table-wrap">
              <table className="table table--stack">
                <thead>
                  <tr>
                    <th scope="col">{t('common.time')}</th>
                    <th scope="col">{t('common.patient')}</th>
                    <th scope="col">{t('common.service')}</th>
                    <th scope="col">{t('common.branch')}</th>
                    <th scope="col">{t('common.status')}</th>
                  </tr>
                </thead>
                <tbody>
                  {result.items.map((a) => (
                    <tr key={a.id}>
                      <td data-label={t('common.time')}>{formatDateTime(a.startsAt, a.timeZone, locale)}</td>
                      <td data-label={t('common.patient')}>
                        {a.contactName}
                        <div className="muted small nowrap">{formatPhone(a.contactPhone)}</div>
                      </td>
                      <td data-label={t('common.service')}>{a.serviceName}</td>
                      <td data-label={t('common.branch')}>{a.branchName}</td>
                      <td data-label={t('common.status')}>
                        <StatusBadge status={a.status} />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <Pagination page={result.page} pageSize={result.pageSize} totalCount={result.totalCount} onPage={(p) => void fetchPage(p)} />
          </>
        ))}
    </div>
  )
}
