import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider, createBrowserRouter } from 'react-router'
import './index.css'
import { createApi } from './api'
import { createApiClient } from './api/client'
import { AppProviders } from './app/AppProviders'
import { routes } from './app/routes'
import { loadConfig } from './config'

async function start() {
  const config = await loadConfig()
  const client = createApiClient(config.apiBaseUrl, (input, init) => window.fetch(input, init), {
    clientKind: window.location.pathname.startsWith('/tg') ? 'tg-miniapp' : 'web',
  })
  const router = createBrowserRouter(routes)
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <AppProviders api={createApi(client)}>
        <RouterProvider router={router} />
      </AppProviders>
    </StrictMode>,
  )
}

void start()
