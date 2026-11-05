import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { ProductionAreaChart } from './ProductionAreaChart'
import { niceMax, underRanges, type SeriesPoint } from './chartGeometry'

const p = (label: string, actual: number | null, predicted: number | null): SeriesPoint => ({ label, actual, predicted })

describe('underRanges', () => {
  it('finds consecutive points below the ratio of the prediction', () => {
    const points = [p('10', 5, 5), p('11', 3, 5), p('12', 3.5, 5), p('13', 5, 5), p('14', 1, 5)]
    expect(underRanges(points, 0.85)).toEqual([
      { start: 1, end: 2, ratio: 0.65 },
      { start: 4, end: 4, ratio: 0.2 },
    ])
  })

  it('respects the minimum length and ignores missing or zero predictions', () => {
    const points = [p('10', 1, 5), p('11', null, 5), p('12', 0, 0), p('13', 1, 5), p('14', 1, 5)]
    expect(underRanges(points, 0.85, 2)).toEqual([{ start: 3, end: 4, ratio: 0.2 }])
  })
})

describe('niceMax', () => {
  it('rounds the axis up without much empty space', () => {
    expect(niceMax(0)).toBe(1)
    expect(niceMax(6.2)).toBe(8)
    expect(niceMax(31.4)).toBe(40)
    expect(niceMax(1284)).toBe(1500)
  })
})

describe('ProductionAreaChart', () => {
  const points = [p('6:00', 0, 0), p('10:00', 4, 4.2), p('12:00', 3, 6), p('14:00', 3.2, 6), p('18:00', 2, 2.1)]

  it('is an image with a text summary and a table alternative', () => {
    render(<ProductionAreaChart title="Výroba dnes" unit="kW" points={points} summary="Výroba 3 h pod predikciou." />)
    expect(screen.getByRole('img', { name: 'Výroba dnes' })).toHaveAccessibleDescription('Výroba 3 h pod predikciou.')
    const table = screen.getByRole('table', { name: 'Výroba dnes' })
    expect(within(table).getAllByRole('row')).toHaveLength(points.length + 1)
  })

  it('shows the underproduction legend only when there is a range', () => {
    const { rerender } = render(<ProductionAreaChart title="Výroba" unit="kW" points={points} />)
    expect(screen.getByText('Podvýroba')).toBeInTheDocument()
    rerender(<ProductionAreaChart title="Výroba" unit="kW" points={points.map((x) => ({ ...x, actual: x.predicted }))} />)
    expect(screen.queryByText('Podvýroba')).not.toBeInTheDocument()
  })

  it('hides the highlight and its legend when showUnder is off', () => {
    render(<ProductionAreaChart title="Výroba" unit="kW" points={points} showUnder={false} />)
    expect(screen.queryByText('Podvýroba')).not.toBeInTheDocument()
  })

  it('moves a cursor with the keyboard and announces the point in interactive mode', async () => {
    render(
      <ProductionAreaChart
        title="Výroba"
        unit="kW"
        points={points}
        table={false}
        interactive={{ label: 'Graf výroby', describe: (i) => [points[i].label, `Skutočná: ${points[i].actual}`] }}
      />,
    )
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    const plot = screen.getByRole('group', { name: 'Graf výroby' })
    plot.focus()
    await userEvent.keyboard('{End}')
    expect(screen.getAllByText('18:00').length).toBeGreaterThan(1)
    await userEvent.keyboard('{ArrowLeft}')
    expect(screen.getByText('14:00. Skutočná: 3.2')).toBeInTheDocument()
    await userEvent.keyboard('{Escape}')
    expect(screen.queryByText('14:00. Skutočná: 3.2')).not.toBeInTheDocument()
  })
})
