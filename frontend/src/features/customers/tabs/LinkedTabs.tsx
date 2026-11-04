import { Link } from 'react-router-dom'
import { api } from '../../../api/client'
import type { ReactNode } from 'react'
import { EmptyState } from '../../../components/EmptyState'
import { Skeleton } from '../../../components/Skeleton'
import { useLoad } from '../../../components/useLoad'
import { formatCurrency, formatDate, formatKwp } from '../../../lib/format'
import { StateBadge } from '../../installations/StateBadge'
import { STATUS_BADGE, STATUS_LABELS } from '../../quotes/quoteLabels'

const date = formatDate
const money = formatCurrency

const CONTRACT_STATUS: Record<string, string> = { Draft: 'Koncept', Signed: 'Podpísaná' }
const CONTRACT_BADGE: Record<string, string> = { Draft: 'badge badge--attention', Signed: 'badge badge--success' }

function Section({
  title,
  listPath,
  loading,
  error,
  empty,
  children,
}: {
  title: string
  listPath: string
  loading: boolean
  error: boolean
  empty: boolean
  children: ReactNode
}) {
  return (
    <div className="linked-tab">
      <p className="linked-tab__all">
        <Link to={listPath}>Všetky {title.toLowerCase()}</Link>
      </p>
      {loading && <Skeleton lines={3} />}
      {error && (
        <p role="alert" className="alert">
          {title} sa nepodarilo načítať.
        </p>
      )}
      {!loading && !error && empty && <EmptyState title="Zákazník zatiaľ nemá žiadne záznamy." />}
      {children}
    </div>
  )
}

export function QuotesTab({ customerId }: { customerId: string }) {
  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/quotes', { params: { query: { customerId } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, customerId)

  return (
    <Section title="Ponuky" listPath="/quotes" loading={loading} error={error} empty={data?.length === 0}>
      {data && data.length > 0 && (
        <div className="data-table__scroll">
<table>
          <thead>
            <tr>
              <th scope="col">Ponuka</th>
              <th scope="col">Stav</th>
              <th scope="col">Platná do</th>
              <th scope="col">Celkom s DPH</th>
            </tr>
          </thead>
          <tbody>
            {data.map((q) => (
              <tr key={q.id}>
                <td>
                  <Link to={`/quotes/${q.id}`}>Ponuka z {date(q.createdAt)}</Link>
                </td>
                <td>
                  <span className={STATUS_BADGE[q.status]}>{STATUS_LABELS[q.status]}</span>
                </td>
                <td>{date(q.validUntil)}</td>
                <td>{money(q.grossTotal, q.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
</div>
      )}
    </Section>
  )
}

export function ContractsTab({ customerId }: { customerId: string }) {
  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/contracts', { params: { query: { customerId } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, customerId)

  return (
    <Section title="Zmluvy" listPath="/contracts" loading={loading} error={error} empty={data?.length === 0}>
      {data && data.length > 0 && (
        <div className="data-table__scroll">
<table>
          <thead>
            <tr>
              <th scope="col">Číslo</th>
              <th scope="col">Stav</th>
              <th scope="col">Podpísaná</th>
              <th scope="col">Celkom s DPH</th>
            </tr>
          </thead>
          <tbody>
            {data.map((c) => (
              <tr key={c.id}>
                <td>
                  <Link to={`/contracts/${c.id}`}>{c.number}</Link>
                </td>
                <td>
                  <span className={CONTRACT_BADGE[c.status] ?? 'badge'}>{CONTRACT_STATUS[c.status] ?? c.status}</span>
                </td>
                <td>{date(c.signedOn)}</td>
                <td>{money(c.grossTotal, c.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
</div>
      )}
    </Section>
  )
}

export function InstallationsTab({ customerId }: { customerId: string }) {
  const { data, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/installations', { params: { query: { customerId } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, customerId)

  return (
    <Section title="Inštalácie" listPath="/installations" loading={loading} error={error} empty={data?.length === 0}>
      {data && data.length > 0 && (
        <div className="data-table__scroll">
<table>
          <thead>
            <tr>
              <th scope="col">Názov</th>
              <th scope="col">Stav</th>
              <th scope="col">Výkon</th>
              <th scope="col">Termín</th>
            </tr>
          </thead>
          <tbody>
            {data.map((i) => (
              <tr key={i.id}>
                <td>
                  <Link to={`/installations/${i.id}`}>{i.name}</Link>
                </td>
                <td>
                  <StateBadge state={i.state} />
                </td>
                <td>{formatKwp(i.plannedPowerKwp)}</td>
                <td>{date(i.scheduledStart)}</td>
              </tr>
            ))}
          </tbody>
        </table>
</div>
      )}
    </Section>
  )
}
