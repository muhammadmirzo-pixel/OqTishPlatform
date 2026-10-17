import { useEffect, useState } from 'react'

type ServiceStatus = 'checking' | 'ready' | 'unavailable'

const label: Record<ServiceStatus, string> = { checking: '…', ready: 'API ✓', unavailable: 'API ✕' }

/** Small footer indicator backed by the API readiness probe (proves SPA → proxy → API → DB). */
export function ServerStatus({ fetchImpl = fetch }: { fetchImpl?: typeof fetch }) {
  const [status, setStatus] = useState<ServiceStatus>('checking')

  useEffect(() => {
    const controller = new AbortController()
    fetchImpl('/health/ready', { signal: controller.signal })
      .then((response) => setStatus(response.ok ? 'ready' : 'unavailable'))
      .catch(() => {
        if (!controller.signal.aborted) setStatus('unavailable')
      })
    return () => controller.abort()
  }, [fetchImpl])

  return (
    <span role="status" className={`server-status server-status--${status}`} data-status={status}>
      {label[status]}
    </span>
  )
}
