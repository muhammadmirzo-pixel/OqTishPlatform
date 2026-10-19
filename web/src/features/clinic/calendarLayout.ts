// Pure layout helpers for the clinic calendar (minutes since branch-local midnight).

export interface Span {
  start: number
  end: number
}

export interface Lane {
  /** 0-based lane within its overlap cluster. */
  lane: number
  /** Lanes in the cluster, so the block width is 1 / lanes of the column. */
  lanes: number
}

/**
 * Places overlapping spans side by side. Spans that overlap (directly or through a chain) form a
 * cluster; each span takes the first free lane of its cluster. Returns one entry per input span,
 * in input order.
 */
export function layoutLanes(spans: readonly Span[]): Lane[] {
  const order = spans.map((s, i) => ({ ...s, i })).sort((a, b) => a.start - b.start || a.end - b.end)
  const result: Lane[] = spans.map(() => ({ lane: 0, lanes: 1 }))
  let cluster: number[] = []
  let laneEnds: number[] = []
  let clusterEnd = -Infinity

  const close = () => {
    for (const i of cluster) result[i]!.lanes = laneEnds.length
    cluster = []
    laneEnds = []
  }

  for (const s of order) {
    if (s.start >= clusterEnd) close()
    let lane = laneEnds.findIndex((end) => end <= s.start)
    if (lane === -1) lane = laneEnds.push(s.end) - 1
    else laneEnds[lane] = s.end
    result[s.i]!.lane = lane
    cluster.push(s.i)
    clusterEnd = Math.max(clusterEnd, s.end)
  }
  close()
  return result
}

/** Visible hour range covering every span, rounded out to whole hours; `fallback` when there is nothing. */
export function visibleRange(spans: readonly Span[], fallback: Span = { start: 8 * 60, end: 20 * 60 }): Span {
  if (spans.length === 0) return fallback
  const start = Math.min(...spans.map((s) => s.start))
  const end = Math.max(...spans.map((s) => s.end))
  return { start: Math.max(0, Math.floor(start / 60) * 60), end: Math.min(24 * 60, Math.ceil(end / 60) * 60) }
}
