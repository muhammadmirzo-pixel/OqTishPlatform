import { useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent, type ReactNode } from 'react'

export interface LineSeries {
  id: string
  name: string
  /** Categorical slot: 1 = --c-data-1, 2 = --c-data-2 (fixed order, never by rank). */
  slot: 1 | 2
  /** Plot positions only. Displayed values come from `formatted`, never from these numbers. */
  values: number[]
  formatted: string[]
  info?: ReactNode
}

const PLOT_HEIGHT = 200
const TOP = 12
const AXIS_BAND = 28
const LEFT = 56
const RIGHT = 24
const MIN_LABEL_GAP = 56

/** 0 → a clean upper bound split into ~4 steps of 1/2/2.5/5 × 10ⁿ. */
function niceTicks(max: number): number[] {
  if (max <= 0) return [0]
  const raw = max / 4
  const mag = 10 ** Math.floor(Math.log10(raw))
  const norm = raw / mag
  const step = (norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 2.5 ? 2.5 : norm <= 5 ? 5 : 10) * mag
  const top = Math.ceil(max / step) * step
  const ticks: number[] = []
  for (let v = 0; v <= top + step / 2; v += step) ticks.push(v)
  return ticks
}

function useWidth<T extends HTMLElement>(fallback: number) {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(fallback)
  useEffect(() => {
    const el = ref.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => entry && setWidth(Math.round(entry.contentRect.width)))
    observer.observe(el)
    return () => observer.disconnect()
  }, [])
  return [ref, width] as const
}

/**
 * Multi-series line chart on one value axis (inline SVG, no library). Hover shows a crosshair and
 * one tooltip listing every series; the same readout is reachable with ← → when focused. Values are
 * never gated behind the tooltip — callers render a table view next to the chart.
 */
export function LineChart({
  title,
  hint,
  labels,
  tipLabels,
  series,
  formatTick,
}: {
  title: string
  hint: string
  /** Short x-axis label per point. */
  labels: string[]
  /** Full label per point for the tooltip. */
  tipLabels: string[]
  series: LineSeries[]
  formatTick: (value: number) => string
}) {
  const [boxRef, width] = useWidth<HTMLDivElement>(640)
  const [active, setActive] = useState<number | null>(null)
  const [announce, setAnnounce] = useState('')
  const tipRef = useRef<HTMLDivElement>(null)
  const [tipWidth, setTipWidth] = useState(200)
  useLayoutEffect(() => {
    if (active !== null && tipRef.current) setTipWidth(tipRef.current.offsetWidth)
  }, [active])
  const hintId = useId()
  const count = labels.length
  const plotWidth = Math.max(width - LEFT - RIGHT, 40)
  const ticks = niceTicks(Math.max(0, ...series.flatMap((s) => s.values)))
  const top = ticks[ticks.length - 1] || 1
  const x = (i: number) => LEFT + (count > 1 ? (i / (count - 1)) * plotWidth : plotWidth / 2)
  const y = (v: number) => TOP + PLOT_HEIGHT - (v / top) * PLOT_HEIGHT
  const every = Math.max(1, Math.ceil(MIN_LABEL_GAP / (plotWidth / Math.max(count - 1, 1))))
  const height = TOP + PLOT_HEIGHT + AXIS_BAND

  function readout(i: number) {
    return [tipLabels[i], ...series.map((s) => `${s.name}: ${s.formatted[i]}`)].join('; ')
  }

  function onPointer(e: PointerEvent<SVGRectElement>) {
    const box = e.currentTarget.getBoundingClientRect()
    const ratio = (e.clientX - box.left) / Math.max(box.width, 1)
    setActive(Math.min(count - 1, Math.max(0, Math.round(ratio * (count - 1)))))
  }

  function onKey(e: KeyboardEvent<HTMLDivElement>) {
    let next: number | null = null
    if (e.key === 'ArrowRight') next = active === null ? count - 1 : Math.min(count - 1, active + 1)
    else if (e.key === 'ArrowLeft') next = active === null ? count - 1 : Math.max(0, active - 1)
    else if (e.key === 'Home') next = 0
    else if (e.key === 'End') next = count - 1
    else if (e.key === 'Escape') {
      setActive(null)
      return
    } else return
    e.preventDefault()
    setActive(next)
    setAnnounce(readout(next))
  }

  // Beside the crosshair (right, else left), always kept inside the chart box.
  const cross = active === null ? 0 : x(active)
  const preferred = cross + 12 + tipWidth <= width ? cross + 12 : cross - 12 - tipWidth
  const tipLeft = Math.max(0, Math.min(preferred, width - tipWidth))

  return (
    <div className="chart-block">
      <ul className="chart-legend">
        {series.map((s) => (
          <li key={s.id}>
            <span className={`chart__key chart__key--${s.slot}`} aria-hidden="true" />
            {s.name}
            {s.info}
          </li>
        ))}
      </ul>
      <div
        ref={boxRef}
        className="chart"
        role="group"
        aria-label={title}
        aria-describedby={hintId}
        tabIndex={0}
        onKeyDown={onKey}
        onBlur={() => setActive(null)}
      >
        <p id={hintId} className="visually-hidden">
          {hint}
        </p>
        <svg width={width} height={height} aria-hidden="true" focusable="false">
          {ticks.map((t) => (
            <g key={t}>
              <line className="chart__grid" x1={LEFT} x2={LEFT + plotWidth} y1={y(t)} y2={y(t)} />
              <text className="chart__tick" x={LEFT - 8} y={y(t)} dy="0.32em" textAnchor="end">
                {formatTick(t)}
              </text>
            </g>
          ))}
          {labels.map((label, i) =>
            (count - 1 - i) % every === 0 ? (
              <text key={i} className="chart__tick" x={x(i)} y={TOP + PLOT_HEIGHT + 18} textAnchor="middle">
                {label}
              </text>
            ) : null,
          )}
          {active !== null && <line className="chart__cross" x1={x(active)} x2={x(active)} y1={TOP} y2={TOP + PLOT_HEIGHT} />}
          {series.map((s) => (
            <path key={s.id} className={`chart__line chart__line--${s.slot}`} d={s.values.map((v, i) => `${i ? 'L' : 'M'}${x(i)},${y(v)}`).join(' ')} />
          ))}
          {series.map((s) => {
            const i = active ?? count - 1
            const v = s.values[i]
            return v === undefined ? null : <circle key={s.id} className={`chart__dot chart__dot--${s.slot}`} cx={x(i)} cy={y(v)} r={4} />
          })}
          <rect
            className="chart__hit"
            x={LEFT}
            y={TOP}
            width={plotWidth}
            height={PLOT_HEIGHT}
            onPointerMove={onPointer}
            onPointerDown={onPointer}
            onPointerLeave={() => setActive(null)}
          />
        </svg>
        {active !== null && (
          <div ref={tipRef} className="chart__tip" style={{ left: tipLeft }} aria-hidden="true">
            <p className="chart__tip-title">{tipLabels[active]}</p>
            {series.map((s) => (
              <p key={s.id} className="chart__tip-row">
                <span className={`chart__key chart__key--${s.slot}`} />
                <strong>{s.formatted[active]}</strong>
                <span>{s.name}</span>
              </p>
            ))}
          </div>
        )}
        <span className="visually-hidden" aria-live="polite">
          {announce}
        </span>
      </div>
    </div>
  )
}
