import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { DataTable, type Column } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { Skeleton } from '../../components/Skeleton'
import { useLoad } from '../../components/useLoad'
import { formatDate } from '../../lib/format'
import { CREATE_ROLES, MANAGE_ROLES, ORIGIN_LABELS, PRIORITY_LABELS, STATE_LABELS, T } from './messages'
import { PriorityBadge, StateBadge } from './TicketBadges'
import './tickets.css'
import {
  TICKET_ORIGINS,
  TICKET_PRIORITIES,
  TICKET_STATES,
  TRANSITION_MAP,
  VISIT_WORKFLOW_STATES,
  listTickets,
  type TicketListItem,
} from './ticketsApi'
import { useTicketTransition } from './useTicketTransition'

const PAGE_SIZE = 20
const BOARD_SIZE = 100

/** `/tickets`: filters in the URL, table with paging or Kanban board by state (`?view=board`). */
export default function TicketListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { roles } = useAuth()

  const state = TICKET_STATES.find((s) => s === params.get('state'))
  const priority = TICKET_PRIORITIES.find((p) => p === params.get('priority'))
  const origin = TICKET_ORIGINS.find((o) => o === params.get('origin'))
  const board = params.get('view') === 'board'
  const page = Math.max(1, Number(params.get('page')) || 1)
  const canCreate = CREATE_ROLES.some((r) => roles.includes(r))
  const canManage = MANAGE_ROLES.some((r) => roles.includes(r))

  const { data, loading, error, reload } = useLoad(
    (signal) => listTickets({ state, priority, origin, page: board ? 1 : page, pageSize: board ? BOARD_SIZE : PAGE_SIZE }, signal),
    JSON.stringify([state, priority, origin, board, board ? 1 : page]),
  )
  const mover = useTicketTransition(() => reload())

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  const columns: Column<TicketListItem>[] = [
    { key: 'title', header: T.list.colTitle, render: (t) => <Link to={`/tickets/${t.id}`}>{t.title}</Link> },
    { key: 'state', header: T.list.colState, render: (t) => <StateBadge state={t.state} /> },
    { key: 'priority', header: T.list.colPriority, render: (t) => <PriorityBadge priority={t.priority} /> },
    { key: 'origin', header: T.list.colOrigin, render: (t) => ORIGIN_LABELS[t.origin] },
    { key: 'created', header: T.list.colCreated, render: (t) => formatDate(t.createdAt) },
  ]

  const filtered = Boolean(state || priority || origin)
  const items = data?.items ?? []

  return (
    <section>
      <div className="toolbar">
        <h2>{T.list.title}</h2>
        {canCreate && (
          <Link to="/tickets/new" className="btn btn--primary">
            {T.list.create}
          </Link>
        )}
      </div>
      <div className="btn-row tickets-filters">
        <label>
          {T.list.state}
          <select value={state ?? ''} onChange={(e) => update({ state: e.target.value, page: null })}>
            <option value="">{T.list.allStates}</option>
            {TICKET_STATES.map((s) => (
              <option key={s} value={s}>
                {STATE_LABELS[s]}
              </option>
            ))}
          </select>
        </label>
        <label>
          {T.list.priority}
          <select value={priority ?? ''} onChange={(e) => update({ priority: e.target.value, page: null })}>
            <option value="">{T.list.allPriorities}</option>
            {TICKET_PRIORITIES.map((p) => (
              <option key={p} value={p}>
                {PRIORITY_LABELS[p]}
              </option>
            ))}
          </select>
        </label>
        <label>
          {T.list.origin}
          <select value={origin ?? ''} onChange={(e) => update({ origin: e.target.value, page: null })}>
            <option value="">{T.list.allOrigins}</option>
            {TICKET_ORIGINS.map((o) => (
              <option key={o} value={o}>
                {ORIGIN_LABELS[o]}
              </option>
            ))}
          </select>
        </label>
        <div className="view-switch" role="group" aria-label={T.list.viewLabel}>
          <button type="button" className="btn btn--ghost" aria-pressed={!board} onClick={() => update({ view: null })}>
            {T.list.viewTable}
          </button>
          <button type="button" className="btn btn--ghost" aria-pressed={board} onClick={() => update({ view: 'board', page: null })}>
            {T.list.viewBoard}
          </button>
        </div>
      </div>

      {!board && (
        <DataTable
          columns={columns}
          rows={items}
          rowKey={(t) => t.id}
          page={page}
          pageSize={PAGE_SIZE}
          totalCount={data?.totalCount ?? 0}
          onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
          loading={loading}
          error={error ? T.list.error : null}
          emptyMessage={filtered ? T.list.emptyFiltered : T.list.empty}
          onRowClick={(t) => navigate(`/tickets/${t.id}`)}
        />
      )}

      {board && loading && <Skeleton lines={4} label={T.list.title} />}
      {board && error && (
        <p role="alert" className="alert">
          {T.list.error}
        </p>
      )}
      {board && !loading && !error && items.length === 0 && <EmptyState title={filtered ? T.list.emptyFiltered : T.list.empty} />}
      {board && !loading && !error && items.length > 0 && (
        <>
          {data && data.totalCount > items.length && <p role="note">{T.list.boardTruncated(items.length, data.totalCount)}</p>}
          <div className="kanban" role="region" aria-label={T.list.boardLabel} tabIndex={0}>
            {TICKET_STATES.map((column) => {
              const cards = items.filter((t) => t.state === column)
              return (
                <section key={column} className="kanban__column" aria-labelledby={`col-${column}`}>
                  <h3 id={`col-${column}`}>
                    <span>{STATE_LABELS[column]}</span>
                    <span className="badge">{cards.length}</span>
                  </h3>
                  {cards.length === 0 && <p className="kanban__empty">{T.list.columnEmpty}</p>}
                  <ul className="kanban__cards">
                    {cards.map((t) => (
                      <li key={t.id} className="kanban__card">
                        <Link to={`/tickets/${t.id}`} className="kanban__card-title">
                          {t.title}
                        </Link>
                        <span>
                          <PriorityBadge priority={t.priority} />
                        </span>
                        {canManage && (
                          <div role="group" aria-label={T.list.moveLabel(t.title)} className="btn-row">
                            {TRANSITION_MAP[t.state]
                              .filter((target) => !VISIT_WORKFLOW_STATES.includes(target))
                              .map((target) => (
                                <button
                                  key={target}
                                  type="button"
                                  className="btn btn--ghost btn--sm"
                                  disabled={mover.busy}
                                  aria-label={`${T.list.moveTo(STATE_LABELS[target])}: ${t.title}`}
                                  onClick={() => void mover.move(t.id, t.state, target)}
                                >
                                  → {STATE_LABELS[target]}
                                </button>
                              ))}
                          </div>
                        )}
                      </li>
                    ))}
                  </ul>
                </section>
              )
            })}
          </div>
        </>
      )}
    </section>
  )
}
