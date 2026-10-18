import { useState } from 'react'
import { Link } from 'react-router'
import { useApi } from '../../api/context'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate } from '../../lib/format'
import { DEFAULT_TIME_ZONE } from '../../lib/time'
import { FormError, Notice } from '../../ui/forms'
import { EmptyState, QueryView } from '../../ui/states'

export function InvitationsPage() {
  const { t, locale } = useI18n()
  useTitle(t('account.tabInvitations'))
  const api = useApi()
  const { me, refresh } = useSession()
  const invitations = useQuery('my-invitations', () => api.me.invitations())
  const [accepted, setAccepted] = useState<{ clinicId: string; clinicName: string } | null>(null)
  const accept = useAction((id: string) => api.me.acceptInvitation(id))

  return (
    <div className="stack">
      {!me?.phoneVerified && (
        <p className="alert alert--warning">
          {t('invite.needPhone')} <Link to="/me">{t('account.tabProfile')}</Link>
        </p>
      )}
      {accepted && (
        <Notice>
          {t('invite.accepted', { clinic: accepted.clinicName })} <Link to={`/clinic/${accepted.clinicId}`}>{t('invite.openConsole')}</Link>
        </Notice>
      )}
      <FormError error={accept.error} />
      <QueryView query={invitations}>
        {(items) =>
          items.length === 0 ? (
            <EmptyState title={t('invite.none')} />
          ) : (
            <ul className="list-plain stack">
              {items.map((inv) => (
                <li key={inv.id} className="card row row--between">
                  <div>
                    <strong>{inv.clinicName}</strong>
                    <div>
                      {t(`role.${inv.role}`)} · {inv.scope === 'Clinic' ? t('scope.Clinic') : inv.branchNames.join(', ')}
                    </div>
                    <div className="muted small">{t('invite.expires', { date: formatInstantDate(inv.expiresAt, DEFAULT_TIME_ZONE, locale) })}</div>
                  </div>
                  <button
                    type="button"
                    className="btn"
                    disabled={accept.pending}
                    onClick={async () => {
                      const r = await accept.run(inv.id)
                      if (r.ok) {
                        setAccepted({ clinicId: inv.clinicId, clinicName: inv.clinicName })
                        invitations.reload()
                        await refresh()
                      }
                    }}
                  >
                    {t('invite.accept')}
                  </button>
                </li>
              ))}
            </ul>
          )
        }
      </QueryView>
    </div>
  )
}
