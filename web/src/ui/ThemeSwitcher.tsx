import { Monitor, Moon, Sun } from 'lucide-react'
import { useState } from 'react'
import { useI18n } from '../i18n/context'
import { readTheme, saveTheme, type ThemeChoice } from './theme'

const order: ThemeChoice[] = ['light', 'dark', 'system']
const icons = { light: Sun, dark: Moon, system: Monitor }

/** One button cycling light → dark → system; the label names the current choice. */
export function ThemeSwitcher({ className = 'btn btn--ghost btn--small btn--icon' }: { className?: string }) {
  const { t } = useI18n()
  const [choice, setChoice] = useState<ThemeChoice>(readTheme)
  const Icon = icons[choice]
  const label = `${t('theme.label')}: ${t(`theme.${choice}`)}`
  return (
    <button
      type="button"
      className={className}
      aria-label={label}
      title={label}
      onClick={() => {
        const next = order[(order.indexOf(choice) + 1) % order.length] ?? 'system'
        saveTheme(next)
        setChoice(next)
      }}
    >
      <Icon className="icon" aria-hidden="true" />
    </button>
  )
}
