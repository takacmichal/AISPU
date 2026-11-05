import { Suspense, lazy, useMemo, useRef, type KeyboardEvent } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { useAuth } from '../../auth/AuthContext'
import { canAccess, findItem } from '../../auth/navigation'
import { ActivityTimeline } from '../../components/ActivityTimeline'
import { KeyValueList, type KeyValueItem } from '../../components/KeyValueList'
import { PageHeader } from '../../components/PageHeader'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatRelative } from '../../lib/format'
import { customerActivity, nextStep, type CustomerRecords } from './customerOverview'
import { CUSTOMER_TEXTS as T, STATUS_BADGE, STATUS_LABELS, TYPE_LABELS } from './texts'
import './customers.css'

const SupplyPointsTab = lazy(() => import('./tabs/SupplyPointsTab'))
const QuotesTab = lazy(() => import('./tabs/LinkedTabs').then((m) => ({ default: m.QuotesTab })))
const ContractsTab = lazy(() => import('./tabs/LinkedTabs').then((m) => ({ default: m.ContractsTab })))
const InstallationsTab = lazy(() => import('./tabs/LinkedTabs').then((m) => ({ default: m.InstallationsTab })))

type Customer = components['schemas']['CustomerResponse']
type TabKey = 'data' | 'supply-points' | 'quotes' | 'contracts' | 'installations'

interface TabDef {
  key: TabKey
  label: string
  /** Section whose roles decide whether the tab is shown. */
  section?: string
}

const TABS: readonly TabDef[] = [
  { key: 'data', label: T.tabs.overview },
  { key: 'supply-points', label: T.tabs.supplyPoints },
  { key: 'quotes', label: T.tabs.quotes, section: '/quotes' },
  { key: 'contracts', label: T.tabs.contracts, section: '/contracts' },
  { key: 'installations', label: T.tabs.installations, section: '/installations' },
]

/** Loads a list when the role may read it; undefined otherwise (no request is sent). */
function useList<D>(allowed: boolean, key: string, load: (signal: AbortSignal) => Promise<{ data?: D[]; response: Response }>) {
  return useLoad<D[] | undefined>(async (signal) => {
    if (!allowed) return undefined
    const { data, response } = await load(signal)
    if (!Array.isArray(data)) throw new Error(`HTTP ${response.status}`)
    return data
  }, `${key}|${allowed}`)
}

export default function CustomerDetailPage() {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const { roles } = useAuth()
  const tabRefs = useRef<Partial<Record<TabKey, HTMLButtonElement | null>>>({})

  const tabs = useMemo(() => TABS.filter((tab) => !tab.section || canAccess(findItem(tab.section)!, roles)), [roles])
  const allowed = (key: TabKey) => tabs.some((t) => t.key === key)
  const requested = params.get('tab')
  const active = tabs.find((tab) => tab.key === requested) ?? tabs[0]

  const { data: customer, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/customers/{id}', { params: { path: { id } }, signal })
    if (!data) throw new Error(response.status === 404 ? 'not-found' : `HTTP ${response.status}`)
    return data
  }, id)

  // Counts for the tabs and the overview. The tabs still load their own lists so they stay independent.
  const supplyPoints = useList(true, `sp-${id}`, (signal) =>
    api.GET('/api/customers/{customerId}/supply-points', { params: { path: { customerId: id } }, signal }),
  )
  const quotes = useList(allowed('quotes'), `q-${id}`, (signal) => api.GET('/api/quotes', { params: { query: { customerId: id } }, signal }))
  const contracts = useList(allowed('contracts'), `c-${id}`, (signal) =>
    api.GET('/api/contracts', { params: { query: { customerId: id } }, signal }),
  )
  const installations = useList(allowed('installations'), `i-${id}`, (signal) =>
    api.GET('/api/installations', { params: { query: { customerId: id } }, signal }),
  )

  const counts: Partial<Record<TabKey, number>> = {
    'supply-points': supplyPoints.data?.length,
    quotes: quotes.data?.length,
    contracts: contracts.data?.length,
    installations: installations.data?.length,
  }
  const records: CustomerRecords = { quotes: quotes.data, contracts: contracts.data, installations: installations.data }
  const recordsLoading = quotes.loading || contracts.loading || installations.loading

  function select(key: TabKey, focus = false) {
    setParams({ tab: key }, { replace: true })
    if (focus) tabRefs.current[key]?.focus()
  }

  // Tabs pattern: arrows move between tabs, Home/End jump to the ends.
  function onTabKey(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const moves: Record<string, number> = { ArrowRight: index + 1, ArrowLeft: index - 1, Home: 0, End: tabs.length - 1 }
    if (!(event.key in moves)) return
    event.preventDefault()
    select(tabs[(moves[event.key] + tabs.length) % tabs.length].key, true)
  }

  if (loading) return <Skeleton lines={4} label={T.detail.loading} />
  if (error || !customer)
    return (
      <p role="alert" className="alert">
        {T.detail.error}
      </p>
    )

  const subtitle = [TYPE_LABELS[customer.type] ?? customer.type, customer.ico && `IČO ${customer.ico}`, customer.address.city]
    .filter(Boolean)
    .join(' · ')

  return (
    <section>
      <PageHeader
        title={customer.displayName}
        status={<span className={STATUS_BADGE[customer.status] ?? 'badge'}>{STATUS_LABELS[customer.status] ?? customer.status}</span>}
        subtitle={subtitle}
        actions={
          <>
            <Link to={`/customers/${customer.id}/edit`} className="btn">
              {T.detail.edit}
            </Link>
            {allowed('quotes') && (
              <Link to={`/quotes/new?customerId=${customer.id}`} className="btn btn--primary">
                {T.detail.newQuote}
              </Link>
            )}
          </>
        }
      />
      <div role="tablist" aria-label={T.tabs.label} className="tabs">
        {tabs.map((tab, index) => {
          const selected = tab.key === active.key
          return (
            <button
              key={tab.key}
              ref={(el) => {
                tabRefs.current[tab.key] = el
              }}
              type="button"
              role="tab"
              id={`tab-${tab.key}`}
              aria-selected={selected}
              aria-controls="tab-panel"
              tabIndex={selected ? 0 : -1}
              onClick={() => select(tab.key)}
              onKeyDown={(event) => onTabKey(event, index)}
            >
              {tab.label}
              {counts[tab.key] !== undefined && (
                <>
                  {' '}
                  <span className="tabs__count">{counts[tab.key]}</span>
                </>
              )}
            </button>
          )
        })}
      </div>
      <div role="tabpanel" id="tab-panel" aria-labelledby={`tab-${active.key}`}>
        <Suspense fallback={<Skeleton lines={3} label={T.detail.tabLoading} />}>
          {active.key === 'data' && (
            <OverviewTab
              customer={customer}
              tabs={tabs.filter((t) => t.key !== 'data')}
              counts={counts}
              records={records}
              recordsLoading={recordsLoading}
              onOpen={select}
            />
          )}
          {active.key === 'supply-points' && <SupplyPointsTab customerId={id} />}
          {active.key === 'quotes' && <QuotesTab customerId={id} />}
          {active.key === 'contracts' && <ContractsTab customerId={id} />}
          {active.key === 'installations' && <InstallationsTab customerId={id} />}
        </Suspense>
      </div>
    </section>
  )
}

