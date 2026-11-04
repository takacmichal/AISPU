import { useCallback, useRef, useState, type ChangeEvent, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import type { FieldErrors } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { Skeleton } from '../../components/Skeleton'
import { FormField } from '../../components/forms'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { useLoad } from '../../components/useLoad'
import { useUnsavedChanges } from '../../components/useUnsavedChanges'
import { PRIORITY_LABELS, T } from './messages'
import { emptyTicketValues, toTicketRequest, validateTicket, type TicketFormValues } from './ticketForm'
import './tickets.css'
import { MAX_ATTACHMENTS, TICKET_PRIORITIES, createTicket, listInstallationOptions, type TicketPriority } from './ticketsApi'

/** `/tickets/new`: fault report (customer) or manual ticket (staff, with optional priority). */
export default function TicketCreatePage() {
  const navigate = useNavigate()
  const toast = useToast()
  const { roles } = useAuth()
  const staff = roles.some((r) => r !== 'Customer')
  const [values, setValues] = useState<TicketFormValues>(emptyTicketValues)
  const [clientErrors, setClientErrors] = useState<FieldErrors>({})
  const saved = useRef(false)

  const installations = useLoad(listInstallationOptions, 'installations')

  const dirty = JSON.stringify({ ...values, files: values.files.length }) !== JSON.stringify({ ...emptyTicketValues, files: 0 })
  useUnsavedChanges(useCallback(() => dirty && !saved.current, [dirty]))

  const save = useCallback(async () => {
    const created = await createTicket(await toTicketRequest(values))
    saved.current = true
    toast.success(T.create.created)
    navigate(`/tickets/${created.id}`)
  }, [values, navigate, toast])
  const { busy, fieldErrors, formError, submit, clearFieldError } = useFormSubmit(save)

  function set<K extends keyof TicketFormValues>(name: K, value: TicketFormValues[K]) {
    setValues((current) => ({ ...current, [name]: value }))
    setClientErrors((current) => {
      const rest = { ...current }
      delete rest[name]
      return rest
    })
    clearFieldError(name)
  }

  function onFiles(event: ChangeEvent<HTMLInputElement>) {
    const picked = Array.from(event.target.files ?? [])
    event.target.value = ''
    set('files', [...values.files, ...picked])
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    const errors = validateTicket(values)
    setClientErrors(errors)
    if (Object.keys(errors).length === 0) await submit()
  }

  // Server keys for attachments look like "attachments[0].contentType"; show them next to the file input.
  const serverAttachmentErrors = Object.entries(fieldErrors)
    .filter(([key]) => key.startsWith('attachments'))
    .flatMap(([, messages]) => messages)
  const errorsFor = (field: string) => [...(clientErrors[field] ?? []), ...(fieldErrors[field] ?? [])]

  if (installations.loading) return <Skeleton lines={5} label={T.detail.loading} />

  return (
    <section>
      <h2>{staff ? T.create.titleStaff : T.create.title}</h2>
      {installations.error && (
        <p role="alert" className="alert">
          {T.create.installationsError}
        </p>
      )}
      {!installations.error && installations.data?.length === 0 && <p role="note">{T.create.installationsEmpty}</p>}
      <form onSubmit={onSubmit} noValidate>
        <FormField label={T.create.installation} errors={errorsFor('installationId')}>
          {(props) => (
            <select {...props} value={values.installationId} onChange={(e) => set('installationId', e.target.value)}>
              <option value="">{T.create.installationPlaceholder}</option>
              {(installations.data ?? []).map((i) => (
                <option key={i.id} value={i.id}>
                  {i.name}
                </option>
              ))}
            </select>
          )}
        </FormField>
        <FormField label={T.create.name} errors={errorsFor('title')}>
          {(props) => <input {...props} type="text" value={values.title} onChange={(e) => set('title', e.target.value)} />}
        </FormField>
        <FormField label={T.create.description} errors={errorsFor('description')}>
          {(props) => <textarea {...props} rows={5} value={values.description} onChange={(e) => set('description', e.target.value)} />}
        </FormField>
        {staff && (
          <FormField label={T.create.priority} errors={errorsFor('priority')}>
            {(props) => (
              <select {...props} value={values.priority} onChange={(e) => set('priority', e.target.value as TicketPriority | '')}>
                <option value="">{T.create.priorityDefault}</option>
                {TICKET_PRIORITIES.map((p) => (
                  <option key={p} value={p}>
                    {PRIORITY_LABELS[p]}
                  </option>
                ))}
              </select>
            )}
          </FormField>
        )}
        <FormField label={T.create.attachments} errors={[...errorsFor('attachments'), ...serverAttachmentErrors]}>
          {(props) => (
            <input {...props} type="file" multiple accept="image/jpeg,image/png,application/pdf" onChange={onFiles} disabled={values.files.length >= MAX_ATTACHMENTS} />
          )}
        </FormField>
        {values.files.length > 0 && (
          <ul className="file-list">
            {values.files.map((file, index) => (
              <li key={`${file.name}-${index}`}>
                <span>{file.name}</span>
                <button
                  type="button"
                  className="btn btn--ghost btn--sm"
                  aria-label={T.create.removeAttachment(file.name)}
                  onClick={() => set('files', values.files.filter((_, i) => i !== index))}
                >
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}
        {formError && (
          <p role="alert" className="form-error">
            {formError}
          </p>
        )}
        <div className="btn-row">
          <button type="submit" className="btn btn--primary" disabled={busy}>
            {T.create.submit}
          </button>
          <button type="button" className="btn btn--ghost" onClick={() => navigate('/tickets')}>
            {T.create.cancel}
          </button>
        </div>
      </form>
    </section>
  )
}
