/** Pure helpers for the production charts (no React), unit-tested in charts.test.tsx. */

export interface SeriesPoint {
  /** X axis label, e.g. "10:00" or "4. 11.". */
  label: string
  actual: number | null
  predicted: number | null
}

export interface UnderRange {
  /** Inclusive point indexes. */
  start: number
  end: number
  /** Sum of actual / sum of predicted in the range, rounded to 2 decimals. */
  ratio: number
}

/** Below this share of the prediction a point counts as underproduction (ADR #194 uses 85 %). */
export const DEFAULT_UNDER_RATIO = 0.85

function isUnder(point: SeriesPoint, ratio: number): boolean {
  return point.actual !== null && point.predicted !== null && point.predicted > 0 && point.actual < ratio * point.predicted
}

/** Runs of consecutive underproducing points that are at least `minPoints` long. */
export function underRanges(points: readonly SeriesPoint[], ratio = DEFAULT_UNDER_RATIO, minPoints = 1): UnderRange[] {
  const ranges: UnderRange[] = []
  let start = -1
  const close = (end: number) => {
    if (start < 0) return
    if (end - start + 1 >= minPoints) {
      const slice = points.slice(start, end + 1)
      const actual = slice.reduce((sum, p) => sum + (p.actual ?? 0), 0)
      const predicted = slice.reduce((sum, p) => sum + (p.predicted ?? 0), 0)
      ranges.push({ start, end, ratio: Math.round((actual / predicted) * 100) / 100 })
    }
    start = -1
  }
  points.forEach((point, index) => {
    if (isUnder(point, ratio)) {
      if (start < 0) start = index
    } else {
      close(index - 1)
    }
  })
  close(points.length - 1)
  return ranges
}

/** Round the axis maximum up to a readable value (1, 1.5, 2, 3, 4, 5, 6, 8 or 10 × 10^n) without much empty space. */
export function niceMax(value: number): number {
  if (value <= 0) return 1
  const exp = 10 ** Math.floor(Math.log10(value))
  const step = [1, 1.5, 2, 3, 4, 5, 6, 8, 10].find((s) => s * exp >= value) ?? 10
  return step * exp
}

/** SVG path through the defined points; a null value breaks the line into separate segments. */
export function linePath(values: readonly (number | null)[], x: (i: number) => number, y: (v: number) => number): string {
  let path = ''
  let open = false
  values.forEach((value, i) => {
    if (value === null) {
      open = false
      return
    }
    path += `${open ? 'L' : 'M'}${x(i).toFixed(1)} ${y(value).toFixed(1)} `
    open = true
  })
  return path.trim()
}

/** Closed area paths (one per segment without nulls) down to the baseline. */
export function areaPaths(values: readonly (number | null)[], x: (i: number) => number, y: (v: number) => number, baseline: number): string[] {
  const segments: number[][] = []
  let current: number[] = []
  values.forEach((value, i) => {
    if (value === null) {
      if (current.length) segments.push(current)
      current = []
    } else current.push(i)
  })
  if (current.length) segments.push(current)
  return segments
    .filter((s) => s.length > 1)
    .map((s) => {
      const top = s.map((i, k) => `${k === 0 ? 'M' : 'L'}${x(i).toFixed(1)} ${y(values[i] as number).toFixed(1)}`).join(' ')
      return `${top} L${x(s[s.length - 1]).toFixed(1)} ${baseline} L${x(s[0]).toFixed(1)} ${baseline} Z`
    })
}
