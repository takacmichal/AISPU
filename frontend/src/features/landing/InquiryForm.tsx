import { useEffect, useRef, useState, type FormEvent } from 'react'
import { FormErrorSummary } from '../../components/FormErrorSummary'
import { FormField } from '../../components/forms'
import { useFocusFirstInvalid } from '../auth/formHelpers'
import { withoutField, type FieldErrors } from '../auth/validation'
import { EMPTY_INQUIRY, toInquiry, validateInquiry, type InquiryDraft, type SendInquiry } from './inquiry'
import { LANDING_TEXTS } from './texts'

const T = LANDING_TEXTS.inquiry

/** Inquiry form from the landing page with its three states: empty, with errors and sent. */
export function InquiryForm({ onSubmit }: { onSubmit: SendInquiry }) {
  const [draft, setDraft] = useState<InquiryDraft>(EMPTY_INQUIRY)
  const [errors, setErrors] = useState<FieldErrors>({})
  const [failed, setFailed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [sent, setSent] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const formRef = useFocusFirstInvalid(attempt)
  const sentRef = useRef<HTMLHeadingElement>(null)

  useEffect(() => {
    if (sent) sentRef.current?.focus()
  }, [sent])

  function change(field: keyof InquiryDraft, value: string) {
    setDraft((current) => ({ ...current, [field]: value }))
    setErrors((current) => withoutField(current, field))
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setFailed(false)
    const found = validateInquiry(draft)
    setErrors(found)
    if (Object.keys(found).length > 0) {
      setAttempt((n) => n + 1)
      return
    }
    setBusy(true)
    try {
      await onSubmit(toInquiry(draft))
      setSent(true)
    } catch {
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  if (sent) {
    return (
      <div role="status" className="inquiry-sent">
        <span className="inquiry-sent__icon" aria-hidden="true">
          <svg width="26" height="26" viewBox="0 0 26 26" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M6 13.5l5 5 9-11" />
          </svg>
        </span>
        <h3 ref={sentRef} tabIndex={-1} className="inquiry-sent__title">
          {T.sentTitle}
        </h3>
        <p className="inquiry-sent__text">{T.sentText}</p>
        <button
          type="button"
          className="btn btn--pill"
          onClick={() => {
            setDraft(EMPTY_INQUIRY)
            setSent(false)
          }}
        >
          {T.another}
        </button>
      </div>
    )
  }

  const field = (name: keyof InquiryDraft) => ({
    value: draft[name],
    onChange: (event: { target: { value: string } }) => change(name, event.target.value),
  })

  return (
    <form ref={formRef} className="inquiry-form" onSubmit={submit} noValidate>
      <FormErrorSummary errors={Object.values(errors).flat()} />
      {failed && (
        <p role="alert" className="form-summary">
          {T.failed}
        </p>
      )}
      <div className="inquiry-form__row">
        <FormField label={T.name} required errors={errors.name}>
          {(props) => <input {...props} {...field('name')} type="text" required autoComplete="name" />}
        </FormField>
        <FormField label={T.email} required errors={errors.email}>
          {(props) => <input {...props} {...field('email')} type="email" required autoComplete="email" />}
        </FormField>
      </div>
      <div className="inquiry-form__row">
        <FormField label={T.phone} errors={errors.phone}>
          {(props) => <input {...props} {...field('phone')} type="tel" autoComplete="tel" placeholder={T.phonePlaceholder} />}
        </FormField>
        <FormField label={T.consumption} errors={errors.consumption}>
          {(props) => <input {...props} {...field('consumption')} type="text" inputMode="numeric" placeholder={T.consumptionPlaceholder} />}
        </FormField>
      </div>
      <FormField label={T.address} required errors={errors.address}>
        {(props) => <input {...props} {...field('address')} type="text" required autoComplete="street-address" />}
      </FormField>
      <FormField label={T.message} errors={errors.message}>
        {(props) => <textarea {...props} {...field('message')} rows={4} placeholder={T.messagePlaceholder} />}
      </FormField>
      <div className="inquiry-form__footer">
        <span className="inquiry-form__note">{T.requiredNote}</span>
        <button type="submit" className="btn btn--primary btn--lg" aria-busy={busy} disabled={busy}>
          {T.submit}
        </button>
      </div>
    </form>
  )
}
