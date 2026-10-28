import { useCallback, useState, type FormEvent } from 'react'
import { api } from '../../../api/client'
import type { components } from '../../../api/generated/schema'
import { problemToApiError, type FieldErrors } from '../../../api/problem'
import { Dialog } from '../../../components/Dialog'
import { FormField } from '../../../components/forms'
import { useToast } from '../../../components/toast'
import { useFormSubmit } from '../../../components/useFormSubmit'
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
        <button type="button" onClick={() => setDialog({ kind: 'form' })}>
          Pridať odberné miesto
        </button>
      </p>
      {loading && <p role="status">Načítavam odberné miesta…</p>}
      {error && <p role="alert">Odberné miesta sa nepodarilo načítať.</p>}
      {data && data.length === 0 && <p>Zákazník zatiaľ nemá žiadne odberné miesto.</p>}
      {data && data.length > 0 && (
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
                <td>{sp.eicCode}</td>
                <td>{`${sp.address.street}, ${sp.address.postalCode} ${sp.address.city}`}</td>
                <td>{sp.mainBreakerAmps} A</td>
                <td>
                  <button type="button" onClick={() => setDialog({ kind: 'form', supplyPoint: sp })}>
                    Upraviť
                  </button>{' '}
                  <button type="button" onClick={() => setDialog({ kind: 'delete', supplyPoint: sp })}>
                    Zmazať
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
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
        {formError && <p role="alert">{formError}</p>}
        <button type="submit" disabled={busy}>
          Uložiť
        </button>{' '}
        <button type="button" onClick={onClose}>
          Zrušiť
        </button>
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
    <Dialog title="Zmazať odberné miesto" onClose={onClose}>
      <p>
        Naozaj chcete zmazať odberné miesto <strong>{supplyPoint.eicCode}</strong>?
      </p>
      {formError && <p role="alert">{formError}</p>}
      <button type="button" disabled={busy} onClick={() => void submit()}>
        Zmazať
      </button>{' '}
      <button type="button" onClick={onClose}>
        Zrušiť
      </button>
    </Dialog>
  )
}
