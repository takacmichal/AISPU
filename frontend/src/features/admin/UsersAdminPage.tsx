import { useCallback, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { ROLE_LABELS } from '../../auth/navigation'
import { Avatar } from '../../components/Avatar'
import { DataTable, type Column } from '../../components/DataTable'
import { Dialog } from '../../components/Dialog'
import { PageHeader } from '../../components/PageHeader'
import { SegmentedControl } from '../../components/SegmentedControl'
import { SearchField, Toolbar } from '../../components/Toolbar'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import './admin.css'

type UserRoles = components['schemas']['UserRolesResponse']

const PAGE_SIZE = 20
/** Largest page the API allows; the admin loads every user so that the tabs can count them. */
const LOAD_PAGE_SIZE = 100
const MAX_PAGES = 20

const TABS = ['all', 'staff', 'customers', 'none'] as const
type Tab = (typeof TABS)[number]

const TEXT = {
  title: 'Používatelia a roly',
  subtitle: (users: number) => `${users} ${users === 1 ? 'používateľ' : users >= 2 && users <= 4 ? 'používatelia' : 'používateľov'} · 6 rolí`,
  forbidden: 'Na túto stránku nemáte oprávnenie.',
  search: 'Hľadať podľa e-mailu',
  searchPlaceholder: 'E-mail…',
  tabsLabel: 'Typ účtu',
  tabs: { all: 'Všetci', staff: 'Interní', customers: 'Zákazníci', none: 'Bez roly' } satisfies Record<Tab, string>,
  colUser: 'Používateľ',
  colType: 'Typ účtu',
  colRoles: 'Roly',
  colActions: 'Akcie',
  typeStaff: 'Interný',
  typeCustomer: 'Zákazník',
  typeNone: 'Bez roly',
  edit: 'Upraviť roly',
  editLabel: (email: string) => `Upraviť roly používateľa ${email}`,
  error: 'Zoznam používateľov sa nepodarilo načítať.',
  empty: 'Zatiaľ nie sú evidovaní žiadni používatelia.',
  emptyFiltered: 'Nenašli sme žiadneho používateľa.',
  saved: 'Roly boli uložené.',
}

/** What each role can do; shown in the role dialog so the admin does not have to guess. */
const ROLE_DESCRIPTIONS: Record<string, string> = {
  Admin: 'Správa používateľov a prístup ku všetkým modulom.',
  Sales: 'Zákazníci, ponuky a zmluvy.',
  Designer: 'Návrhy FVE, katalóg a plán inštalácií.',
  Technician: 'Montáže, servisné výjazdy a protokoly.',
  Operator: 'Monitoring výroby, tikety a plánovanie servisu.',
  Customer: 'Zákaznícky portál: vlastné elektrárne a tikety.',
}

const roleLabel = (role: string) => ROLE_LABELS[role] ?? role

function accountType(user: UserRoles): Exclude<Tab, 'all'> {
  if (user.roles.length === 0) return 'none'
  return user.roles.some((r) => r !== 'Customer') ? 'staff' : 'customers'
}

async function loadAllUsers(signal: AbortSignal): Promise<UserRoles[]> {
  const users: UserRoles[] = []
  for (let page = 1; page <= MAX_PAGES; page++) {
    const { data, response } = await api.GET('/api/users', { params: { query: { page, pageSize: LOAD_PAGE_SIZE } }, signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    users.push(...data.items)
    if (data.items.length === 0 || users.length >= Number(data.totalCount)) break
  }
  return users
}

/**
 * `/admin/users` (wireframe "Používatelia a roly"), Admin only: users with avatar, account type tabs with counts,
 * e-mail search and role assignment in a dialog. Search, tabs and paging are in the URL.
 */
export default function UsersAdminPage() {
  const { roles: myRoles } = useAuth()
  const toast = useToast()
  const [params, setParams] = useSearchParams()
  const [editing, setEditing] = useState<UserRoles | null>(null)

  const isAdmin = myRoles.includes('Admin')
  const q = params.get('q') ?? ''
  const tab: Tab = TABS.find((t) => t === params.get('type')) ?? 'all'
  const page = Math.max(1, Number(params.get('page')) || 1)

  const { data, loading, error, reload } = useLoad(async (signal) => (isAdmin ? loadAllUsers(signal) : null), `users|${isAdmin}`)

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '' || value === 'all') next.delete(key)
      else next.set(key, value)
    }
    setParams(next, { replace: true })
  }

  if (!isAdmin) {
    return (
      <section>
        <PageHeader title={TEXT.title} />
        <p role="alert" className="alert">
          {TEXT.forbidden}
        </p>
      </section>
    )
  }

  const all = data ?? []
  const needle = q.trim().toLowerCase()
  const searched = needle ? all.filter((u) => u.email.toLowerCase().includes(needle)) : all
  const counts: Record<Tab, number> = {
    all: searched.length,
    staff: searched.filter((u) => accountType(u) === 'staff').length,
    customers: searched.filter((u) => accountType(u) === 'customers').length,
    none: searched.filter((u) => accountType(u) === 'none').length,
  }
  const rows = tab === 'all' ? searched : searched.filter((u) => accountType(u) === tab)
  const current = Math.min(page, Math.max(1, Math.ceil(rows.length / PAGE_SIZE)))
  const typeLabel = { staff: TEXT.typeStaff, customers: TEXT.typeCustomer, none: TEXT.typeNone }

  const columns: Column<UserRoles>[] = [
    {
      key: 'user',
      header: TEXT.colUser,
      render: (u) => (
        <span className="user-cell">
          <Avatar name={u.email} size="sm" />
          <span className="user-cell__email">{u.email}</span>
        </span>
      ),
    },
    { key: 'type', header: TEXT.colType, render: (u) => typeLabel[accountType(u)] },
    {
      key: 'roles',
      header: TEXT.colRoles,
      render: (u) =>
        u.roles.length === 0 ? (
          <span className="badge badge--muted">{TEXT.typeNone}</span>
        ) : (
          <span className="user-roles">
            {u.roles.map((r) => (
              <span key={r} className="badge badge--info">
                {roleLabel(r)}
              </span>
            ))}
          </span>
        ),
    },
    {
      key: 'actions',
      header: TEXT.colActions,
      render: (u) => (
        <button type="button" className="btn btn--ghost btn--sm" onClick={() => setEditing(u)} aria-label={TEXT.editLabel(u.email)}>
          {TEXT.edit}
        </button>
      ),
    },
  ]

  return (
    <section>
      <PageHeader title={TEXT.title} subtitle={data ? TEXT.subtitle(all.length) : undefined} />
      <div className="users-tabs">
        <SegmentedControl<Tab>
          label={TEXT.tabsLabel}
          value={tab}
          onChange={(next) => update({ type: next, page: null })}
          options={TABS.filter((t) => t !== 'none' || counts.none > 0).map((t) => ({ value: t, label: TEXT.tabs[t], count: counts[t] }))}
        />
      </div>
      <Toolbar>
        <SearchField label={TEXT.search} placeholder={TEXT.searchPlaceholder} value={q} onChange={(value) => update({ q: value, page: null })} />
      </Toolbar>
      <DataTable
        columns={columns}
        rows={rows.slice((current - 1) * PAGE_SIZE, current * PAGE_SIZE)}
        rowKey={(u) => u.id}
        page={current}
        pageSize={PAGE_SIZE}
        totalCount={rows.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        loading={loading}
        error={error ? TEXT.error : null}
        emptyMessage={needle || tab !== 'all' ? TEXT.emptyFiltered : TEXT.empty}
      />
      {editing && (
        <RolesDialog
          user={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            toast.success(TEXT.saved)
            reload()
          }}
        />
      )}
    </section>
  )
}

