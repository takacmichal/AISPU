import { useState, type DragEvent } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/AuthContext'
import { DataTable, type Column } from '../../components/DataTable'
import { EmptyState } from '../../components/EmptyState'
import { PageHeader } from '../../components/PageHeader'
import { SegmentedControl } from '../../components/SegmentedControl'
import { Skeleton } from '../../components/Skeleton'
import { FilterSelect, Toolbar } from '../../components/Toolbar'
import { useLoad } from '../../components/useLoad'
import { formatDate, formatRelative } from '../../lib/format'
import { CREATE_ROLES, MANAGE_ROLES, ORIGIN_LABELS, PRIORITY_LABELS, STATE_LABELS, T } from './messages'
import { PriorityBadge, StateBadge } from './TicketBadges'
import './tickets.css'
import {
  TICKET_ORIGINS,
  TICKET_PRIORITIES,
  TICKET_STATES,
  TRANSITION_MAP,
  VISIT_WORKFLOW_STATES,
  listInstallationOptions,
  listTickets,
  type TicketListItem,
  type TicketState,
} from './ticketsApi'
import { useTicketTransition } from './useTicketTransition'

const PAGE_SIZE = 20
const BOARD_SIZE = 100
type View = 'board' | 'list'

/** States a planner may move a ticket to from the board; visit-driven states are set by the visit workflow. */
const boardTargets = (state: TicketState) => TRANSITION_MAP[state].filter((target) => !VISIT_WORKFLOW_STATES.includes(target))

/**
 * `/tickets` (wireframe "Servisné tikety"): Kanban with the 8 backend states or a paged list (`?view=`).
 * Planners (Admin, Operator) start on the Kanban, everybody else on the list. Filters live in the URL.
 */
