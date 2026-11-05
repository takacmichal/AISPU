import { useState } from 'react'
import { formatNumber } from '../../lib/format'
import { formatBucket } from './chartMath'
import { deviationPercent, type ChartPoint } from './kpis'
import type { Granularity } from './productionApi'
import { T } from './texts'

const TABLE_LIMIT = 500

interface TableProps {
  points: ChartPoint[]
  granularity: Granularity
  timeZone: string
  predictionAvailable: boolean
}

/** Accessible table alternative; rendered only while the <details> is open (5-minute data can be large). */
export function ProductionTable({ points, granularity, timeZone, predictionAvailable }: TableProps) {
  const [open, setOpen] = useState(false)
  const shown = points.slice(0, TABLE_LIMIT)
  return (
    <details className="prod-table" onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>{T.chart.table}</summary>
      {open && (
        <div className="data-table__scroll">
          <table className="data-table">
            <caption className="sr-only">{T.chart.tableCaption}</caption>
            <thead>
              <tr>
                <th scope="col">{T.chart.colPeriod}</th>
                <th scope="col">{T.chart.colActual}</th>
                {predictionAvailable && <th scope="col">{T.chart.colPredicted}</th>}
                {predictionAvailable && <th scope="col">{T.chart.colDeviation}</th>}
                <th scope="col">{T.chart.colPeak}</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((p) => {
                const dev = deviationPercent(p)
                return (
                  <tr key={p.start}>
                    <td>{formatBucket(p.start, granularity, timeZone, true)}</td>
                    <td>{formatNumber(p.actual)}</td>
                    {predictionAvailable && <td>{formatNumber(p.predicted)}</td>}
                    {predictionAvailable && <td>{dev === null ? '–' : `${formatNumber(dev, 1)} %`}</td>}
                    <td>{formatNumber(p.peakKw)}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          {points.length > TABLE_LIMIT && <p className="kpi__detail">{T.chart.tableTruncated(TABLE_LIMIT, points.length)}</p>}
        </div>
      )}
    </details>
  )
}
