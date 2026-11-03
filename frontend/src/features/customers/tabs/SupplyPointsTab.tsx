import { useCallback, useState, type FormEvent } from 'react'
import { api } from '../../../api/client'
import type { components } from '../../../api/generated/schema'
import { problemToApiError, type FieldErrors } from '../../../api/problem'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import { Dialog } from '../../../components/Dialog'
import { EmptyState } from '../../../components/EmptyState'
import { FormField } from '../../../components/forms'
import { useToast } from '../../../components/toast'
import { useFormSubmit } from '../../../components/useFormSubmit'
import { Skeleton } from '../../../components/Skeleton'
import { useLoad } from '../../../components/useLoad'
import {
  emptySupplyPoint,
  fromSupplyPoint,
  toSupplyPointRequest,
  validateSupplyPoint,
  type SupplyPointFormValues,
} from '../supplyPointForm'

type SupplyPoint = components['schemas']['SupplyPointResponse']

type DialogState = { kind: 'form'; supplyPoint?: SupplyPoint } | { kind: 'delete'; supplyPoint: SupplyPoint } | null

export default function SupplyPointsTab({ customerId }: { customerId: string }) {
  const toast = useToast()
  const [dialog, setDialog] = useState<DialogState>(null)

  const { data, loading, error, reload } = useLoad(async (signal) => {
    const { data, response } = await api.GET('/api/customers/{customerId}/supply-points', {
      params: { path: { customerId } },
      signal,
    })
    if (!data) throw new Error(`HTTP ${response.status}`)
    return data
  }, customerId)

  function done(message: string) {
    setDialog(null)
    toast.success(message)
    reload()
  }

  return (
    <div>
      <p>
        <button type="button" className="btn btn--primary" onClick={() => setDialog({ kind: 'form' })}>
          Pridať odberné miesto
        </button>
      </p>
      {loading && <Skeleton lines={3} label="Načítavam odberné miesta…" />}
      {error && (
        <p role="alert" className="alert">
          Odberné miesta sa nepodarilo načítať.
        </p>
      )}
      {data && data.length === 0 && <EmptyState title="Zákazník zatiaľ nemá žiadne odberné miesto." />}
      {data && data.length > 0 && (
        <div className="data-table__scroll">
<table>
          <thead>
            <tr>
              <th scope="col">Názov</th>
              <th scope="col">EIC</th>
              <th scope="col">Adresa</th>
              <th scope="col">Hlavný istič</th>
              <th scope="col">Akcie</th>
            </tr>
          </thead>
          <tbody>
            {data.map((sp) => (
              <tr key={sp.id}>
                <td>{sp.name ?? '–'}</td>
                <td className="mono">{sp.eicCode}</td>
                <td>{`${sp.address.street}, ${sp.address.postalCode} ${sp.address.city}`}</td>
                <td>{sp.mainBreakerAmps} A</td>
                <td>
                  <button type="button" className="btn btn--ghost btn--sm" onClick={() => setDialog({ kind: 'form', supplyPoint: sp })}>
                    Upraviť
                  </button>{' '}
                  <button type="button" className="btn btn--danger btn--sm" onClick={() => setDialog({ kind: 'delete', supplyPoint: sp })}>
                    Zmazať
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
</div>
      )}

      {dialog?.kind === 'form' && (
        <SupplyPointDialog
          customerId={customerId}
          supplyPoint={dialog.supplyPoint}
          onClose={() => setDialog(null)}
          onSaved={() => done('Odberné miesto bolo uložené.')}
        />
      )}
      {dialog?.kind === 'delete' && (
        <DeleteDialog
          customerId={customerId}
          supplyPoint={dialog.supplyPoint}
          onClose={() => setDialog(null)}
          onDeleted={() => done('Odberné miesto bolo zmazané.')}
        />
      )}
    </div>
  )
}

function SupplyPointDialog({
  customerId,
  supplyPoint,
  onClose,
  onSaved,
}: {
  customerId: string
  supplyPoint?: SupplyPoint
  onClose: () => void
  onSaved: () => void
}) {
  const [values, setValues] = useState<SupplyPointFormValues>(supplyPoint ? fromSupplyPoint(supplyPoint) : emptySupplyPoint)
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})

  const save = useCallback(async () => {
    const body = toSupplyPointRequest(values)
    const result = supplyPoint
      ? await api.PUT('/api/customers/{customerId}/supply-points/{id}', { params: { path: { customerId, id: supplyPoint.id } }, body })
      : await api.POST('/api/customers/{customerId}/supply-points', { params: { path: { customerId } }, body })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    onSaved()
  }, [values, supplyPoint, customerId, onSaved])

  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  function set(name: keyof SupplyPointFormValues, value: string, errorKey: string = name) {
    setValues((current) => ({ ...current, [name]: value }))
    setClientErrors((current) => {
      const rest = { ...current }
      delete rest[errorKey]
      return rest
    })
    clearFieldError(errorKey)
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateSupplyPoint(values)
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  const field = (name: keyof SupplyPointFormValues, label: string, errorKey: string = name, type = 'text') => (
    <FormField label={label} errors={errorsFor(errorKey)}>
      {(props) => <input {...props} type={type} value={values[name]} onChange={(e) => set(name, e.target.value, errorKey)} />}
    </FormField>
  )

  return (
    <Dialog title={supplyPoint ? 'Upraviť odberné miesto' : 'Nové odberné miesto'} onClose={onClose}>
      <form onSubmit={onSubmit} noValidate>
        {field('name', 'Názov (voliteľný)')}
        {field('eicCode', 'EIC kód')}
        {field('street', 'Ulica a číslo', 'address.street')}
        {field('city', 'Mesto', 'address.city')}
        {field('postalCode', 'PSČ', 'address.postalCode')}
        {field('country', 'Krajina (kód)', 'address.country')}
        {field('mainBreakerAmps', 'Hlavný istič (A)', 'mainBreakerAmps', 'number')}
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

function DeleteDialog({
  customerId,
  supplyPoint,
  onClose,
  onDeleted,
}: {
  customerId: string
  supplyPoint: SupplyPoint
  onClose: () => void
  onDeleted: () => void
}) {
  const remove = useCallback(async () => {
    const { response, error } = await api.DELETE('/api/customers/{customerId}/supply-points/{id}', {
      params: { path: { customerId, id: supplyPoint.id } },
    })
    if (!response.ok) throw problemToApiError(response.status, error)
    onDeleted()
  }, [customerId, supplyPoint.id, onDeleted])

  const { busy, formError, submit } = useFormSubmit(remove)

  return (
    <ConfirmDialog
      title="Zmazať odberné miesto"
      message={
        <>
          Naozaj chcete zmazať odberné miesto <strong>{supplyPoint.eicCode}</strong>?
        </>
      }
      confirmLabel="Zmazať"
      danger
      busy={busy}
      error={formError}
      onConfirm={() => void submit()}
      onCancel={onClose}
    />
  )
}
