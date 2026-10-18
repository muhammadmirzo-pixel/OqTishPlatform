import type { TelegramWidgetUser } from '../api/types'

/** Reads a widget payload from redirect-mode query parameters; null when incomplete. */
export function widgetUserFromParams(params: URLSearchParams): TelegramWidgetUser | null {
  const id = Number(params.get('id'))
  const authDate = Number(params.get('auth_date'))
  const hash = params.get('hash')
  const firstName = params.get('first_name')
  if (!Number.isSafeInteger(id) || !Number.isSafeInteger(authDate) || !hash || !firstName) return null
  const user: TelegramWidgetUser = { id, first_name: firstName, auth_date: authDate, hash }
  const lastName = params.get('last_name')
  const username = params.get('username')
  const photo = params.get('photo_url')
  if (lastName) user.last_name = lastName
  if (username) user.username = username
  if (photo) user.photo_url = photo
  return user
}
