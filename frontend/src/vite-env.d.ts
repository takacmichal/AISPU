/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Base URL of the SolarOps API, e.g. http://localhost:5080 */
  readonly VITE_API_URL?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
