import { useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../../api/client'
import { problemToApiError } from '../../api/problem'
import { useAuth } from '../../auth/AuthContext'
import { useToast } from '../../components/toast'
import { useFormSubmit } from '../../components/useFormSubmit'
import { CONTRACT_TEXTS as T } from './texts'

const CREATE_ROLES = ['Admin', 'Sales']

/**
 * "Vytvoriť zmluvu" for the quote detail: shown only for an accepted quote and a role that may create contracts.
 * Navigates to the new contract (route /contracts/:id).
 */
export function CreateContractButton({ quoteId, quoteStatus }: { quoteId: string; quoteStatus: string }) {
  const { roles } = useAuth()
  const navigate = useNavigate()
  const toast = useToast()

  const create = useCallback(async () => {
    const { data, response, error } = await api.POST('/api/contracts', { body: { quoteId } })
    if (!data) throw problemToApiError(response.status, error)
    toast.success(T.create.success)
    navigate(`/contracts/${data.id}`)
  }, [quoteId, navigate, toast])
  const { busy, formError, submit } = useFormSubmit(create)

  if (quoteStatus !== 'Accepted' || !roles.some((role) => CREATE_ROLES.includes(role))) return null
  return (
    <>
      <button type="button" className="btn btn--primary" disabled={busy} onClick={() => void submit()}>
        {T.create.button}
      </button>
      {formError && (
        <p role="alert" className="form-error">
          {formError}
        </p>
      )}
    </>
  )
}
