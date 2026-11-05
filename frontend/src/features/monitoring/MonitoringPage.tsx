import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { useAuth } from '../../auth/AuthContext'
import { canAccess, findItem } from '../../auth/navigation'
import { EmptyState } from '../../components/EmptyState'
import { PageHeader } from '../../components/PageHeader'
import { Skeleton } from '../../components/Skeleton'
import { FilterSelect } from '../../components/Toolbar'
import { useLoad } from '../../components/useLoad'
import type { AlarmSeverityFilter, AlarmStateFilter } from './alarmLogic'
import { AlarmsSection } from './AlarmsSection'
import './monitoring.css'
import { periodFromParams, periodToParams } from './periods'
import { ProductionPanel } from './ProductionPanel'
import { T } from './texts'

/**
 * `/monitoring` (wireframe "Monitoring výroby"): filter band, KPIs, actual vs. expected production and
 * underproduction periods of one installation. Installation, period and the highlight switch are kept in the URL.
 */
export default function MonitoringPage() {
  const [params, setParams] = useSearchParams()
  const { roles } = useAuth()
  const ticketRoute = findItem('/tickets/new')
  const canCreateTicket = ticketRoute ? canAccess(ticketRoute, roles) : false
  const canAnalyze = roles.includes('Admin') || roles.includes('Operator')
  const [analyses, setAnalyses] = useState(0)

  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'installations')

  const list = data ?? []
  const selected = list.find((i) => i.id === params.get('installation')) ?? list[0]

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null) next.delete(key)
      else next.set(key, value)
    }
    setParams(next, { replace: true })
  }

  return (
    <section>
      <PageHeader title={T.title} subtitle={T.subtitle} />
      {loading && <Skeleton lines={2} />}
      {error && (
        <div className="kpi__error" role="alert">
          <p>{T.installationsError}</p>
          <button type="button" className="btn btn--sm" onClick={reload}>
            {T.retry}
          </button>
        </div>
      )}
      {data && list.length === 0 && <EmptyState title={T.noInstallations} />}
      {selected && (
        <>
          <ProductionPanel
            key={`${selected.id}#${analyses}`}
            installationId={selected.id}
            installationName={selected.name}
            period={periodFromParams(params)}
            onPeriodChange={(p) => setParams(periodToParams(params, p), { replace: true })}
            highlight={params.get('under') !== '0'}
            onHighlightChange={(on) => update({ under: on ? null : '0' })}
            canCreateTicket={canCreateTicket}
            picker={
              <FilterSelect
                label={T.installation}
                value={selected.id}
                onChange={(id) => update({ installation: id })}
                options={list.map((i) => ({ value: i.id, label: i.name }))}
              />
            }
          />
          <AlarmsSection
            key={selected.id}
            installationId={selected.id}
            severity={(params.get('alarmSeverity') as AlarmSeverityFilter | null) ?? 'all'}
            state={(params.get('alarmState') as AlarmStateFilter | null) ?? 'all'}
            onFilterChange={({ severity, state }) =>
              update({
                ...(severity && { alarmSeverity: severity === 'all' ? null : severity }),
                ...(state && { alarmState: state === 'all' ? null : state }),
              })
            }
            canAnalyze={canAnalyze}
            canCreateTicket={canCreateTicket}
            onAnalyzed={() => setAnalyses((n) => n + 1)}
          />
        </>
      )}
    </section>
  )
}
