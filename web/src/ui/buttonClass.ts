export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'subtle' | 'danger'
export type ButtonSize = 'small' | 'medium' | 'large'

/** Builds the class list for buttons and button-styled links (`<Link className={buttonClass()}>`). */
export function buttonClass({ variant = 'primary', size = 'medium', block, iconOnly }: { variant?: ButtonVariant; size?: ButtonSize; block?: boolean; iconOnly?: boolean } = {}) {
  return ['btn', variant !== 'primary' && `btn--${variant}`, size !== 'medium' && `btn--${size}`, block && 'btn--block', iconOnly && 'btn--icon'].filter(Boolean).join(' ')
}
