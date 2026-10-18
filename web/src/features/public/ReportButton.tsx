import { useState } from 'react'
import { Link, useLocation } from 'react-router'
import { useApi } from '../../api/context'
import { loginPath } from '../../auth/session'
import { useSession } from '../../auth/session'
import { useI18n } from '../../i18n/context'
import { Notice } from '../../ui/forms'
import { ReasonDialog } from '../../ui/ReasonDialog'

/** Lets a signed-in user report a clinic or a review to the platform admins (POST /reports). */
export function ReportButton({ targetType, targetId }: { targetType: 'Clinic' | 'Review'; targetId: string }) {
  const { t } = useI18n()
  const api = useApi()
  const { me } = useSession()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  const [sent, setSent] = useState(false)

  if (sent) return <Notice>{t('report.sent')}</Notice>
  if (!me) {
    return (
      <Link className="btn btn--ghost btn--small" to={loginPath(location.pathname + location.search)}>
        {t(targetType === 'Clinic' ? 'report.clinic' : 'report.review')}
      </Link>
    )
  }
  return (
    <>
      <button type="button" className="btn btn--ghost btn--small" onClick={() => setOpen(true)}>
        {t(targetType === 'Clinic' ? 'report.clinic' : 'report.review')}
      </button>
      {open && (
        <ReasonDialog
          title={t(targetType === 'Clinic' ? 'report.clinic' : 'report.review')}
          label={t('report.reason')}
          intro={<p className="muted">{t('report.intro')}</p>}
          submitLabel={t('report.submit')}
          maxLength={500}
          minLength={5}
          onSubmit={(reason) => api.patient.report({ targetType, targetId, reason })}
          onDone={() => {
            setOpen(false)
            setSent(true)
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  )
}
