import { useState } from 'react'
import { ApiError, userMessage } from '../../api/problem'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useToast } from '../../components/toast'
import { useLoad } from '../../components/useLoad'
import { formatNumber } from '../../lib/format'
import { computeKpis, mergeChartPoints } from './kpis'
import {
  DEFAULT_PERIOD,
  GRANULARITIES,
  PRESETS,
  allowedGranularities,
  customRangeError,
  effectiveGranularity,
  resolvePeriod,
  type PeriodState,
} from './periods'
import { ProductionChart, ProductionTable } from './ProductionChart'
import {
  downloadBlob,
  fetchComparison,
  fetchProduction,
  fetchProductionCsv,
  type ComparisonResponse,
  type CsvSeparator,
  type ProductionResponse,
} from './productionApi'
import { T } from './texts'

interface Props {
  installationId: string
  /** Controlled period (e.g. kept in the URL). Without it the panel keeps its own state, so it can be embedded anywhere. */
  period?: PeriodState
  onPeriodChange?: (period: PeriodState) => void
}

type Loaded =
  | { kind: 'ok'; production: ProductionResponse; comparison: ComparisonResponse | null; comparisonFailed: boolean }
  | { kind: 'rejected'; message: string }

/** KPI cards, period selector, production chart and CSV export of one installation. */
export function ProductionPanel({ installationId, period, onPeriodChange }: Props) {
  const toast = useToast()
  const [internal, setInternal] = useState<PeriodState>(DEFAULT_PERIOD)
  const [now] = useState(() => new Date())
  const [separator, setSeparator] = useState<CsvSeparator>('semicolon')
  const [exporting, setExporting] = useState(false)
  const state = period ?? internal
  const setState = onPeriodChange ?? setInternal

  const resolved = resolvePeriod(state, now)
  const rangeError = customRangeError(state)
  const granularity = resolved ? effectiveGranularity(state, resolved.days) : 'day'
  const allowed = resolved ? allowedGranularities(resolved.days) : GRANULARITIES
  const query = resolved ? { from: resolved.from, to: resolved.to, granularity } : null

  const { data, loading, error, reload } = useLoad<Loaded | null>(
    async (signal) => {
      if (!query || rangeError) return null
      try {
        const production = await fetchProduction(installationId, query, signal)
        if (granularity === 'fiveMinutes') return { kind: 'ok', production, comparison: null, comparisonFailed: false }
        try {
          return { kind: 'ok', production, comparison: await fetchComparison(installationId, query, signal), comparisonFailed: false }
        } catch (e) {
          if (signal.aborted) throw e
          return { kind: 'ok', production, comparison: null, comparisonFailed: true }
        }
      } catch (e) {
        if (e instanceof ApiError && e.status === 400) return { kind: 'rejected', message: userMessage(e) }
        throw e
      }
    },
    JSON.stringify([installationId, query, rangeError]),
  )

  async function exportCsv() {
    if (!query) return
    setExporting(true)
    try {
      downloadBlob(await fetchProductionCsv(installationId, query, separator))
      toast.success(T.export.success)
    } catch (e) {
      toast.error(e instanceof ApiError ? userMessage(e) : T.export.error)
    } finally {
      setExporting(false)
    }
  }

  const ok = data?.kind === 'ok' ? data : null
  const kpis = ok ? computeKpis(ok.production) : null
  const points = ok ? mergeChartPoints(ok.production, ok.comparison) : []
  const hasData = (ok?.production.points.length ?? 0) > 0
  const predictionAvailable = ok?.comparison?.predictionAvailable ?? false
  const errorText = rangeError ? T.customErrors[rangeError] : undefined

  return (
    <div className="prod-panel">
      <div className="btn-row prod-controls">
        <div role="group" aria-label={T.period} className="prod-presets">
          {PRESETS.map((preset) => (
            <button
              key={preset}
              type="button"
              className="btn btn--sm"
              aria-pressed={state.preset === preset}
              onClick={() => setState({ ...state, preset })}
            >
              {T.presets[preset]}
            </button>
          ))}
        </div>
        {state.preset === 'custom' && (
          <>
            <label>
              {T.from}
              <input type="date" value={state.from} aria-invalid={Boolean(errorText)} onChange={(e) => setState({ ...state, from: e.target.value })} />
            </label>
            <label>
              {T.to}
              <input type="date" value={state.to} aria-invalid={Boolean(errorText)} onChange={(e) => setState({ ...state, to: e.target.value })} />
            </label>
          </>
        )}
        <label>
          {T.granularity}
          <select value={state.granularity} onChange={(e) => setState({ ...state, granularity: e.target.value as PeriodState['granularity'] })}>
            <option value="auto">{T.auto}</option>
            {GRANULARITIES.map((g) => (
              <option key={g} value={g} disabled={!allowed.includes(g)}>
                {T.granularities[g]}
              </option>
            ))}
          </select>
        </label>
      </div>
      {errorText && (
        <p role="alert" className="form-field__error">
          {errorText}
        </p>
      )}

      {loading && !errorText && (
        <>
          <div className="kpi-grid" aria-hidden="true">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="card kpi">
                <Skeleton lines={2} label={T.loading} />
              </div>
            ))}
          </div>
          <Skeleton lines={6} label={T.loading} />
        </>
      )}
      {error && (
        <div className="kpi__error" role="alert">
          <p>{T.error}</p>
          <button type="button" className="btn btn--sm" onClick={reload}>
            {T.retry}
          </button>
        </div>
      )}
      {data?.kind === 'rejected' && (
        <p role="alert" className="form-field__error">
          {data.message}
        </p>
      )}

      {ok && kpis && (
        <>
          <div className="kpi-grid" role="group" aria-label={T.kpiRegion}>
            <Kpi label={T.kpi.energy} value={`${formatNumber(kpis.energyKwh, 1)} ${T.units.kwh}`} />
            <Kpi label={T.kpi.peak} value={kpis.peakPowerKw === null ? '–' : `${formatNumber(kpis.peakPowerKw, 2)} ${T.units.kw}`} />
            <Kpi label={T.kpi.average} value={`${formatNumber(kpis.avgPerDayKwh, 1)} ${T.units.kwh}`} />
            <Kpi label={T.kpi.periods} value={formatNumber(kpis.periodsWithData, 0)} />
          </div>
          {!hasData ? (
            <EmptyState title={T.empty} description={T.emptyHint} />
          ) : (
            <section className="card prod-chart-card" aria-label={T.chart.title}>
              <h3 className="kpi__label">{T.chart.title}</h3>
              {granularity === 'fiveMinutes' ? (
                <p className="kpi__detail">{T.predictionUnavailableForGranularity}</p>
              ) : ok.comparisonFailed ? (
                <p className="kpi__detail">{T.comparisonFailed}</p>
              ) : (
                !predictionAvailable && <p className="kpi__detail">{T.noPrediction}</p>
              )}
              <ProductionChart
                points={points}
                from={Date.parse(ok.production.from)}
                to={Date.parse(ok.production.to)}
                granularity={ok.production.granularity}
                timeZone={ok.production.timeZone}
                predictionAvailable={predictionAvailable}
              />
              <ProductionTable
                points={points}
                granularity={ok.production.granularity}
                timeZone={ok.production.timeZone}
                predictionAvailable={predictionAvailable}
              />
            </section>
          )}
          <div className="btn-row">
            <label>
              {T.export.separator}
              <select value={separator} onChange={(e) => setSeparator(e.target.value as CsvSeparator)}>
                {(Object.keys(T.export.separators) as CsvSeparator[]).map((s) => (
                  <option key={s} value={s}>
                    {T.export.separators[s]}
                  </option>
                ))}
              </select>
            </label>
            <button type="button" className="btn btn--primary" disabled={exporting} onClick={exportCsv}>
              {exporting ? T.export.busy : T.export.button}
            </button>
          </div>
        </>
      )}
    </div>
  )
}

function Kpi({ label, value }: { label: string; value: string }) {
  return (
    <article className="card kpi">
      <h3 className="kpi__label">{label}</h3>
      <p className="kpi__value">{value}</p>
    </article>
  )
}
