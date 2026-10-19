import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react'

export interface AnchoredPosition {
  left: number
  minWidth: number
  maxWidth: number
  top?: number
  bottom?: number
  maxHeight: number
}

const GAP = 4

/**
 * Where a popup (listbox, calendar) goes next to its anchor, in viewport coordinates for
 * position: fixed. Below by default; above when there is not enough room below and more above.
 */
export function anchoredPosition(
  anchor: DOMRect,
  wantedHeight: number,
  opts: { minWidth?: number; maxWidth?: number; maxHeight?: number; /** Flip up unless the whole popup fits below. */ fitWhole?: boolean } = {},
): AnchoredPosition {
  const below = window.innerHeight - anchor.bottom - GAP - 8
  const above = anchor.top - GAP - 8
  const cap = opts.maxHeight ?? 320
  const up = below < (opts.fitWhole ? wantedHeight : Math.min(wantedHeight, 200)) && above > below
  const minWidth = Math.min(Math.max(anchor.width, opts.minWidth ?? 200), window.innerWidth - 16)
  const left = Math.max(8, Math.min(anchor.left, window.innerWidth - minWidth - 8))
  return {
    left,
    minWidth,
    maxWidth: Math.max(minWidth, Math.min(opts.maxWidth ?? 420, window.innerWidth - left - 8)),
    ...(up ? { bottom: window.innerHeight - anchor.top + GAP } : { top: anchor.bottom + GAP }),
    maxHeight: Math.max(120, Math.min(cap, up ? above : below)),
  }
}

/** While open: a press outside `inside` closes; page scroll/resize re-places the popup. */
export function useAnchoredPopup(open: boolean, inside: RefObject<HTMLElement | null>[], onClose: () => void, onMove: () => void) {
  const latest = useRef({ inside, onClose, onMove })
  useLayoutEffect(() => {
    latest.current = { inside, onClose, onMove }
  })
  useEffect(() => {
    if (!open) return
    const contains = (target: EventTarget | null) => target instanceof Node && latest.current.inside.some((r) => r.current?.contains(target))
    const onPointer = (e: PointerEvent) => {
      if (!contains(e.target)) latest.current.onClose()
    }
    const onScroll = (e: Event) => {
      if (!contains(e.target)) latest.current.onMove()
    }
    const onResize = () => latest.current.onMove()
    document.addEventListener('pointerdown', onPointer, true)
    window.addEventListener('scroll', onScroll, true)
    window.addEventListener('resize', onResize)
    return () => {
      document.removeEventListener('pointerdown', onPointer, true)
      window.removeEventListener('scroll', onScroll, true)
      window.removeEventListener('resize', onResize)
    }
  }, [open])
}
