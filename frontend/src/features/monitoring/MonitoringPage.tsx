import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { AnomalySection } from './AnomalySection'
import './monitoring.css'
import { periodFromParams, periodToParams } from './periods'
import { ProductionPanel } from './ProductionPanel'
import { T } from './texts'

/** `/monitoring`: production of one installation; installation and period are kept in the URL. */
export default function MonitoringPage() {
  const [params, setParams] = useSearchParams()
  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'installations')

  const list = data ?? []
  const selected = list.find((i) => i.id === params.get('installation'))?.id ?? list[0]?.id ?? ''

  return (
    <section>
      <header className="page-header">
        <h2 className="page-header__title">{T.title}</h2>
        <p className="page-header__subtitle">{T.subtitle}</p>
      </header>
      {loading && <Skeleton lines={2} />}
      {error && (
        <div className="kpi__error" role="alert">
          <p>{T.installationsError}</p>
          <button type="button" className="btn btn--sm" onClick={reload}>{T.retry}</button>
        </div>
      )}
      {data && list.length === 0 && <EmptyState title={T.noInstallations} />}
      {list.length > 0 && (
        <>
          <div className="btn-row">
            <label>
              {T.installation}
              <select
                value={selected}
                onChange={(e) => {
                  const next = new URLSearchParams(params)
                  next.set('installation', e.target.value)
                  setParams(next)
                }}
              >
                {list.map((i) => (
                  <option key={i.id} value={i.id}>{i.name}</option>
                ))}
              </select>
            </label>
          </div>
          <ProductionPanel
            key={selected}
            installationId={selected}
            period={periodFromParams(params)}
            onPeriodChange={(p) => setParams(periodToParams(params, p))}
          />
          <AnomalySection installationId={selected} />
        </>
      )}
    </section>
  )
}