export default function TicketListPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const { roles } = useAuth()

  const state = TICKET_STATES.find((s) => s === params.get('state'))
  const priority = TICKET_PRIORITIES.find((p) => p === params.get('priority'))
  const origin = TICKET_ORIGINS.find((o) => o === params.get('origin'))
  const canCreate = CREATE_ROLES.some((r) => roles.includes(r))
  const canManage = MANAGE_ROLES.some((r) => roles.includes(r))
  const requestedView = params.get('view')
  const view: View = requestedView === 'board' || requestedView === 'list' ? requestedView : canManage ? 'board' : 'list'
  const board = view === 'board'
  const page = Math.max(1, Number(params.get('page')) || 1)

  const { data, loading, error, reload } = useLoad(
    (signal) => listTickets({ state, priority, origin, page: board ? 1 : page, pageSize: board ? BOARD_SIZE : PAGE_SIZE }, signal),
    JSON.stringify([state, priority, origin, board, board ? 1 : page]),
  )
  // Installation names are a convenience on the cards and rows; without them the title is enough.
  const installations = useLoad(async (signal) => {
    const list = await listInstallationOptions(signal).catch(() => [])
    return new Map(list.map((i) => [i.id, i.name]))
  }, 'ticket-installations')
  const installationName = (id: string) => installations.data?.get(id)
  const mover = useTicketTransition(() => reload())
  // Cards dropped into another column; kept after a successful move, so the board does not flash a reload.
  const [movedStates, setMovedStates] = useState<Record<string, TicketState>>({})
  const [dragged, setDragged] = useState<TicketListItem | null>(null)

  function dropOn(target: TicketState) {
    const card = dragged
    setDragged(null)
    if (!card || !boardTargets(card.state).includes(target)) return
    void mover.moveOptimistic(card.id, card.state, target, {
      apply: () => setMovedStates((moved) => ({ ...moved, [card.id]: target })),
      rollback: () =>
        setMovedStates((moved) => {
          const rest = { ...moved }
          delete rest[card.id]
          return rest
        }),
    })
  }

  function startDrag(event: DragEvent, card: TicketListItem) {
    event.dataTransfer.effectAllowed = 'move'
    // Firefox starts a drag only when some data is set.
    event.dataTransfer.setData('text/plain', card.title)
    setDragged(card)
  }

  function update(changes: Record<string, string | null>) {
    setMovedStates({})
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next, { replace: true })
  }

  const columns: Column<TicketListItem>[] = [
    { key: 'title', header: T.list.colTitle, render: (t) => <Link to={`/tickets/${t.id}`}>{t.title}</Link> },
    { key: 'installation', header: T.list.colInstallation, render: (t) => installationName(t.installationId) ?? '–' },
    { key: 'state', header: T.list.colState, render: (t) => <StateBadge state={t.state} /> },
    { key: 'priority', header: T.list.colPriority, render: (t) => <PriorityBadge priority={t.priority} /> },
    { key: 'origin', header: T.list.colOrigin, render: (t) => ORIGIN_LABELS[t.origin] },
    { key: 'created', header: T.list.colCreated, render: (t) => formatDate(t.createdAt) },
  ]

  const filtered = Boolean(state || priority || origin)
  const items = (data?.items ?? []).map((t) => (movedStates[t.id] ? { ...t, state: movedStates[t.id] } : t))
  const all = (label: string) => ({ value: '', label })

  return (
    <section>
      <PageHeader
        title={T.list.title}
        subtitle={T.list.subtitle}
        actions={
          canCreate && (
            <Link to="/tickets/new" className="btn btn--primary">
              {roles.includes('Customer') ? T.list.create : T.list.createStaff}
            </Link>
          )
        }
      />
      <Toolbar
        aside={
          <SegmentedControl<View>
            label={T.list.viewLabel}
            size="sm"
            value={view}
            onChange={(next) => update({ view: next, page: null })}
            options={[
              { value: 'board', label: T.list.viewBoard },
              { value: 'list', label: T.list.viewTable },
            ]}
          />
        }
      >
        <FilterSelect
          label={T.list.state}
          value={state ?? ''}
          onChange={(value) => update({ state: value, page: null })}
          options={[all(T.list.allStates), ...TICKET_STATES.map((s) => ({ value: s, label: STATE_LABELS[s] }))]}
        />
        <FilterSelect
          label={T.list.priority}
          value={priority ?? ''}
          onChange={(value) => update({ priority: value, page: null })}
          options={[all(T.list.allPriorities), ...TICKET_PRIORITIES.map((p) => ({ value: p, label: PRIORITY_LABELS[p] }))]}
        />
        <FilterSelect
          label={T.list.origin}
          value={origin ?? ''}
          onChange={(value) => update({ origin: value, page: null })}
          options={[all(T.list.allOrigins), ...TICKET_ORIGINS.map((o) => ({ value: o, label: ORIGIN_LABELS[o] }))]}
        />
      </Toolbar>

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
          {canManage && <p className="kanban__hint">{T.list.dragHint}</p>}
          <div className="kanban" role="region" aria-label={T.list.boardLabel} tabIndex={0}>
            {TICKET_STATES.map((column) => {
              const cards = items.filter((t) => t.state === column)
              const droppable = dragged !== null && boardTargets(dragged.state).includes(column)
              return (
                <section
                  key={column}
                  className={`kanban__column${droppable ? ' kanban__column--droppable' : ''}`}
                  aria-labelledby={`col-${column}`}
                  onDragOver={(event) => {
                    // Without preventDefault the browser refuses the drop, so other columns stay closed.
                    if (droppable) event.preventDefault()
                  }}
                  onDrop={(event) => {
                    event.preventDefault()
                    dropOn(column)
                  }}
                >
                  <h3 id={`col-${column}`}>
                    <span>{STATE_LABELS[column]}</span>
                    <span className="kanban__count">{cards.length}</span>
                  </h3>
                  {cards.length === 0 && <p className="kanban__empty">{T.list.columnEmpty}</p>}
                  <ul className="kanban__cards">
                    {cards.map((t) => (
                      <li
                        key={t.id}
                        className={`kanban__card kanban__card--${t.priority.toLowerCase()}`}
                        draggable={canManage && !mover.busy && boardTargets(t.state).length > 0}
                        onDragStart={(event) => startDrag(event, t)}
                        onDragEnd={() => setDragged(null)}
                      >
                        <Link to={`/tickets/${t.id}`} className="kanban__card-title" draggable={false}>
                          {t.title}
                        </Link>
                        {installationName(t.installationId) && <span className="kanban__card-meta">{installationName(t.installationId)}</span>}
                        <div className="kanban__card-badges">
                          <PriorityBadge priority={t.priority} />
                          {t.origin === 'AnomalyDetected' && <span className="badge badge--info">{ORIGIN_LABELS[t.origin]}</span>}
                          <time className="kanban__card-time" dateTime={t.createdAt}>
                            {formatRelative(t.createdAt)}
                          </time>
                        </div>
                        {canManage && (
                          <div role="group" aria-label={T.list.moveLabel(t.title)} className="kanban__moves">
                            {boardTargets(t.state).map((target) => (
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
