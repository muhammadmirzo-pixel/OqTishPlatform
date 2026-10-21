import { useState, type CSSProperties } from 'react'
import { useImageUrl } from '../api/context'
import { initials } from '../lib/initials'

const palettes: [string, string][] = [
  ['#2563eb', '#10b981'],
  ['#1d4ed8', '#8b5cf6'],
  ['#0ea5e9', '#2563eb'],
  ['#10b981', '#0ea5e9'],
  ['#7c3aed', '#2563eb'],
  ['#0f766e', '#22c55e'],
]

function hash(s: string) {
  let h = 0
  for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) | 0
  return Math.abs(h)
}

/**
 * Clinic logo / profile photo when `src` is set, otherwise initials on a gradient. Colour is derived
 * from the id so the same clinic always looks the same; a broken image falls back to the initials.
 */
export function Monogram({
  name,
  seed,
  size = 56,
  imageId,
  round,
  className = '',
}: {
  name: string
  seed?: string
  size?: number
  /** Uploaded image id (clinic logo, profile photo). */
  imageId?: string | null
  round?: boolean
  className?: string
}) {
  const src = useImageUrl(imageId)
  const [a, b] = palettes[hash(seed ?? name) % palettes.length] ?? ['#2563eb', '#10b981']
  const [failed, setFailed] = useState<string | null>(null)
  const style = { width: size, height: size, fontSize: size * 0.36, '--mono-a': a, '--mono-b': b } as CSSProperties
  const photo = src && failed !== src
  return (
    <span className={`monogram${photo ? ' monogram--photo' : ''}${round ? ' monogram--round' : ''}${className ? ` ${className}` : ''}`} style={style} aria-hidden="true">
      {photo ? <img src={src} alt="" loading="lazy" decoding="async" onError={() => setFailed(src)} /> : initials(name)}
    </span>
  )
}
