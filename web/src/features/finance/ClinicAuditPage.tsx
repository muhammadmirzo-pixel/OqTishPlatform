import { useState } from 'react'
import { useApi } from '../../api/context'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { QueryView } from '../../ui/states'
import { PageHeader } from '../../ui/widgets'
import { useClinic } from '../clinic/clinicContext'
import { AuditTable } from './AuditTable'

export function ClinicAuditPage() {
  const { t } = useI18n()
  useTitle(t('clinicNav.audit'))
  const api = useApi()
  const { clinicId } = useClinic()
  const [page, setPage] = useState(1)
  const audit = useQuery(`clinic-audit:${clinicId}:${page}`, () => api.finance.audit(clinicId, page))
  return (
    <div className="stack">
      <PageHeader title={t('clinicNav.audit')} subtitle={t('audit.clinicIntro')} />
      <QueryView query={audit}>{(p) => <AuditTable page={p} onPage={setPage} />}</QueryView>
    </div>
  )
}
