import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../../api/client'
import { problemToApiError, type FieldErrors } from '../../api/problem'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useUnsavedChanges } from '../../components/useUnsavedChanges'
import { emptyValues, fromCustomer, toRequest, validateCustomer, type CustomerFormValues } from './customerForm'

/** Create (/customers/new) and edit (/customers/:id/edit) share one form. */
export default function CustomerFormPage() {
  const { id } = useParams()
  const [loaded, setLoaded] = useState<{ id: string; values: CustomerFormValues } | null>(null)
  const [failed, setFailed] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    const controller = new AbortController()
    api
      .GET('/api/customers/{id}', { params: { path: { id } }, signal: controller.signal })
      .then(({ data, response }) => {
        if (controller.signal.aborted) return
        if (data) setLoaded({ id, values: fromCustomer(data) })
        else setFailed(response.status === 404 ? 'Zákazník neexistuje.' : 'Zákazníka sa nepodarilo načítať.')
      })
      .catch(() => {
        if (!controller.signal.aborted) setFailed('Zákazníka sa nepodarilo načítať.')
      })
    return () => controller.abort()
  }, [id])

  if (!id) return <CustomerForm key="new" initial={emptyValues} />
  if (failed) return <p role="alert">{failed}</p>
  if (loaded?.id !== id) return <p role="status">Načítavam…</p>
  return <CustomerForm key={id} customerId={id} initial={loaded.values} />
}

function CustomerForm({ customerId, initial }: { customerId?: string; initial: CustomerFormValues }) {
  const navigate = useNavigate()
  const toast = useToast()
  const [values, setValues] = useState(initial)
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})
  const saved = useRef(false)

  const dirty = JSON.stringify(values) !== JSON.stringify(initial)
  useUnsavedChanges(useCallback(() => dirty && !saved.current, [dirty]))

  const save = useCallback(async () => {
    const body = toRequest(values)
    const result = customerId
      ? await api.PUT('/api/customers/{id}', { params: { path: { id: customerId } }, body })
      : await api.POST('/api/customers', { body })
    if (!result.data) throw problemToApiError(result.response.status, result.error)
    saved.current = true
    toast.success('Zákazník bol uložený.')
    navigate(`/customers/${result.data.id}`)
  }, [values, customerId, navigate, toast])

  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  function set<K extends keyof CustomerFormValues>(name: K, value: CustomerFormValues[K], errorKey: string = name) {
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
    const errors = validateCustomer(values)
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]
  const company = values.type === 'Company'
  const text = (name: keyof CustomerFormValues, label: string, errorKey: string = name, type = 'text') => (
    <FormField label={label} errors={errorsFor(errorKey)}>
      {(props) => (
        <input {...props} type={type} value={values[name]} onChange={(e) => set(name, e.target.value as never, errorKey)} />
      )}
    </FormField>
  )

  return (
    <section>
      <h2>{customerId ? 'Upraviť zákazníka' : 'Nový zákazník'}</h2>
      <form onSubmit={onSubmit} noValidate>
        <FormField label="Typ zákazníka" errors={errorsFor('type')}>
          {(props) => (
            <select {...props} value={values.type} onChange={(e) => set('type', e.target.value as CustomerFormValues['type'])}>
              <option value="Person">Fyzická osoba</option>
              <option value="Company">Právnická osoba</option>
            </select>
          )}
        </FormField>

        {company ? (
          <>
            {text('companyName', 'Názov firmy')}
            {text('ico', 'IČO')}
            {text('dic', 'DIČ')}
            {text('icDph', 'IČ DPH')}
          </>
        ) : (
          <>
            {text('firstName', 'Meno')}
            {text('lastName', 'Priezvisko')}
          </>
        )}

        {text('email', 'E-mail', 'email', 'email')}
        {text('phone', 'Telefón', 'phone', 'tel')}

        <fieldset>
          <legend>Adresa</legend>
          {text('street', 'Ulica a číslo', 'address.street')}
          {text('city', 'Mesto', 'address.city')}
          {text('postalCode', 'PSČ', 'address.postalCode')}
          {text('country', 'Krajina (kód)', 'address.country')}
        </fieldset>

        <FormField label="Poznámka" errors={errorsFor('note')}>
          {(props) => <textarea {...props} value={values.note} onChange={(e) => set('note', e.target.value)} />}
        </FormField>

        {formError && <p role="alert">{formError}</p>}
        <button type="submit" disabled={busy}>
          Uložiť
        </button>
        <button type="button" onClick={() => navigate(customerId ? `/customers/${customerId}` : '/customers')}>
          Zrušiť
        </button>
      </form>
    </section>
  )
}
