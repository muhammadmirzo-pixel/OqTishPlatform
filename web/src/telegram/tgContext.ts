import { createContext, useContext } from 'react'
import type { TelegramWebApp } from './webapp'

export const TgContext = createContext<TelegramWebApp | null>(null)

export function useTelegram(): TelegramWebApp | null {
  return useContext(TgContext)
}
