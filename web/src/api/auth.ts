import type { ApiClient } from './client'
import type { Me, OtpChallenge, TelegramWidgetUser } from './types'

/** docs/API.md §1 — sign-in endpoints (anonymous). */
export function authApi(c: ApiClient) {
  return {
    requestOtp: (phone: string) => c.send<OtpChallenge>('POST', '/auth/otp/request', { phone }),
    verifyOtp: (challengeId: string, code: string, totpCode?: string) => c.send<Me>('POST', '/auth/otp/verify', { challengeId, code, ...(totpCode ? { totpCode } : {}) }),
    passwordLogin: (body: { email: string; password: string; totpCode?: string }) => c.send<Me>('POST', '/auth/password/login', body),
    requestPasswordReset: (email: string) => c.send<{ challengeId?: string } | undefined>('POST', '/auth/password/reset/request', { email }),
    confirmPasswordReset: (body: { challengeId: string; code: string; newPassword: string }) =>
      c.send<void>('POST', '/auth/password/reset/confirm', body),
    telegramWebApp: (initData: string, totpCode?: string) => c.send<Me>('POST', '/auth/telegram/webapp', { initData, ...(totpCode ? { totpCode } : {}) }),
    telegramWidget: (user: TelegramWidgetUser, totpCode?: string) => c.send<Me>('POST', '/auth/telegram/widget', { ...user, ...(totpCode ? { totpCode } : {}) }),
    logout: () => c.send<void>('POST', '/auth/logout'),
    logoutAll: () => c.send<void>('POST', '/auth/logout-all'),
  }
}
