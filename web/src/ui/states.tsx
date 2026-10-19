import { CloudOff, Compass, Inbox, ShieldAlert, ShieldCheck, type LucideIcon } from 'lucide-react'
import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { ApiError } from '../api/client'
import { errorMessage } from '../api/errors'
import { useI18n } from '../i18n/context'

function StateIcon({ icon: Icon, tone }: { icon: LucideIcon; tone?: string }) {
  return (
    <span className={`icon-tile icon-tile--lg${tone ? ` icon-tile--${tone}` : ''}`}>
      <Icon className="icon" aria-hidden="true" />
    </span>
  )
}

export function EmptyState({ title, children, icon = Inbox }: { title?: string; children?: ReactNode; icon?: LucideIcon }) {
  const { t } = useI18n()
  return (
    <div className="state state--empty">
      <StateIcon icon={icon} tone="neutral" />
      <p className="state__title">{title ?? t('state.emptyTitle')}</p>
      {children}
    </div>
  )
}

export function LoadingState({ inline = false }: { inline?: boolean }) {
  const { t } = useI18n()
  return (
    <div className={inline ? 'state state--inline' : 'state'} role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" /> {t('common.loading')}
    </div>
  )
}

export function ForbiddenState({ children }: { children?: ReactNode }) {
  const { t } = useI18n()
  return (
    <div className="state state--forbidden" role="alert">
      <StateIcon icon={ShieldAlert} tone="danger" />
      <p className="state__title">{t('state.forbiddenTitle')}</p>
      <p>{t('state.forbiddenBody')}</p>
      {children}
    </div>
  )
}

export function TotpRequiredState() {
  const { t } = useI18n()
  return (
    <div className="state state--forbidden" role="alert">
      <StateIcon icon={ShieldCheck} tone="warning" />
      <p className="state__title">{t('state.totpTitle')}</p>
      <p>{t('state.totpBody')}</p>
      <Link className="btn" to="/me/security">
        {t('state.totpLink')}
      </Link>
    </div>
  )
}

export function NotFoundState() {
  const { t } = useI18n()
  return (
    <div className="state">
      <StateIcon icon={Compass} />
      <p className="state__title">{t('state.notFoundTitle')}</p>
      <p>{t('state.notFoundBody')}</p>
      <Link className="btn" to="/">
        {t('state.toHome')}
      </Link>
    </div>
  )
}

export function NotFoundPage() {
  return (
    <div className="page narrow">
      <NotFoundState />
    </div>
  )
}

/** Picks the right full-section state for a failed load (403 / TOTP / 404 / other). */
export function ApiErrorState({ error, onRetry }: { error: unknown; onRetry?: () => void }) {
  const { t } = useI18n()
  if (error instanceof ApiError) {
    if (error.code === 'auth.totp_setup_required') return <TotpRequiredState />
    if (error.kind === 'forbidden') return <ForbiddenState />
    if (error.kind === 'notFound') return <NotFoundState />
  }
  const traceId = error instanceof ApiError ? error.traceId : undefined
  return (
    <div className="state state--error" role="alert">
      <StateIcon icon={CloudOff} tone="danger" />
      <p className="state__title">{t('state.errorTitle')}</p>
      <p>{errorMessage(t, error)}</p>
      {traceId && <p className="muted small">{t('state.errorTrace', { traceId })}</p>}
      {onRetry && (
        <button type="button" className="btn" onClick={onRetry}>
          {t('common.retry')}
        </button>
      )}
    </div>
  )
}

/** Renders loading / error / content for a query result. */
export function QueryView<T>({
  query,
  children,
}: {
  query: { data: T | undefined; error: unknown; loading: boolean; reload: () => void }
  children: (data: T) => ReactNode
}) {
  if (query.data !== undefined) return <>{children(query.data)}</>
  if (query.error !== undefined) return <ApiErrorState error={query.error} onRetry={query.reload} />
  return <LoadingState />
}
