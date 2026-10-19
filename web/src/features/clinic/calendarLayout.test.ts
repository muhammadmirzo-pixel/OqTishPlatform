import { describe, expect, it } from 'vitest'
import { layoutLanes, visibleRange } from './calendarLayout'

describe('layoutLanes', () => {
  it('keeps non-overlapping blocks full width', () => {
    expect(layoutLanes([{ start: 540, end: 570 }, { start: 570, end: 600 }])).toEqual([
      { lane: 0, lanes: 1 },
      { lane: 0, lanes: 1 },
    ])
  })

  it('puts overlapping blocks side by side and reuses freed lanes within a cluster', () => {
    // 9:00–10:00, 9:30–10:30, 10:00–10:15 (fits lane 0 again), then a separate 11:00 block.
    const lanes = layoutLanes([
      { start: 540, end: 600 },
      { start: 570, end: 630 },
      { start: 600, end: 615 },
      { start: 660, end: 690 },
    ])
    expect(lanes).toEqual([
      { lane: 0, lanes: 2 },
      { lane: 1, lanes: 2 },
      { lane: 0, lanes: 2 },
      { lane: 0, lanes: 1 },
    ])
  })

  it('returns lanes in input order even when input is unsorted', () => {
    expect(layoutLanes([{ start: 600, end: 660 }, { start: 540, end: 630 }])).toEqual([
      { lane: 1, lanes: 2 },
      { lane: 0, lanes: 2 },
    ])
  })
})

describe('visibleRange', () => {
  it('rounds out to whole hours and falls back when empty', () => {
    expect(visibleRange([{ start: 545, end: 1050 }])).toEqual({ start: 540, end: 1080 })
    expect(visibleRange([])).toEqual({ start: 480, end: 1200 })
  })
})
