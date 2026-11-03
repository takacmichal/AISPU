import { useState } from 'react'
import type { components } from '../../api/generated/schema'
import { formatCurrency, formatDateTime, formatKwp, formatNumber } from '../../lib/format'
import { TEXT } from './labels'
import { formatKw, formatKwh } from './units'

type Version = components['schemas']['FveDesignVersionResponse']

/** Latest version: configuration and estimated yearly production. */
export function VersionResult({ version }: { version: Version }) {
  return (
    <div className="card">
      <p>
        Verzia {formatNumber(version.versionNumber, 0)} · {formatDateTime(version.createdAt)}
      </p>
      <dl className="design-metrics">
        <div className="design-metric--lead">
          <dt>{TEXT.annualYield}</dt>
          <dd>{formatKwh(version.annualYieldKwh)}</dd>
        </div>
        <div>
          <dt>{TEXT.dcPower}</dt>
          <dd>{formatKwp(version.dcPowerKwp)}</dd>
        </div>
        <div>
          <dt>{TEXT.acPower}</dt>
          <dd>{formatKw(version.acPowerKw)}</dd>
        </div>
        <div>
          <dt>{TEXT.ratio}</dt>
          <dd>{formatNumber(version.dcAcRatio)}</dd>
        </div>
        <div>
          <dt>{TEXT.specificYield}</dt>
          <dd>{formatNumber(version.specificYieldKwhPerKwp, 0)}&nbsp;kWh/kWp</dd>
        </div>
      </dl>
      <p>
        {TEXT.panels}: {formatNumber(version.panelCount, 0)} × {version.panelName} ({formatNumber(version.panelPowerW, 0)} W,{' '}
        {formatCurrency(version.panelUnitPrice)})
      </p>
      <p>
        {TEXT.inverter}: {version.inverterName} ({formatNumber(version.inverterPowerW, 0)} W, {formatCurrency(version.inverterUnitPrice)})
      </p>
      <p>
        {TEXT.source}: {version.estimateSource}
      </p>
      {version.warnings.length > 0 && (
        <ul aria-label={TEXT.warnings}>
          {version.warnings.map((w) => (
            <li key={w}>{w}</li>
          ))}
        </ul>
      )}
    </div>
  )
}

const COMPARE_ROWS: { label: string; value: (v: Version) => string }[] = [
  { label: TEXT.panel, value: (v) => `${formatNumber(v.panelCount, 0)} × ${v.panelName}` },
  { label: TEXT.inverter, value: (v) => v.inverterName },
  { label: TEXT.dcPower, value: (v) => formatKwp(v.dcPowerKwp) },
  { label: TEXT.acPower, value: (v) => formatKw(v.acPowerKw) },
  { label: TEXT.ratio, value: (v) => formatNumber(v.dcAcRatio) },
  { label: TEXT.annualYield, value: (v) => formatKwh(v.annualYieldKwh) },
  { label: TEXT.specificYield, value: (v) => `${formatNumber(v.specificYieldKwhPerKwp, 0)} kWh/kWp` },
  { label: 'Cena komponentov', value: (v) => formatCurrency(Number(v.panelUnitPrice) * Number(v.panelCount) + Number(v.inverterUnitPrice)) },
]

/** Version history (newest first) with a side by side comparison of two selected versions. */
export function VersionHistory({ versions }: { versions: Version[] }) {
  const [selected, setSelected] = useState<number[]>([])
  const toggle = (n: number) =>
    setSelected((current) => (current.includes(n) ? current.filter((x) => x !== n) : [...current, n].slice(-2)))
  const [a, b] = selected.map((n) => versions.find((v) => Number(v.versionNumber) === n)).sort((x, y) => Number(x?.versionNumber) - Number(y?.versionNumber))

  return (
    <div>
      <div className="data-table__scroll">
        <table>
          <thead>
            <tr>
              <th scope="col">{TEXT.compare}</th>
              <th scope="col">Verzia</th>
              <th scope="col">Vytvorená</th>
              <th scope="col">Výkon DC</th>
              <th scope="col">Ročná výroba</th>
            </tr>
          </thead>
          <tbody>
            {versions.map((v) => {
              const n = Number(v.versionNumber)
              return (
                <tr key={v.id}>
                  <td>
                    <input
                      type="checkbox"
                      aria-label={`${TEXT.select} ${n}`}
                      checked={selected.includes(n)}
                      onChange={() => toggle(n)}
                    />
                  </td>
                  <td>{n}</td>
                  <td>{formatDateTime(v.createdAt)}</td>
                  <td>{formatKwp(v.dcPowerKwp)}</td>
                  <td>{formatKwh(v.annualYieldKwh)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {versions.length > 1 && selected.length < 2 && <p>{TEXT.compareHint}</p>}
      {a && b && (
        <div className="data-table__scroll design-section">
          <table className="design-compare" aria-label={TEXT.compareTitle}>
            <thead>
              <tr>
                <th scope="col">{TEXT.compareTitle}</th>
                <th scope="col">Verzia {formatNumber(a.versionNumber, 0)}</th>
                <th scope="col">Verzia {formatNumber(b.versionNumber, 0)}</th>
              </tr>
            </thead>
            <tbody>
              {COMPARE_ROWS.map((row) => {
                const left = row.value(a)
                const right = row.value(b)
                return (
                  <tr key={row.label}>
                    <th scope="row">{row.label}</th>
                    <td className={left !== right ? 'is-diff' : undefined}>{left}</td>
                    <td className={left !== right ? 'is-diff' : undefined}>{right}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
