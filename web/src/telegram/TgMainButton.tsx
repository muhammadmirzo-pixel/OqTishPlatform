import { useEffect, useLayoutEffect, useRef } from 'react'
import type { SubmitControlProps } from '../features/booking/BookingWizard'
import { useTelegram } from './tgContext'

/** Binds Telegram's native MainButton; falls back to an in-page button when unavailable. */
export function TgMainButton({ label, pending, onSubmit }: SubmitControlProps) {
  const app = useTelegram()
  const button = app?.MainButton
  const submitRef = useRef(onSubmit)
  useLayoutEffect(() => {
    submitRef.current = onSubmit
  })

  useEffect(() => {
    if (!button) return
    const handler = () => submitRef.current()
    button.setText(label)
    button.show()
    button.onClick(handler)
    return () => {
      button.offClick(handler)
      button.hide()
    }
  }, [button, label])

  useEffect(() => {
    if (!button) return
    if (pending) {
      button.showProgress(false)
      button.disable()
    } else {
      button.hideProgress()
      button.enable()
    }
  }, [button, pending])

  if (button) return null
  return (
    <button type="button" className="btn" disabled={pending} onClick={onSubmit}>
      {label}
    </button>
  )
}
