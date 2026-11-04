import { useCallback, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import type { components } from '../../api/generated/schema'
import { problemToApiError } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { ROLE_LABELS } from '../../auth/navigation'
import { DataTable, type Column } from '../../components/DataTable'
import { Dialog } from '../../components/Dialog'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'

type UserRoles = components['schemas']['UserRolesResponse']

const PAGE_SIZE = 20

const roleLabel = (role: string) => ROLE_LABELS[role] ?? role

/** User list and role assignment. Route: /admin/users. Admin only. */
export default function UsersAdminPage() {
  const { roles: myRoles } = useAuth()
  const toast = useToast()
  const [params, setParams] = useSearchParams()
  const [editing, setEditing] = useState<UserRoles | null>(null)

  const isAdmin = myRoles.includes('Admin')
  const q = params.get('q') ?? ''
  const page = Math.max(1, Number(params.get('page')) || 1)

  const { data, loading, error, reload } = useLoad(
    async (signal) => {
      if (!isAdmin) return null
      const { data, response } = await api.GET('/api/users', { params: { query: { q: q || undefined, page, pageSize: PAGE_SIZE } }, signal })
      if (!data) throw new Error(`HTTP ${response.status}`)
      return data
    },
    JSON.stringify([q, page, isAdmin]),
  )

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  if (!isAdmin) {
    return (
      <section>
        <h2>Používatelia a roly</h2>
        <p role="alert" className="alert">
          Na túto stránku nemáte oprávnenie.
        </p>
      </section>
    )
  }

  const columns: Column<UserRoles>[] = [
    { key: 'email', header: 'E-mail', render: (u) => u.email },
    {
      key: 'roles',
      header: 'Roly',
      render: (u) => u.roles.map((r) => (
        <span key={r} className="badge badge--info">
          {roleLabel(r)}
        </span>
      )),
    },
    {
      key: 'actions',
      header: 'Akcie',
      render: (u) => (
        <button type="button" className="btn btn--ghost btn--sm" onClick={() => setEditing(u)} aria-label={`Upraviť roly používateľa ${u.email}`}>
          Upraviť roly
        </button>
      ),
    },
  ]

  return (
    <section>
      <div className="toolbar">
        <h2>Používatelia a roly</h2>
      </div>
      <DataTable
        columns={columns}
        rows={data?.items ?? []}
        rowKey={(u) => u.id}
        page={page}
        pageSize={PAGE_SIZE}
        totalCount={Number(data?.totalCount ?? 0)}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        filter={q}
        onFilterChange={(value) => update({ q: value.trim(), page: null })}
        filterLabel="Hľadať podľa e-mailu"
        loading={loading}
        error={error ? 'Zoznam používateľov sa nepodarilo načítať.' : null}
        emptyMessage={q ? 'Nenašli sme žiadneho používateľa.' : 'Zatiaľ nie sú evidovaní žiadni používatelia.'}
      />
      {editing && (
        <RolesDialog
          user={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null)
            toast.success('Roly boli uložené.')
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
          <fieldset aria-describedby={rolesError ? 'roles-error' : undefined}>
            <legend>Roly</legend>
            {allRoles.map((role) => (
              <label key={role} className="checkbox">
                <input type="checkbox" checked={selected.includes(role)} onChange={() => toggle(role)} /> {roleLabel(role)}
              </label>
            ))}
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
          <button type="submit" className="btn btn--primary" disabled={busy || !allRoles}>
            Uložiť
          </button>
        </div>
      </form>
    </Dialog>
  )
}
