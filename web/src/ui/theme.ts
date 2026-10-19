export type ThemeChoice = 'light' | 'dark' | 'system'

const KEY = 'oqtish.theme'

export function readTheme(): ThemeChoice {
  try {
    const v = localStorage.getItem(KEY)
    return v === 'light' || v === 'dark' ? v : 'system'
  } catch {
    return 'system'
  }
}

/** Sets html[data-theme]; "system" removes it so prefers-color-scheme decides. */
export function applyTheme(choice: ThemeChoice) {
  const root = document.documentElement
  if (choice === 'system') delete root.dataset.theme
  else root.dataset.theme = choice
}

export function saveTheme(choice: ThemeChoice) {
  applyTheme(choice)
  try {
    if (choice === 'system') localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, choice)
  } catch {
    // storage blocked: the choice still applies for this page view
  }
}
