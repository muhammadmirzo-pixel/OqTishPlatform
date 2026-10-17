import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { I18nProvider } from './i18n/I18n'
import { ServerStatus } from './app/ServerStatus'

// Temporary entry while the full router is being built (UI skeleton in progress).
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nProvider>
      <main className="shell">
        <h1>OqTish</h1>
        <ServerStatus />
      </main>
    </I18nProvider>
  </StrictMode>,
)
