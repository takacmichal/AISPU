import { apiUrl } from '../../api/config'
import type { components } from '../../api/generated/schema'
import { toApiError } from '../../api/problem'

type Schemas = components['schemas']

/**
 * Anonymous account endpoints. They use plain fetch (like login in auth/session.ts) instead of the typed client,
 * because authFetch would attach a possibly stale Bearer token and try to refresh it on 401.
 * Non-2xx responses throw ApiError with ProblemDetails field errors.
 */
async function post(path: string, body: unknown): Promise<Response> {
  const response = await fetch(`${apiUrl}${path}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw await toApiError(response)
  return response
}

/** 201 with the created account (role Customer). No tokens: the user signs in afterwards. */
export async function register(body: Schemas['RegisterRequest']): Promise<void> {
  await post('/api/auth/register', body)
}

/** Always 202, whether or not the account exists. */
export async function forgotPassword(body: Schemas['ForgotPasswordRequest']): Promise<void> {
  await post('/api/auth/forgot-password', body)
}

/** 204; 400 on `token` for an unknown e-mail or a bad/expired token, 400 on `newPassword` for a weak password. */
export async function resetPassword(body: Schemas['ResetPasswordRequest']): Promise<void> {
  await post('/api/auth/reset-password', body)
}
