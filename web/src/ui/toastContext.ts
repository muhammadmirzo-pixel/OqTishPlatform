import { createContext, useContext, type ReactNode } from 'react'

export type ToastTone = 'success' | 'error' | 'info' | 'warning'
export interface ToastInput {
  title: ReactNode
  body?: ReactNode
  tone?: ToastTone
}

export const ToastContext = createContext<{ push: (toast: ToastInput) => void }>({ push: () => undefined })

/** `const toast = useToast(); toast({ title: t('common.saved') })` */
export function useToast() {
  return useContext(ToastContext).push
}
