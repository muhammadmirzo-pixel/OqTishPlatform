import type { ReactNode } from 'react'
import { Link } from 'react-router'
import { useI18n } from '../i18n/context'

export function DemoNotice() {
  const { t } = useI18n()
  return (
    <p className="demo-notice" role="note">
      <span aria-hidden="true">⚠</span> {t('demo.notice')}
    </p>
  )
}

export function EmptyState({ title, children }: { title?: string; children?: ReactNode }) {
  const { t } = useI18n()
  return (
    <div className="state state--empty">
      <p className="state__title">{title ?? t('state.emptyTitle')}</p>
      {children}
    </div>
  )
}

export function ErrorState({ traceId, onRetry }: { traceId?: string; onRetry?: () => void }) {
  const { t } = useI18n()
  return (
    <div className="state state--error" role="alert">
      <p className="state__title">{t('state.errorTitle')}</p>
      <p>{t('state.errorBody')}</p>
      {traceId && <p className="muted">{t('state.errorTrace', { traceId })}</p>}
      {onRetry && (
        <button type="button" className="btn" onClick={onRetry}>
          {t('common.retry')}
        </button>
      )}
    </div>
  )
}

export function ForbiddenState() {
  const { t } = useI18n()
  return (
    <div className="state state--forbidden" role="alert">
      <p className="state__title">{t('state.forbiddenTitle')}</p>
      <p>{t('state.forbiddenBody')}</p>
    </div>
  )
}

export function LoadingState() {
  const { t } = useI18n()
  return (
    <div className="state" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" /> {t('common.loading')}
    </div>
  )
}

export function NotFoundPage() {
  const { t } = useI18n()
  return (
    <div className="page narrow">
      <div className="state">
        <p className="state__title">{t('state.notFoundTitle')}</p>
        <p>{t('state.notFoundBody')}</p>
        <Link className="btn" to="/">
          {t('state.toHome')}
        </Link>
      </div>
    </div>
  )
}

/** Inline banner used after a demo-only action, so nobody mistakes it for a real write. */
export function DemoActionResult({ shown }: { shown: boolean }) {
  const { t } = useI18n()
  if (!shown) return null
  return (
    <p className="demo-notice demo-notice--action" role="status">
      {t('common.demoAction')}
    </p>
  )
}
