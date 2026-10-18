import { screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { patientMe } from '../fixtures'
import { meHandler, mockFetch, renderApp } from '../harness'

const INIT_DATA = 'query_id=AAE-test&user=%7B%22id%22%3A1%7D&auth_date=1760000000&hash=deadbeef'

function fakeWebApp(initData: string) {
  return {
    initData,
    initDataUnsafe: { user: { id: 1, language_code: 'ru' } },
    colorScheme: 'dark' as const,
    themeParams: { bg_color: '#101418', text_color: '#fafafa', button_color: '#2ea6ff', button_text_color: '#ffffff' },
    ready: vi.fn(),
    expand: vi.fn(),
    onEvent: vi.fn(),
    offEvent: vi.fn(),
    BackButton: { show: vi.fn(), hide: vi.fn(), onClick: vi.fn(), offClick: vi.fn() },
    MainButton: { setText: vi.fn(), show: vi.fn(), hide: vi.fn(), enable: vi.fn(), disable: vi.fn(), showProgress: vi.fn(), hideProgress: vi.fn(), onClick: vi.fn(), offClick: vi.fn() },
  }
}

afterEach(() => {
  delete window.Telegram
  document.documentElement.removeAttribute('style')
})

describe('Telegram Mini App', () => {
  it('signs in with initData, applies theme and Telegram language, and never logs initData', async () => {
    const app = fakeWebApp(INIT_DATA)
    window.Telegram = { WebApp: app }
    const logs = [vi.spyOn(console, 'log'), vi.spyOn(console, 'info'), vi.spyOn(console, 'warn'), vi.spyOn(console, 'error'), vi.spyOn(console, 'debug')]
    const fetchMock = mockFetch([
      meHandler(null),
      ['POST', '/auth/telegram/webapp', { body: patientMe }],
      ['GET', '/public/clinics', { body: { items: [], page: 1, pageSize: 20, totalCount: 0 } }],
    ])
    renderApp('/tg', fetchMock, { locale: null })

    expect(await screen.findByRole('heading', { name: 'Найдите надёжного стоматолога' })).toBeInTheDocument()
    const post = fetchMock.find('POST', '/auth/telegram/webapp')[0]!
    expect(post.body).toEqual({ initData: INIT_DATA })
    expect(post.headers['X-OqTish-Client']).toBe('tg-miniapp')
    expect(app.ready).toHaveBeenCalled()
    expect(app.expand).toHaveBeenCalled()
    expect(document.documentElement.style.getPropertyValue('--bg')).toBe('#101418')
    expect(document.documentElement.style.getPropertyValue('--accent')).toBe('#2ea6ff')
    expect(app.BackButton.hide).toHaveBeenCalled()
    // The SDK was already present, so no script tag was injected.
    expect(document.querySelector('script[src*="telegram-web-app"]')).toBeNull()
    for (const spy of logs) for (const call of spy.mock.calls) expect(JSON.stringify(call)).not.toContain('deadbeef')
  })

  it('keeps a language the user chose explicitly', async () => {
    localStorage.setItem('oqtish.locale', 'en')
    window.Telegram = { WebApp: fakeWebApp(INIT_DATA) }
    renderApp('/tg', mockFetch([meHandler(null), ['POST', '/auth/telegram/webapp', { body: patientMe }], ['GET', '/public/clinics', { body: { items: [], page: 1, pageSize: 20, totalCount: 0 } }]]), { locale: null })
    expect(await screen.findByRole('heading', { name: 'Find a dentist you can trust' })).toBeInTheDocument()
  })

  it('shows a clear notice with a web login link when opened outside Telegram', async () => {
    window.Telegram = { WebApp: fakeWebApp('') }
    const fetchMock = mockFetch([meHandler(null)])
    renderApp('/tg', fetchMock)
    expect(await screen.findByRole('alert')).toHaveTextContent('must be opened inside Telegram')
    expect(screen.getByRole('link', { name: 'Sign in on the website' })).toHaveAttribute('href', '/login')
    expect(fetchMock.find('POST', '/auth/telegram/webapp')).toHaveLength(0)
  })

  it('shows the error when Telegram data is rejected', async () => {
    window.Telegram = { WebApp: fakeWebApp(INIT_DATA) }
    const fetchMock = mockFetch([
      meHandler(null),
      ['POST', '/auth/telegram/webapp', () => new Response(JSON.stringify({ status: 401, code: 'auth.telegram_invalid' }), { status: 401, headers: { 'Content-Type': 'application/problem+json' } })],
    ])
    renderApp('/tg', fetchMock)
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Данные Telegram не подтверждены'))
  })

  it('binds the booking submit to Telegram MainButton', async () => {
    const app = fakeWebApp(INIT_DATA)
    window.Telegram = { WebApp: app }
    const { TgMainButton } = await import('../../telegram/TgMainButton')
    const { TgContext } = await import('../../telegram/tgContext')
    const { render } = await import('@testing-library/react')
    const onSubmit = vi.fn()
    const { unmount } = render(
      <TgContext.Provider value={app}>
        <TgMainButton label="Send request" pending={false} onSubmit={onSubmit} />
      </TgContext.Provider>,
    )
    expect(app.MainButton.setText).toHaveBeenCalledWith('Send request')
    expect(app.MainButton.show).toHaveBeenCalled()
    const handler = app.MainButton.onClick.mock.calls[0]![0] as () => void
    handler()
    expect(onSubmit).toHaveBeenCalledTimes(1)
    unmount()
    expect(app.MainButton.hide).toHaveBeenCalled()
    expect(app.MainButton.offClick).toHaveBeenCalledWith(handler)
  })
})