function RolesDialog({ user, onClose, onSaved }: { user: UserRoles; onClose: () => void; onSaved: () => void }) {
  const [selected, setSelected] = useState<string[]>(user.roles)
  const [clientError, setClientError] = useState<string | null>(null)

  const { data: allRoles, loading, error } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/roles', { signal })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, 'roles')

  const save = useCallback(async () => {
    const { data, response, error: problem } = await api.PUT('/api/users/{id}/roles', {
      params: { path: { id: user.id } },
      body: { roles: selected },
    })
    if (!data) throw problemToApiError(response.status, problem)
    onSaved()
  }, [user.id, selected, onSaved])

  const { busy, fieldErrors, formError, submit } = useFormSubmit(save)

  function toggle(role: string) {
    setClientError(null)
    setSelected((current) => (current.includes(role) ? current.filter((r) => r !== role) : [...current, role]))
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (selected.length === 0) {
      setClientError('Vyberte aspoň jednu rolu.')
      return
    }
    await submit()
  }

  const rolesError = clientError ?? fieldErrors.roles?.join(' ') ?? null

  return (
    <Dialog title={`Roly používateľa ${user.email}`} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        {loading && <p role="status">Načítavam roly…</p>}
        {error && (
          <p role="alert" className="form-error">
            Roly sa nepodarilo načítať.
          </p>
        )}
        {allRoles && (
          <fieldset className="role-options" aria-describedby={rolesError ? 'roles-error' : undefined}>
            <legend className="sr-only">Roly</legend>
            {allRoles.map((role) => {
              const checked = selected.includes(role)
              return (
                <label key={role} className={checked ? 'role-option role-option--checked' : 'role-option'}>
                  <input
                    type="checkbox"
                    checked={checked}
                    onChange={() => toggle(role)}
                    aria-labelledby={`role-${role}-name`}
                    aria-describedby={ROLE_DESCRIPTIONS[role] ? `role-${role}-text` : undefined}
                  />
                  <span className="role-option__text">
                    <span id={`role-${role}-name`} className="role-option__name">
                      {roleLabel(role)}
                    </span>
                    {ROLE_DESCRIPTIONS[role] && (
                      <span id={`role-${role}-text`} className="role-option__description">
                        {ROLE_DESCRIPTIONS[role]}
                      </span>
                    )}
                  </span>
                </label>
              )
            })}
          </fieldset>
        )}
        {rolesError && (
          <p id="roles-error" role="alert" className="form-field__error">
            {rolesError}
          </p>
        )}
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="dialog__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            Zrušiť
          </button>
          <button type="submit" className="btn btn--primary" disabled={busy || !allRoles} aria-busy={busy}>
            Uložiť
          </button>
        </div>
      </form>
    </Dialog>
  )
}
