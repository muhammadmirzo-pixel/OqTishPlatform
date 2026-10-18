import { Link, Navigate } from 'react-router'
import { useSession } from '../../auth/session'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { Badge, PageHeader } from '../../ui/widgets'

/** /clinic: opens the only membership directly, otherwise lists them; always offers registration. */
export function ClinicChooserPage() {
  const { t } = useI18n()
  useTitle(t('console.myClinics'))
  const { me } = useSession()
  const memberships = me?.memberships ?? []
  if (memberships.length === 1) return <Navigate replace to={`/clinic/${memberships[0]!.clinicId}`} />

  return (
    <div className="page narrow stack">
      <PageHeader title={t('console.myClinics')} />
      {memberships.length === 0 ? (
        <div className="card stack">
          <p>{t('chooser.none')}</p>
          <p className="muted small">{t('chooser.inviteHint')}</p>
          <div className="row">
            <Link className="btn" to="/clinic/register">
              {t('chooser.register')}
            </Link>
            <Link className="btn btn--ghost" to="/me/invitations">
              {t('account.tabInvitations')}
            </Link>
          </div>
        </div>
      ) : (
        <>
          <ul className="list-plain stack">
            {memberships.map((m) => (
              <li key={m.membershipId}>
                <Link className="card card--link row row--between" to={`/clinic/${m.clinicId}`}>
                  <span>
                    <strong>{m.clinicName}</strong>
                    <span className="muted"> · {t(`role.${m.role}`)}</span>
                  </span>
                  <Badge tone={m.clinicStatus === 'Approved' ? 'success' : 'warning'}>{t(`verification.${m.clinicStatus}`)}</Badge>
                </Link>
              </li>
            ))}
          </ul>
          <Link className="btn btn--ghost" to="/clinic/register">
            {t('chooser.register')}
          </Link>
        </>
      )}
    </div>
  )
}
