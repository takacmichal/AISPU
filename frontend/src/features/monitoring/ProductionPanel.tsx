import { useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, userMessage } from '../../api/problem'
import { ProductionAreaChart } from '../../components/charts/ProductionAreaChart'
import { EmptyState } from '../../components/EmptyState'
import { KpiCard } from '../../components/KpiCard'
import { SegmentedControl } from '../../components/SegmentedControl'
import { Skeleton } from '../../components/Skeleton'
import { FilterSelect } from '../../components/Toolbar'
import { useToast } from '../../components/toast'
import { UpdatedIndicator } from '../../components/UpdatedIndicator'
import { DASHBOARD_POLL_MS, useLoad } from '../../components/useLoad'
import { formatDate, formatNumber } from '../../lib/format'
import { formatBucket } from './chartMath'
import { TOLERANCE_RATIO, computeKpis, deviationPercent, mergeChartPoints } from './kpis'
import {
  DEFAULT_PERIOD,
  PRESETS,
  addDays,
  allowedGranularities,
  customRangeError,
  effectiveGranularity,
  resolvePeriod,
  toDateString,
  type PeriodState,
} from './periods'
import { ProductionTable } from './ProductionTable'
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
import { periodText, underPeriods, underproductionTicketHref } from './underproduction'

interface Props {
  installationId: string
  /** Used in the prefilled ticket description. */
  installationName?: string
  /** Controlled period (e.g. kept in the URL). Without it the panel keeps its own state, so it can be embedded anywhere. */
  period?: PeriodState
  onPeriodChange?: (period: PeriodState) => void
  /** Controlled "Zvýrazniť podvýrobu" switch; on by default. */
  highlight?: boolean
  onHighlightChange?: (value: boolean) => void
  /** First item of the filter band, e.g. the installation picker. */
  picker?: ReactNode
  /** Underproduction chips link to a prefilled new ticket; without it they are plain text. */
  canCreateTicket?: boolean
}

type Loaded =
  | { kind: 'ok'; production: ProductionResponse; comparison: ComparisonResponse | null; comparisonFailed: boolean }
  | { kind: 'rejected'; message: string }

const kwh = (value: number) => `${formatNumber(value, 1)} ${T.units.kwh}`

