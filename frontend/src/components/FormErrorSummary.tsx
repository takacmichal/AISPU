import './ui.css'
import { formErrorsTitle } from './uiHelpers'

/** Summary above a form (K-04). The field messages stay next to the fields; this only lists them once more. */
export function FormErrorSummary({ errors }: { errors: readonly string[] }) {
  if (errors.length === 0) return null
  return (
    <div role="alert" className="form-summary">
      <p className="form-summary__title">{formErrorsTitle(errors.length)}</p>
      <ul>
        {errors.map((error) => (
          <li key={error}>{error}</li>
        ))}
      </ul>
    </div>
  )
}
