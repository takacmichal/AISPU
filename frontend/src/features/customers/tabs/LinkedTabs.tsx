import { Link } from 'react-router-dom'
import { api } from '../../../api/client'
import type { ReactNode } from 'react'
import { useLoad } from '../../../components/useLoad'

const date = (value?: string | null) => (value ? new Date(value).toLocaleDateString('sk-SK') : '–')
const money = (amount: number | string, currency: string) =>
  new Intl.NumberFormat('sk-SK', { style: 'currency', currency }).format(Number(amount))

const QUOTE_STATUS: Record<string, string> = {
  Draft: 'Koncept',
  Sent: 'Odoslaná',
  Accepted: 'Prijatá',
  Rejected: 'Zamietnutá',
  Expired: 'Expirovaná',
}
const CONTRACT_STATUS: Record<string, string> = { Draft: 'Koncept', Signed: 'Podpísaná', Cancelled: 'Zrušená' }
const INSTALLATION_STATE: Record<string, string> = {
  Planned: 'Plánovaná',
  Scheduled: 'Naplánovaná',
  InProgress: 'Prebieha',
  Completed: 'Dokončená',
  HandedOver: 'Odovzdaná',
}

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
    <div>
      <p>
        <Link to={listPath}>Všetky {title.toLowerCase()}</Link>
      </p>
      {loading && <p role="status">Načítavam…</p>}
      {error && <p role="alert">{title} sa nepodarilo načítať.</p>}
      {!loading && !error && empty && <p>Zákazník zatiaľ nemá žiadne záznamy.</p>}
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
                <td>{QUOTE_STATUS[q.status] ?? q.status}</td>
                <td>{date(q.validUntil)}</td>
                <td>{money(q.grossTotal, q.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
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
                <td>{CONTRACT_STATUS[c.status] ?? c.status}</td>
                <td>{date(c.signedOn)}</td>
                <td>{money(c.grossTotal, c.currency)}</td>
              </tr>
            ))}
          </tbody>
        </table>
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
                <td>{INSTALLATION_STATE[i.state] ?? i.state}</td>
                <td>{i.plannedPowerKwp != null ? `${i.plannedPowerKwp} kWp` : '–'}</td>
                <td>{date(i.scheduledStart)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </Section>
  )
}
