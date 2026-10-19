import type { CSSProperties } from 'react'

/** Placeholder block shaped like the content it stands in for (keeps layout stable). */
export function Skeleton({ variant = 'text', width, height, className = '' }: { variant?: 'text' | 'title' | 'block' | 'circle'; width?: string | number; height?: string | number; className?: string }) {
  const style: CSSProperties = {}
  if (width !== undefined) style.width = width
  if (height !== undefined) style.height = height
  const v = variant === 'block' ? '' : ` skeleton--${variant}`
  return <span className={`skeleton${v}${className ? ` ${className}` : ''}`} style={style} aria-hidden="true" />
}

/** A list of card-shaped skeletons, announced once as "loading". */
export function SkeletonCards({ count = 6, label }: { count?: number; label: string }) {
  return (
    <div role="status" aria-live="polite">
      <span className="visually-hidden">{label}</span>
      <ul className="card-grid" aria-hidden="true">
        {Array.from({ length: count }, (_, i) => (
          <li key={i} className="card skeleton-card">
            <Skeleton variant="block" height={120} />
            <Skeleton variant="title" />
            <Skeleton width="70%" />
            <Skeleton width="45%" />
          </li>
        ))}
      </ul>
    </div>
  )
}
