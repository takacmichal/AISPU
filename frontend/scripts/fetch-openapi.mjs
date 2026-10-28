// Downloads the OpenAPI document from a running API into openapi/openapi.json.
// Usage: pnpm api:fetch   (OPENAPI_URL overrides the default http://localhost:5080/openapi/v1.json)
import { mkdir, writeFile } from 'node:fs/promises'

const url = process.env.OPENAPI_URL ?? 'http://localhost:5080/openapi/v1.json'
const response = await fetch(url).catch((error) => {
  console.error(`Cannot reach ${url}: ${error.message}\nStart the API first (dotnet run --project src/SolarOps.Api).`)
  process.exit(1)
})
if (!response.ok) {
  console.error(`GET ${url} failed with HTTP ${response.status}`)
  process.exit(1)
}

const document = await response.json()
await mkdir('openapi', { recursive: true })
await writeFile('openapi/openapi.json', JSON.stringify(document, null, 2) + '\n')
console.log(`Saved openapi/openapi.json from ${url}. Run pnpm api:generate next.`)
