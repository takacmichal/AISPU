import { useCallback, useState, type FormEvent } from 'react'
import { useSearchParams } from 'react-router-dom'
import { api } from '../../api/client'
import { problemToApiError, userMessage, type FieldErrors } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { useConfirm } from '../../components/confirm'
import { DataTable, type Column } from '../../components/DataTable'
import { Dialog } from '../../components/Dialog'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { formatCurrency, formatNumber } from '../../lib/format'
import {
  COMPONENT_TYPES,
  TYPE_LABELS,
  emptyComponent,
  fromComponent,
  parsePrice,
  toComponentRequest,
  validateComponent,
  type CatalogComponent,
  type ComponentFormValues,
  type ComponentType,
} from './catalogForm'

const PAGE_SIZE = 20
/** Mirrors ComponentsModule.ReadRoles / WriteRoles; the backend stays the source of truth. */
const READ_ROLES = ['Admin', 'Sales', 'Designer', 'Operator']
const WRITE_ROLES = ['Admin', 'Sales']

type DialogState = { component?: CatalogComponent } | null

/** Component and price catalog. Route: /catalog. Visible to Admin, Sales, Designer (and Operator); edits for Admin and Sales. */
export default function CatalogPage() {
  const { roles } = useAuth()
  const toast = useToast()
  const confirm = useConfirm()
  const [params, setParams] = useSearchParams()
  const [dialog, setDialog] = useState<DialogState>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  const canRead = roles.some((r) => READ_ROLES.includes(r))
  const canWrite = roles.some((r) => WRITE_ROLES.includes(r))

  const q = params.get('q') ?? ''
  const typeParam = params.get('type') ?? ''
  const type = (COMPONENT_TYPES as readonly string[]).includes(typeParam) ? (typeParam as ComponentType) : ''
  const activeParam = params.get('active')
  const active = activeParam === 'true' || activeParam === 'false' ? activeParam : ''
  const page = Math.max(1, Number(params.get('page')) || 1)

  const { data, loading, error, reload } = useLoad(
    async (signal) => {
      if (!canRead) return []
      const { data, response } = await api.GET('/api/components', {
        params: { query: { q: q || undefined, type: type || undefined, active: active === '' ? undefined : active === 'true' } },
        signal,
      })
      if (!data) throw new Error(`HTTP ${response.status}`)
      return data
    },
    JSON.stringify([q, type, active, canRead]),
  )

  function update(changes: Record<string, string | null>) {
    const next = new URLSearchParams(params)
    for (const [key, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(key)
      else next.set(key, value)
    }
    setParams(next)
  }

  async function toggleActive(component: CatalogComponent) {
    const activating = !component.isActive
    if (!activating) {
      const ok = await confirm({
        title: 'Deaktivovať komponent',
        message: `Komponent ${component.code} sa prestane ponúkať v nových ponukách. Pokračovať?`,
        confirmLabel: 'Deaktivovať',
        danger: true,
      })
      if (!ok) return
    }
    setBusyId(component.id)
    try {
      const path = activating ? '/api/components/{id}/activate' : '/api/components/{id}/deactivate'
      const { data: updated, response, error: problem } = await api.POST(path, { params: { path: { id: component.id } } })
      if (!updated) throw problemToApiError(response.status, problem)
      toast.success(activating ? 'Komponent bol aktivovaný.' : 'Komponent bol deaktivovaný.')
      reload()
    } catch (e) {
      toast.error(userMessage(e))
    } finally {
      setBusyId(null)
    }
  }

  const columns: Column<CatalogComponent>[] = [
    { key: 'code', header: 'Kód', render: (c) => <span className="mono">{c.code}</span> },
    { key: 'name', header: 'Názov', render: (c) => c.name },
    { key: 'type', header: 'Typ', render: (c) => TYPE_LABELS[c.type] ?? c.type },
    { key: 'manufacturer', header: 'Výrobca', render: (c) => c.manufacturer ?? '–' },
    { key: 'power', header: 'Výkon', render: (c) => (c.powerW === null || c.powerW === undefined ? '–' : `${formatNumber(c.powerW, 0)}\u00a0W`) },
    { key: 'price', header: 'Cena', render: (c) => formatCurrency(c.unitPrice, c.currency) },
    {
      key: 'status',
      header: 'Dostupnosť',
      render: (c) => <span className={`badge ${c.isActive ? 'badge--success' : ''}`}>{c.isActive ? 'Aktívny' : 'Neaktívny'}</span>,
    },
    ...(canWrite
      ? [
          {
            key: 'actions',
            header: 'Akcie',
            render: (c: CatalogComponent) => (
              <>
                <button type="button" className="btn btn--ghost btn--sm" onClick={() => setDialog({ component: c })}>
                  Upraviť
                </button>{' '}
                <button type="button" className="btn btn--ghost btn--sm" disabled={busyId === c.id} onClick={() => void toggleActive(c)}>
                  {c.isActive ? 'Deaktivovať' : 'Aktivovať'}
                </button>
              </>
            ),
          },
        ]
      : []),
  ]

  if (!canRead) {
    return (
      <section>
        <h2>Katalóg komponentov</h2>
        <p role="alert" className="alert">
          Na zobrazenie katalógu nemáte oprávnenie.
        </p>
      </section>
    )
  }

  const items = data ?? []
  const pageItems = items.slice((page - 1) * PAGE_SIZE, page * PAGE_SIZE)
  const filtered = q !== '' || type !== '' || active !== ''

  return (
    <section>
      <div className="toolbar">
        <h2>Katalóg komponentov</h2>
        {canWrite && (
          <button type="button" className="btn btn--primary" onClick={() => setDialog({})}>
            Nový komponent
          </button>
        )}
      </div>
      <div className="toolbar">
        <label className="data-table__filter">
          Typ
          <select value={type} onChange={(e) => update({ type: e.target.value, page: null })}>
            <option value="">Všetky typy</option>
            {COMPONENT_TYPES.map((t) => (
              <option key={t} value={t}>
                {TYPE_LABELS[t]}
              </option>
            ))}
          </select>
        </label>
        <label className="data-table__filter">
          Dostupnosť
          <select value={active} onChange={(e) => update({ active: e.target.value, page: null })}>
            <option value="">Všetky</option>
            <option value="true">Aktívne</option>
            <option value="false">Neaktívne</option>
          </select>
        </label>
      </div>
      <DataTable
        columns={columns}
        rows={pageItems}
        rowKey={(c) => c.id}
        page={page}
        pageSize={PAGE_SIZE}
        totalCount={items.length}
        onPageChange={(next) => update({ page: next === 1 ? null : String(next) })}
        filter={q}
        onFilterChange={(value) => update({ q: value.trim(), page: null })}
        filterLabel="Hľadať podľa kódu, názvu alebo výrobcu"
        loading={loading}
        error={error ? 'Katalóg sa nepodarilo načítať.' : null}
        emptyMessage={filtered ? 'Nenašli sme žiadny komponent.' : 'Katalóg zatiaľ neobsahuje žiadne komponenty.'}
      />
      {dialog && (
        <ComponentDialog
          component={dialog.component}
          onClose={() => setDialog(null)}
          onSaved={() => {
            setDialog(null)
            toast.success('Komponent bol uložený.')
            reload()
          }}
        />
      )}
    </section>
  )
}

function ComponentDialog({
  component,
  onClose,
  onSaved,
}: {
  component?: CatalogComponent
  onClose: () => void
  onSaved: () => void
}) {
  const confirm = useConfirm()
  const [values, setValues] = useState<ComponentFormValues>(component ? fromComponent(component) : emptyComponent)
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const save = useCallback(async () => {
    const body = toComponentRequest(values)
    const result = component
      ? await api.PUT('/api/components/{id}', { params: { path: { id: component.id } }, body })
      : await api.POST('/api/components', { body })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    onSaved()
  }, [values, component, onSaved])

  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  function set(name: keyof ComponentFormValues, value: string) {
    setValues((current) => ({ ...current, [name]: value }))
    setClientErrors((current) => {
      const rest = { ...current }
      delete rest[name]
      return rest
    })
    clearFieldError(name)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateComponent(values)
    setClientErrors(errors)
    if (Object.keys(errors).length > 0) return
    if (component && parsePrice(values.unitPrice) !== Number(component.unitPrice)) {
      const ok = await confirm({
        title: 'Zmena ceny',
        message: `Cena sa zmení z ${formatCurrency(component.unitPrice, component.currency)} na ${formatCurrency(parsePrice(values.unitPrice), component.currency)}. Platí len pre nové ponuky, existujúce ponuky si ponechajú pôvodnú cenu.`,
        confirmLabel: 'Zmeniť cenu',
      })
      if (!ok) return
    }
    await submit()
  }

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  const text = (name: keyof ComponentFormValues, label: string, inputMode?: 'numeric' | 'decimal') => (
    <FormField label={label} errors={errorsFor(name)}>
      {(props) => <input {...props} type="text" inputMode={inputMode} value={values[name]} onChange={(e) => set(name, e.target.value)} />}
    </FormField>
  )

  return (
    <Dialog title={component ? 'Upraviť komponent' : 'Nový komponent'} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        <FormField label="Typ" errors={errorsFor('type')}>
          {(props) => (
            <select {...props} value={values.type} onChange={(e) => set('type', e.target.value)}>
              {COMPONENT_TYPES.map((t) => (
                <option key={t} value={t}>
                  {TYPE_LABELS[t]}
                </option>
              ))}
            </select>
          )}
        </FormField>
        {text('code', 'Kód')}
        {text('name', 'Názov')}
        {text('manufacturer', 'Výrobca (voliteľný)')}
        {values.type !== 'Labor' && text('powerW', 'Výkon (W)', 'numeric')}
        {text('unitPrice', values.type === 'Labor' ? 'Cena za hodinu (€)' : 'Cena za kus (€)', 'decimal')}
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="dialog__actions">
          <button type="button" className="btn btn--ghost" onClick={onClose}>
            Zrušiť
          </button>
          <button type="submit" className="btn btn--primary" disabled={busy}>
            Uložiť
          </button>
        </div>
      </form>
    </Dialog>
  )
}
