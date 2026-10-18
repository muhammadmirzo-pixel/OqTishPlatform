import { seg, type ApiClient } from './client'
import type { Me, MyInvitationDto, OtpChallenge, SessionDto, TelegramLink, TotpSetup } from './types'

/** docs/API.md §1 — the signed-in user's own account. */
export function meApi(c: ApiClient) {
  return {
    get: () => c.get<Me>('/me'),
    updateProfile: (fullName: string) => c.send<Me | undefined>('PUT', '/me/profile', { fullName }),
    setPassword: (body: { email?: string; currentPassword?: string; newPassword: string }) => c.send<void>('POST', '/me/password', body),
    requestPhone: (phone: string) => c.send<OtpChallenge>('POST', '/me/phone/request', { phone }),
    verifyPhone: (challengeId: string, code: string) => c.send<Me | undefined>('POST', '/me/phone/verify', { challengeId, code }),
    totpSetup: () => c.send<TotpSetup>('POST', '/me/totp/setup'),
    totpEnable: (code: string) => c.send<void>('POST', '/me/totp/enable', { code }),
    totpDisable: (code: string) => c.send<void>('POST', '/me/totp/disable', { code }),
    telegramLink: () => c.send<TelegramLink>('POST', '/me/telegram/link'),
    telegramUnlink: () => c.send<void>('DELETE', '/me/telegram'),
    sessions: () => c.get<SessionDto[]>('/me/sessions'),
    revokeSession: (id: string) => c.send<void>('DELETE', `/me/sessions/${seg(id)}`),
    invitations: () => c.get<MyInvitationDto[]>('/me/invitations'),
    acceptInvitation: (id: string) => c.send<void>('POST', `/me/invitations/${seg(id)}/accept`),
    deleteAccount: () => c.send<void>('POST', '/me/delete'),
  }
}