interface OverviewTabProps {
  customer: Customer
  tabs: readonly TabDef[]
  counts: Partial<Record<TabKey, number>>
  records: CustomerRecords
  recordsLoading: boolean
  onOpen: (key: TabKey) => void
}

/** "Prehľad" from the wireframe: contact details, summary tiles, the next step and recent activity. */
function OverviewTab({ customer, tabs, counts, records, recordsLoading, onOpen }: OverviewTabProps) {
  const O = T.overview
  const a = customer.address
  const rows: (KeyValueItem | null | '' | undefined)[] = [
    { label: O.email, value: <a href={`mailto:${customer.email}`}>{customer.email}</a> },
    customer.phone && { label: O.phone, value: <a href={`tel:${customer.phone.replace(/\s/g, '')}`}>{customer.phone}</a> },
    { label: O.address, value: `${a.street}, ${a.postalCode} ${a.city}, ${a.country}` },
    customer.ico && { label: O.ico, value: customer.ico },
    customer.dic && { label: O.dic, value: customer.dic },
    customer.icDph && { label: O.icDph, value: customer.icDph },
    { label: O.since, value: formatDate(customer.createdAt) },
    customer.note && { label: O.note, value: customer.note },
  ]
  const contact = rows.filter((row): row is KeyValueItem => Boolean(row))

  const step = recordsLoading ? undefined : nextStep(records)
  const activity = customerActivity(records)
  const now = new Date()

  return (
    <div className="customer-overview">
      <article className="card customer-overview__card" aria-labelledby="contact-title">
        <h3 id="contact-title" className="customer-overview__title">
          {O.contact}
        </h3>
        <KeyValueList items={contact} />
      </article>

      <div className="customer-overview__middle">
        <div className="card customer-tiles">
          {tabs.map((tab) => (
            <button key={tab.key} type="button" className="customer-tile" onClick={() => onOpen(tab.key)}>
              <span className="customer-tile__value">{counts[tab.key] ?? '–'}</span>
              <span className="customer-tile__label">{tab.label}</span>
            </button>
          ))}
        </div>
        <article className="card customer-overview__card customer-next" aria-labelledby="next-title">
          <h3 id="next-title" className="customer-next__eyebrow">
            {O.nextStep}
          </h3>
          {step === undefined && <Skeleton lines={2} label={O.nextLoading} />}
          {step === null && <p className="customer-next__detail">{O.nextNone}</p>}
          {step && (
            <>
              <p className="customer-next__title">{step.title}</p>
              <p className="customer-next__detail">{step.detail}</p>
              <Link to={step.href} className="btn btn--sm">
                {step.action}
              </Link>
            </>
          )}
        </article>
      </div>

      <article className="card customer-overview__card" aria-labelledby="activity-title">
        <h3 id="activity-title" className="customer-overview__title">
          {O.activity}
        </h3>
        {recordsLoading && <Skeleton lines={3} label={O.activityLoading} />}
        {!recordsLoading && activity.length === 0 && <p className="customer-next__detail">{O.activityEmpty}</p>}
        {!recordsLoading && activity.length > 0 && (
          <ActivityTimeline
            label={O.activity}
            items={activity.map((entry) => ({
              id: entry.id,
              title: <Link to={entry.href}>{entry.title}</Link>,
              meta: <time dateTime={entry.at}>{formatRelative(entry.at, now)}</time>,
            }))}
          />
        )}
      </article>
    </div>
  )
}
