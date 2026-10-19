import type { ButtonHTMLAttributes, ReactNode } from 'react'
import type { LucideIcon } from 'lucide-react'
import { buttonClass, type ButtonSize, type ButtonVariant } from './buttonClass'


export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  loading?: boolean
  icon?: LucideIcon
  block?: boolean
  children?: ReactNode
}

/** Button with variants and a loading state that blocks repeat clicks (no double submissions). */
export function Button({ variant, size, loading = false, icon: Icon, block, children, disabled, type = 'button', className, ...rest }: ButtonProps) {
  const cls = buttonClass({ variant, size, block, iconOnly: !children })
  return (
    <button type={type} className={className ? `${cls} ${className}` : cls} disabled={disabled || loading} aria-busy={loading || undefined} {...rest}>
      {loading ? <span className="btn__spinner" aria-hidden="true" /> : Icon && <Icon className="icon" aria-hidden="true" />}
      {children}
    </button>
  )
}
