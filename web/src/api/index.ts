import { adminApi } from './admin'
import { authApi } from './auth'
import type { ApiClient } from './client'
import { clinicApi } from './clinic'
import { financeApi } from './finance'
import { meApi } from './me'
import { patientApi } from './patient'
import { publicApi } from './public'
import { telegramApi } from './telegram'

/** Every endpoint of docs/API.md, grouped by area. */
export function createApi(client: ApiClient) {
  return {
    client,
    auth: authApi(client),
    me: meApi(client),
    public: publicApi(client),
    patient: patientApi(client),
    clinic: clinicApi(client),
    finance: financeApi(client),
    admin: adminApi(client),
    telegram: telegramApi(client),
  }
}

export type Api = ReturnType<typeof createApi>
