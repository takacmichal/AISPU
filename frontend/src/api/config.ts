/** Base URL of the API without trailing slash (empty = same origin). */
export const apiUrl = (import.meta.env.VITE_API_URL ?? '').replace(/\/+$/, '')
