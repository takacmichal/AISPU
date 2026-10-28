import { useEffect, useState } from 'react'
import { fetchSystemInfo, type SystemInfo } from '../api/system'

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
      <p>Správa fotovoltických elektrární</p>
      {state.status === 'loading' && <p role="status">Načítavam stav API…</p>}
      {state.status === 'error' && <p role="alert">API je nedostupné.</p>}
      {state.status === 'ok' && (
        <p>
          API: {state.info.name} v{state.info.version} ({state.info.environment})
        </p>
      )}
    </section>
  )
}
