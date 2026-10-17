// Environment-specific settings are read at runtime from /config.json (written by the web container
// at startup), so one built image serves every environment. VITE_API_BASE_URL is the dev fallback.

export interface AppConfig {
  apiBaseUrl: string
}

const fallback: AppConfig = {
  apiBaseUrl: import.meta.env.VITE_API_BASE_URL ?? '/api',
}

export async function loadConfig(fetchImpl: typeof fetch = fetch): Promise<AppConfig> {
  try {
    const response = await fetchImpl('/config.json', { cache: 'no-store' })
    if (!response.ok) return fallback
    const data = (await response.json()) as Partial<AppConfig>
    return {
      apiBaseUrl: typeof data.apiBaseUrl === 'string' && data.apiBaseUrl.length > 0 ? data.apiBaseUrl : fallback.apiBaseUrl,
    }
  } catch {
    return fallback
  }
}
