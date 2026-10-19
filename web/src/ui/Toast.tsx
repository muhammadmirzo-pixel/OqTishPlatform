import { AlertTriangle, CheckCircle2, Info, X, XCircle } from 'lucide-react'
import { useCallback, useMemo, useRef, useState, type ReactNode } from 'react'
import { useI18n } from '../i18n/context'
import { ToastContext, type ToastInput, type ToastTone } from './toastContext'

interface ToastItem extends ToastInput {
  id: number
}

const icons: Record<ToastTone, typeof Info> = { success: CheckCircle2, error: XCircle, info: Info, warning: AlertTriangle }
const LIFETIME_MS = 5000

/**
 * Transient confirmations ("Saved", "Payment recorded"). Toasts only echo a result the server
 * already returned; they are never the only place an error is shown.
 */
export function ToastProvider({ children }: { children: ReactNode }) {
  const { t } = useI18n()
  const [items, setItems] = useState<ToastItem[]>([])
  const nextId = useRef(1)
  const timers = useRef(new Map<number, ReturnType<typeof setTimeout>>())

  const dismiss = useCallback((id: number) => {
    clearTimeout(timers.current.get(id))
    timers.current.delete(id)
    setItems((list) => list.filter((x) => x.id !== id))
  }, [])

  const arm = useCallback((id: number) => {
    timers.current.set(id, setTimeout(() => dismiss(id), LIFETIME_MS))
  }, [dismiss])

  const push = useCallback(
    (input: ToastInput) => {
      const id = nextId.current++
      setItems((list) => [...list.slice(-3), { ...input, id }])
      arm(id)
    },
    [arm],
  )

  const value = useMemo(() => ({ push }), [push])

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toasts" role="status" aria-live="polite">
        {items.map((item) => {
          const tone = item.tone ?? 'success'
          const Icon = icons[tone]
          return (
            <div
              key={item.id}
              className={`toast toast--${tone}`}
              onMouseEnter={() => clearTimeout(timers.current.get(item.id))}
              onMouseLeave={() => arm(item.id)}
            >
              <Icon className="icon toast__icon" aria-hidden="true" />
              <div className="toast__body">
                <div className="toast__title">{item.title}</div>
                {item.body && <div className="muted small">{item.body}</div>}
              </div>
              <button type="button" className="toast__close" aria-label={t('toast.close')} onClick={() => dismiss(item.id)}>
                <X className="icon" aria-hidden="true" />
              </button>
            </div>
          )
        })}
      </div>
    </ToastContext.Provider>
  )
}
