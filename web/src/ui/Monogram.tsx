import type { CSSProperties } from 'react'
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
 * Stand-in for a clinic photo/logo (the API has none yet — docs/FRONTEND_PHASE_A.md D1).
 * Colour is derived from the id so the same clinic always looks the same.
 */
export function Monogram({ name, seed, size = 56, className = '' }: { name: string; seed?: string; size?: number; className?: string }) {
  const [a, b] = palettes[hash(seed ?? name) % palettes.length] ?? ['#2563eb', '#10b981']
  const style = { width: size, height: size, fontSize: size * 0.36, '--mono-a': a, '--mono-b': b } as CSSProperties
  return (
    <span className={`monogram${className ? ` ${className}` : ''}`} style={style} aria-hidden="true">
      {initials(name)}
    </span>
  )
}
