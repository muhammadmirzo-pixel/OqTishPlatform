import { MailPlus, Pencil, UserMinus, UserPlus, Users } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { useApi } from '../../api/context'
import type { InvitationBody, MembershipScope, StaffBody, StaffMemberDto, StaffRole } from '../../api/types'
import { useSession } from '../../auth/session'
import { useAction } from '../../hooks/useAction'
import { useNow } from '../../hooks/useNow'
import { useQuery } from '../../hooks/useQuery'
import { useTitle } from '../../hooks/useTitle'
import { useI18n } from '../../i18n/context'
import { formatInstantDate } from '../../lib/format'
import { isValidUzPhone, normalizePhone } from '../../lib/phone'
import { Dialog } from '../../ui/Dialog'
import { Select } from '../../ui/Select'
import { CheckboxGroup, Field, FormError, Notice, SubmitButton } from '../../ui/forms'
import { ConfirmDialog } from '../../ui/ReasonDialog'
import { EmptyState, QueryView } from '../../ui/states'
import { useToast } from '../../ui/toastContext'
import { Badge, PageHeader } from '../../ui/widgets'
import { useClinic } from './clinicContext'
import { grantableRoles } from './permissions'

export function StaffPage() {
  const { t, locale } = useI18n()
  useTitle(t('clinicNav.staff'))
  const api = useApi()
  const { me } = useSession()
  const { clinicId, membership, branchName, timeZoneFor } = useClinic()
  const staff = useQuery(`staff:${clinicId}`, () => api.clinic.staff(clinicId))
  const invitations = useQuery(`invitations:${clinicId}`, () => api.clinic.invitations(clinicId))
  const [editing, setEditing] = useState<StaffMemberDto | null>(null)
  const [removing, setRemoving] = useState<StaffMemberDto | null>(null)
  const [inviting, setInviting] = useState(false)
  const [revoking, setRevoking] = useState<string | null>(null)
  // The invitation note explains the next step (no SMS is sent), so it stays on the page; plain saves are toasts.
  const [notice, setNotice] = useState<string | null>(null)
  const toast = useToast()
  const roles = grantableRoles(membership.role)
  const canTouch = (s: StaffMemberDto) => s.userId !== me?.id && roles.includes(s.role)
  const scopeText = (scope: MembershipScope, ids: string[]) => (scope === 'Clinic' ? t('scope.Clinic') : ids.map(branchName).join(', '))
  const tz = timeZoneFor()
  const now = useNow(60_000)

  return (
    <div className="stack">
      <PageHeader
        icon={Users}
        title={t('clinicNav.staff')}
        subtitle={t('staff.intro')}
        actions={
          <button type="button" className="btn" onClick={() => setInviting(true)}>
            <UserPlus className="icon" aria-hidden="true" />
            {t('staff.invite')}
          </button>
        }
      />
      <div aria-live="polite">{notice && <Notice tone="info">{notice}</Notice>}</div>
      <section className="stack" aria-labelledby="members-title">
        <h2 id="members-title">{t('staff.members')}</h2>
        <QueryView query={staff}>
          {(list) =>
            list.length === 0 ? (
              <EmptyState icon={Users} />
            ) : (
              <div className="table-wrap">
                <table className="table table--stack">
                  <thead>
                    <tr>
                      <th scope="col">{t('common.name')}</th>
                      <th scope="col">{t('staff.role')}</th>
                      <th scope="col">{t('staff.scope')}</th>
                      <th scope="col">{t('common.status')}</th>
                      <th scope="col">{t('common.actions')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {list.map((s) => (
                      <tr key={s.membershipId}>
                        <td data-label={t('common.name')}>
                          {s.displayName} {s.userId === me?.id && <Badge tone="info">{t('doc.you')}</Badge>}
                          {s.phoneMasked && <div className="muted small nowrap">{s.phoneMasked}</div>}
                        </td>
                        <td data-label={t('staff.role')}>{t(`role.${s.role}`)}</td>
                        <td data-label={t('staff.scope')}>{scopeText(s.scope, s.branchIds)}</td>
                        <td data-label={t('common.status')}>
                          <Badge tone={s.status === 'Active' ? 'success' : 'neutral'}>{t(`staff.${s.status}`)}</Badge>
                        </td>
                        <td data-label={t('common.actions')}>
                          {canTouch(s) && (
                            <div className="row">
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setEditing(s)}>
                                <Pencil className="icon" aria-hidden="true" />
                                {t('common.edit')}
                              </button>
                              <button type="button" className="btn btn--ghost btn--small" onClick={() => setRemoving(s)}>
                                <UserMinus className="icon" aria-hidden="true" />
                                {t('common.remove')}
                              </button>
                            </div>
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
      </section>

      <section className="stack" aria-labelledby="inv-title">
        <h2 id="inv-title">{t('staff.invitations')}</h2>
        <QueryView query={invitations}>
          {(list) =>
            list.length === 0 ? (
              <p className="muted">{t('staff.noInvitations')}</p>
            ) : (
              <ul className="list-plain stack">
                {list.map((i) => (
                  <li key={i.id} className="card row row--between">
                    <span className="row">
                      <MailPlus className="icon muted" aria-hidden="true" />
                      <strong>{i.phoneMasked}</strong> · {t(`role.${i.role}`)} · {scopeText(i.scope, i.branchIds)}
                      {new Date(i.expiresAt).getTime() <= now ? (
                        <>
                          {' '}
                          <Badge tone="warning">{t('staff.invitationExpired')}</Badge>
                        </>
                      ) : (
                        <span className="muted small"> · {t('invite.expires', { date: formatInstantDate(i.expiresAt, tz, locale) })}</span>
                      )}
                    </span>
                    <button type="button" className="btn btn--ghost btn--small" onClick={() => setRevoking(i.id)}>
                      {t('staff.revoke')}
                    </button>
                  </li>
                ))}
              </ul>
            )
          }
        </QueryView>
      </section>

      {inviting && (
        <StaffDialog
          mode="invite"
          roles={roles}
          onSubmit={(body) => api.clinic.invite(clinicId, body as InvitationBody)}
          onDone={() => {
            setInviting(false)
            setNotice(t('staff.invited'))
            invitations.reload()
          }}
          onClose={() => setInviting(false)}
        />
      )}
      {editing && (
        <StaffDialog
          mode="edit"
          member={editing}
          roles={roles}
          onSubmit={(body) => api.clinic.updateStaff(clinicId, editing.membershipId, { role: body.role, scope: body.scope, branchIds: body.branchIds })}
          onDone={() => {
            setEditing(null)
            toast({ title: t('common.saved') })
            staff.reload()
          }}
          onClose={() => setEditing(null)}
        />
      )}
      {removing && (
        <ConfirmDialog
          title={t('staff.removeTitle')}
          body={<p>{t('staff.removeBody', { name: removing.displayName })}</p>}
          confirmLabel={t('common.remove')}
          danger
          onConfirm={() => api.clinic.removeStaff(clinicId, removing.membershipId)}
          onDone={() => {
            setRemoving(null)
            toast({ title: t('common.saved') })
            staff.reload()
          }}
          onClose={() => setRemoving(null)}
        />
      )}
      {revoking && (
        <ConfirmDialog
          title={t('staff.revoke')}
          body={<p>{t('staff.revokeBody')}</p>}
          confirmLabel={t('staff.revoke')}
          onConfirm={() => api.clinic.revokeInvitation(clinicId, revoking)}
          onDone={() => {
            setRevoking(null)
            toast({ title: t('common.saved') })
            invitations.reload()
          }}
          onClose={() => setRevoking(null)}
        />
      )}
    </div>
  )
}

function StaffDialog({
  mode,
  member,
  roles,
  onSubmit,
  onDone,
  onClose,
}: {
  mode: 'invite' | 'edit'
  member?: StaffMemberDto
  roles: StaffRole[]
  onSubmit: (body: StaffBody & { phone?: string }) => Promise<unknown>
  onDone: () => void
  onClose: () => void
}) {
  const { t } = useI18n()
  const { branches, membership } = useClinic()
  // Scope containment (DECISIONS.md BE1b-3): a Branches-scoped manager grants only Branches scope
  // inside its own branches. The server re-checks (403 staff.cannot_grant_role).
  const canGrantClinicWide = membership.scope === 'Clinic'
  const [phone, setPhone] = useState('')
  const [role, setRole] = useState<StaffRole | ''>(member?.role ?? '')
  const [scope, setScope] = useState<MembershipScope>(member?.scope ?? 'Branches')
  const [branchIds, setBranchIds] = useState<string[]>(member?.branchIds ?? [])
  const [touched, setTouched] = useState(false)
  const action = useAction(onSubmit)
  const errors = {
    phone: mode === 'invite' && !isValidUzPhone(phone) ? t('valid.phone') : null,
    role: !role ? t('valid.required') : null,
    branches: scope === 'Branches' && branchIds.length === 0 ? t('valid.chooseOne') : null,
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (errors.phone || errors.role || errors.branches || !role) return
    const body = { role, scope, branchIds: scope === 'Clinic' ? [] : branchIds, ...(mode === 'invite' ? { phone: normalizePhone(phone) } : {}) }
    const r = await action.run(body)
    if (r.ok) onDone()
  }

  return (
    <Dialog title={mode === 'invite' ? t('staff.invite') : `${t('common.edit')}: ${member?.displayName ?? ''}`} onClose={onClose}>
      <form className="stack" onSubmit={submit} noValidate>
        {mode === 'invite' && (
          <Field label={t('common.phone')} hint={t('staff.phoneHint')} error={touched ? errors.phone : null}>
            {(p) => <input {...p} type="tel" placeholder="+998 90 123 45 67" value={phone} onChange={(e) => setPhone(e.target.value)} />}
          </Field>
        )}
        <Field label={t('staff.role')} hint={t('staff.roleHint')} error={touched ? errors.role : null}>
          {(p) => (
            <Select {...p} value={role} onChange={(v) => setRole(v as StaffRole)} options={[{ value: '', label: t('staff.chooseRole') }, ...roles.map((r) => ({ value: r, label: t(`role.${r}`) }))]} />
          )}
        </Field>
        <Field label={t('staff.scope')}>
          {(p) => (
            <Select
              {...p}
              value={scope}
              onChange={(v) => setScope(v as MembershipScope)}
              options={[{ value: 'Branches', label: t('scope.Branches') }, ...(canGrantClinicWide ? [{ value: 'Clinic', label: t('scope.Clinic') }] : [])]}
            />
          )}
        </Field>
        {scope === 'Branches' && (
          <CheckboxGroup legend={t('clinicNav.branches')} options={branches.map((b) => ({ id: b.id, label: b.name }))} value={branchIds} onChange={setBranchIds} error={touched ? errors.branches : null} />
        )}
        <FormError error={action.error} />
        <SubmitButton pending={action.pending}>{mode === 'invite' ? t('staff.sendInvite') : t('common.save')}</SubmitButton>
      </form>
    </Dialog>
  )
}
