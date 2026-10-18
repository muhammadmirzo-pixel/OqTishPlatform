import type { ReactNode } from 'react'
import { ApiContext } from '../api/context'
import type { Api } from '../api'
import { SessionProvider } from '../auth/SessionProvider'
import { I18nProvider } from '../i18n/I18n'
import type { Locale } from '../i18n/messages'

export function AppProviders({ api, locale, children }: { api: Api; locale?: Locale; children: ReactNode }) {
  return (
    <I18nProvider initial={locale}>
      <ApiContext.Provider value={api}>
        <SessionProvider>{children}</SessionProvider>
      </ApiContext.Provider>
    </I18nProvider>
  )
}
