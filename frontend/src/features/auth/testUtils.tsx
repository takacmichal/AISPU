import { render } from '@testing-library/react'
import { RouterProvider, createMemoryRouter } from 'react-router-dom'
import App from '../../App'
import { AuthProvider } from '../../auth/AuthContext'

export const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/problem+json' } })

export const empty = (status: number) => new Response(null, { status })

/** Renders the whole app (anonymous unless a session was saved before) at the given URL. */
export function renderAt(path: string) {
  const router = createMemoryRouter([{ path: '*', element: <AuthProvider><App /></AuthProvider> }], {
    initialEntries: [path],
  })
  render(<RouterProvider router={router} />)
  return router
}

/** Parsed JSON body of the n-th fetch call. */
export function requestBody(fetchMock: { mock: { calls: unknown[][] } }, call = 0): unknown {
  const init = fetchMock.mock.calls[call]?.[1] as RequestInit | undefined
  return JSON.parse(String(init?.body))
}
