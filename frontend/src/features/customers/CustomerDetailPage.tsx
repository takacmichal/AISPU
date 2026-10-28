import { Suspense, lazy, useMemo } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { useAuth } from '../../auth/AuthContext'
import { canAccess, findItem } from '../../auth/navigation'
import { useLoad } from '../../components/useLoad'

const SupplyPointsTab = lazy(() => import('./tabs/SupplyPointsTab'))
const QuotesTab = lazy(() => import('./tabs/LinkedTabs').then((m) => ({ default: m.QuotesTab })))
const ContractsTab = lazy(() => import('./tabs/LinkedTabs').then((m) => ({ default: m.ContractsTab })))
const InstallationsTab = lazy(() => import('./tabs/LinkedTabs').then((m) => ({ default: m.InstallationsTab })))

interface TabDef {
  key: string
  label: string
  /** Section whose roles decide whether the tab is shown. */
  section?: string
}

const TABS: readonly TabDef[] = [
  { key: 'data', label: 'Údaje' },
  { key: 'supply-points', label: 'Odberné miesta' },
  { key: 'quotes', label: 'Ponuky', section: '/quotes' },
  { key: 'contracts', label: 'Zmluvy', section: '/contracts' },
  { key: 'installations', label: 'Inštalácie', section: '/installations' },
]

const TYPE_LABELS: Record<string, string> = { Person: 'Fyzická osoba', Company: 'Právnická osoba' }
const STATUS_LABELS: Record<string, string> = { Active: 'Aktívny', Archived: 'Archivovaný' }

export default function CustomerDetailPage() {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { roles } = useAuth()

  const tabs = useMemo(
    () => TABS.filter((tab) => !tab.section || canAccess(findItem(tab.section)!, roles)),
    [roles],
  )
  const requested = params.get('tab')
  const active = tabs.find((tab) => tab.key === requested) ?? tabs[0]

  const { data: customer, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/customers/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(response.status === 404 ? 'not-found' : `HTTP ${response.status}`)
    return data
  }, id)

  if (loading) return <p role="status">Načítavam zákazníka…</p>
  if (error || !customer) return <p role="alert">Zákazníka sa nepodarilo načítať.</p>

  return (
    <section>
      <h2>{customer.displayName}</h2>
      <div role="tablist" aria-label="Sekcie zákazníka" className="tabs">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            type="button"
            role="tab"
            id={`tab-${tab.key}`}
            aria-selected={tab.key === active.key}
            aria-controls="tab-panel"
            onClick={() => setParams({ tab: tab.key }, { replace: true })}
          >
            {tab.label}
          </button>
        ))}
      </div>
      <div role="tabpanel" id="tab-panel" aria-labelledby={`tab-${active.key}`}>
        <Suspense fallback={<p role="status">Načítavam záložku…</p>}>
          {active.key === 'data' && <DataTab customer={customer} />}
          {active.key === 'supply-points' && <SupplyPointsTab customerId={id} />}
          {active.key === 'quotes' && <QuotesTab customerId={id} />}
          {active.key === 'contracts' && <ContractsTab customerId={id} />}
          {active.key === 'installations' && <InstallationsTab customerId={id} />}
        </Suspense>
      </div>
    </section>
  )
}

function DataTab({ customer }: { customer: components['schemas']['CustomerResponse'] }) {
  const rows: [string, string | null | undefined][] = [
    ['Typ', TYPE_LABELS[customer.type] ?? customer.type],
    ['Stav', STATUS_LABELS[customer.status] ?? customer.status],
    ['IČO', customer.ico],
    ['DIČ', customer.dic],
    ['IČ DPH', customer.icDph],
    ['E-mail', customer.email],
    ['Telefón', customer.phone],
    ['Adresa', `${customer.address.street}, ${customer.address.postalCode} ${customer.address.city}, ${customer.address.country}`],
    ['Poznámka', customer.note],
  ]
  return (
    <div>
      <p>
        <Link to={`/customers/${customer.id}/edit`}>Upraviť</Link>
      </p>
      <dl>
        {rows
          .filter(([, value]) => value)
          .map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
      </dl>
    </div>
  )
}
