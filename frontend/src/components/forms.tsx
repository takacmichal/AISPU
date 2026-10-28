import { useId, type ReactNode } from 'react'

interface FormFieldProps {
  label: string
  /** Validation messages for this field (client or server side). */
  errors?: readonly string[]
  children: (props: { id: string; 'aria-invalid': boolean; 'aria-describedby'?: string }) => ReactNode
}

export function FormField({ label, errors = [], children }: FormFieldProps) {
  const id = useId()
  const errorId = `${id}-error`
  const invalid = errors.length > 0
  return (
    <div className="form-field">
      <label htmlFor={id}>{label}</label>
      {children({ id, 'aria-invalid': invalid, 'aria-describedby': invalid ? errorId : undefined })}
      {invalid && (
        <p id={errorId} role="alert" className="form-field__error">
          {errors.join(' ')}
        </p>
      )}
    </div>
  )
}
