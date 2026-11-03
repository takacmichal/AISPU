import { useEffect, useState } from 'react'
import { fetchSystemInfo, type SystemInfo } from '../api/system'
import { Skeleton } from '../components/Skeleton'

type State =
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ok'; info: SystemInfo }

export default function Dashboard() {
  const [state, setState] = useState<State>({ status: 'loading' })

  useEffect(() => {
    const controller = new AbortController()
    fetchSystemInfo(controller.signal)
      .then((info) => setState({ status: 'ok', info }))
      .catch((error: unknown) => {
        if (!controller.signal.aborted) {
          console.error(error)
          setState({ status: 'error' })
        }
      })
    return () => controller.abort()
  }, [])

  return (
    <section>
      <h2>Prehľad</h2>
      <p className="page-intro">Správa fotovoltických elektrární</p>
      <div className="stat-grid">
        <div className="card">
          <p className="stat__label">Stav API</p>
          {state.status === 'loading' && <Skeleton lines={2} label="Načítavam stav API…" />}
          {state.status === 'error' && (
            <p role="alert" className="form-error">
              API je nedostupné.
            </p>
          )}
          {state.status === 'ok' && (
            <>
              <p className="stat__value">
                {state.info.name} <span className="mono">v{state.info.version}</span>
              </p>
              <span className="badge badge--success">{state.info.environment}</span>
            </>
          )}
        </div>
      </div>
    </section>
  )
}
