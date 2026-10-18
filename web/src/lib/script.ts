// Loads a third-party script once, on demand (Telegram scripts are only loaded where needed).

const pending = new Map<string, Promise<void>>()

export function loadScript(src: string, attributes: Record<string, string> = {}, timeoutMs = 10000): Promise<void> {
  const existing = pending.get(src)
  if (existing) return existing
  const promise = new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = src
    script.async = true
    for (const [k, v] of Object.entries(attributes)) script.setAttribute(k, v)
    const timer = setTimeout(() => {
      pending.delete(src)
      reject(new Error('script timeout'))
    }, timeoutMs)
    script.onload = () => {
      clearTimeout(timer)
      resolve()
    }
    script.onerror = () => {
      clearTimeout(timer)
      pending.delete(src)
      script.remove()
      reject(new Error('script failed'))
    }
    document.head.appendChild(script)
  })
  pending.set(src, promise)
  return promise
}