/** Filter band, KPI cards, actual vs. expected chart with underproduction periods and CSV export of one installation. */
export function ProductionPanel({
  installationId,
  installationName = '',
  period,
  onPeriodChange,
  highlight,
  onHighlightChange,
  picker,
  canCreateTicket = false,
}: Props) {
  const toast = useToast()
  const [internal, setInternal] = useState<PeriodState>(DEFAULT_PERIOD)
  const [internalHighlight, setInternalHighlight] = useState(true)
  const [now] = useState(() => new Date())
  const [separator, setSeparator] = useState<CsvSeparator>('semicolon')
  const [exporting, setExporting] = useState(false)
  const state = period ?? internal
  const setState = onPeriodChange ?? setInternal
  const showUnder = highlight ?? internalHighlight
  const setShowUnder = onHighlightChange ?? setInternalHighlight

  const resolved = resolvePeriod(state, now)
  const rangeError = customRangeError(state)
  const granularity = resolved ? effectiveGranularity(state, resolved.days) : 'day'
  const allowed = resolved ? allowedGranularities(resolved.days) : []
  const query = resolved ? { from: resolved.from, to: resolved.to, granularity } : null
  // The range end is exclusive: only a period reaching past today contains the current day and can still change.
  const includesToday = resolved !== null && !rangeError && resolved.to > toDateString(now)

  const { data, loading, error, reload, updatedAt, refreshError, refresh } = useLoad<Loaded | null>(
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
    { intervalMs: DASHBOARD_POLL_MS, enabled: includesToday },
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
  const comparison = ok?.comparison?.predictionAvailable ? ok.comparison : null
  const timeZone = ok?.production.timeZone ?? 'Europe/Bratislava'
  const bucket = ok?.production.granularity ?? granularity
  const longLabel = (start: number) => formatBucket(start, bucket, timeZone, true)
  const periods = comparison ? underPeriods(points, longLabel) : []
  const errorText = rangeError ? T.customErrors[rangeError] : undefined

  const expected = comparison?.predictedEnergyKwh ?? null
  const deviation = comparison && expected ? ((comparison.actualEnergyKwh - expected) / expected) * 100 : null
  const deviationTone = deviation === null ? undefined : deviation / 100 + 1 < TOLERANCE_RATIO ? 'error' : deviation >= 0 ? 'success' : 'warning'

  function describe(index: number): string[] {
    const p = points[index]
    const dev = deviationPercent(p)
    return [
      longLabel(p.start),
      `${T.chart.actual}: ${p.actual === null ? T.chart.noValue : kwh(p.actual)}`,
      ...(comparison ? [`${T.chart.predicted}: ${p.predicted === null ? T.chart.noValue : kwh(p.predicted)}`] : []),
      ...(dev !== null ? [`${T.chart.deviation}: ${formatNumber(dev, 1)} %`] : []),
    ]
  }

  return (
    <div className="prod-panel">
      <div className="card monitoring-filters" role="group" aria-label={T.filters}>
        {picker}
        <SegmentedControl
          label={T.period}
          size="sm"
          value={state.preset}
          onChange={(preset) => setState({ ...state, preset })}
          options={PRESETS.map((preset) => ({ value: preset, label: T.presets[preset] }))}
        />
        {state.preset === 'custom' ? (
          <div className="monitoring-filters__dates">
            <input type="date" aria-label={T.from} value={state.from} aria-invalid={Boolean(errorText)} onChange={(e) => setState({ ...state, from: e.target.value })} />
            <span aria-hidden="true">–</span>
            <input type="date" aria-label={T.to} value={state.to} aria-invalid={Boolean(errorText)} onChange={(e) => setState({ ...state, to: e.target.value })} />
          </div>
        ) : (
          resolved && (
            <span className="monitoring-filters__range">
              {resolved.days === 1 ? formatDate(resolved.from) : `${formatDate(resolved.from)} – ${formatDate(addDays(resolved.to, -1))}`}
            </span>
          )
        )}
        <FilterSelect
          label={T.granularity}
          value={state.granularity}
          onChange={(value) => setState({ ...state, granularity: value as PeriodState['granularity'] })}
          options={[
            { value: 'auto', label: T.auto },
            ...allowed.map((g) => ({ value: g, label: T.granularities[g] })),
          ]}
        />
        <label className="monitoring-switch">
          <input type="checkbox" role="switch" checked={showUnder} onChange={(e) => setShowUnder(e.target.checked)} />
          {T.highlight}
        </label>
      </div>
      {errorText && (
        <p role="alert" className="form-field__error">
          {errorText}
        </p>
      )}
      {includesToday && data?.kind === 'ok' && <UpdatedIndicator updatedAt={updatedAt} refreshError={refreshError} onRetry={refresh} />}

      {loading && !errorText && (
        <>
          <div className="kpi-grid monitoring-kpis" aria-hidden="true">
            {[0, 1, 2, 3].map((i) => (
              <div key={i} className="card kpi-card">
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
          <div className="kpi-grid monitoring-kpis" role="group" aria-label={T.kpiRegion}>
            <KpiCard
              label={T.kpi.actual}
              value={kwh(kpis.energyKwh)}
              detail={kpis.peakPowerKw === null ? undefined : T.kpi.peak(`${formatNumber(kpis.peakPowerKw, 2)} ${T.units.kw}`)}
            />
            <KpiCard label={T.kpi.expected} value={expected === null ? '–' : kwh(expected)} detail={expected === null ? T.kpi.noPrediction : T.kpi.expectedDetail} />
            <KpiCard
              label={T.kpi.deviation}
              value={deviation === null ? '–' : `${deviation > 0 ? '+' : ''}${formatNumber(deviation, 1)} %`}
              detail={comparison && expected !== null ? kwh(comparison.actualEnergyKwh - expected) : T.kpi.noPrediction}
              trend={deviation === null ? undefined : { text: `${Math.round((deviation / 100 + 1) * 100)} %`, tone: deviationTone }}
            />
            <KpiCard
              label={T.kpi.under}
              value={comparison ? formatNumber(periods.length, 0) : '–'}
              detail={comparison ? T.kpi.underDetail : T.kpi.noPrediction}
              trend={periods.length > 0 ? { text: T.kpi.underTrend, tone: 'error' } : undefined}
            />
          </div>
          {!hasData ? (
            <EmptyState title={T.empty} description={T.emptyHint} />
          ) : (
            <section className="card prod-chart-card" aria-labelledby="prod-chart-title">
              <div className="prod-chart-card__head">
                <div>
                  <h3 id="prod-chart-title" className="prod-chart-card__title">
                    {T.chart.title}
                  </h3>
                  <p className="prod-chart-card__subtitle">
                    {[installationName, `${T.units.kwh} · ${T.granularities[bucket]}`].filter(Boolean).join(' · ')}
                  </p>
                </div>
                <div className="prod-chart-card__export">
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
                  <button type="button" className="btn" disabled={exporting} onClick={exportCsv}>
                    {exporting ? T.export.busy : T.export.button}
                  </button>
                </div>
              </div>
              {granularity === 'fiveMinutes' ? (
                <p className="kpi__detail">{T.predictionUnavailableForGranularity}</p>
              ) : ok.comparisonFailed ? (
                <p className="kpi__detail">{T.comparisonFailed}</p>
              ) : (
                !comparison && <p className="kpi__detail">{T.noPrediction}</p>
              )}
              <ProductionAreaChart
                title={T.chart.title}
                unit={T.units.kwh}
                points={points.map((p) => ({ label: formatBucket(p.start, bucket, timeZone), actual: p.actual, predicted: p.predicted }))}
                underRatio={TOLERANCE_RATIO}
                showUnder={showUnder}
                underLabels={false}
                table={false}
                xLabelCount={6}
                interactive={{ label: T.chart.label, describe }}
              />
              {comparison && showUnder && (
                <div className="under-periods">
                  <span className="under-periods__title">{T.under.title}</span>
                  {periods.length === 0 && <span className="under-periods__none">{T.under.none}</span>}
                  <ul className="under-periods__list">
                    {periods.map((p) => {
                      const text = T.under.chip(periodText(p), Math.round(p.ratio * 100))
                      return (
                        <li key={p.start}>
                          {canCreateTicket ? (
                            <Link
                              className="under-chip"
                              to={underproductionTicketHref(installationId, installationName, p)}
                              aria-label={T.under.create(periodText(p))}
                            >
                              {text}
                            </Link>
                          ) : (
                            <span className="under-chip">{text}</span>
                          )}
                        </li>
                      )
                    })}
                  </ul>
                  {canCreateTicket && periods.length > 0 && <span className="under-periods__hint">{T.under.hint}</span>}
                </div>
              )}
              <ProductionTable points={points} granularity={bucket} timeZone={timeZone} predictionAvailable={Boolean(comparison)} />
            </section>
          )}
        </>
      )}
    </div>
  )
}
