import { useNow } from '../hooks/useNow'
import { remaining } from '../lib/format'

/**
 * Ring that empties until a server deadline (Pending requests: 30 minutes). Informational only:
 * reaching zero never changes the appointment status — the next server response does.
 */
export function CountdownRing({ deadline, totalMs, size = 72, expiredText }: { deadline: string; totalMs: number; size?: number; expiredText: string }) {
  const now = useNow()
  const leftMs = Math.max(0, new Date(deadline).getTime() - now)
  const left = remaining(deadline, now)
  const stroke = Math.max(4, size / 14)
  const r = (size - stroke) / 2
  const c = 2 * Math.PI * r
  const fraction = totalMs > 0 ? Math.min(1, leftMs / totalMs) : 0
  return (
    <span className="ring" style={{ width: size, height: size }} aria-hidden="true">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`}>
        <circle className="ring__track" cx={size / 2} cy={size / 2} r={r} fill="none" strokeWidth={stroke} />
        <circle className="ring__fill" cx={size / 2} cy={size / 2} r={r} fill="none" strokeWidth={stroke} strokeLinecap="round" strokeDasharray={c} strokeDashoffset={c * (1 - fraction)} />
      </svg>
      <span className="ring__label">{left ?? expiredText}</span>
    </span>
  )
}
