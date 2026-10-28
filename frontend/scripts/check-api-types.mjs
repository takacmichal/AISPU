// Fails when src/api/generated/schema.d.ts does not match openapi/openapi.json.
import { execFileSync } from 'node:child_process'
import { readFileSync } from 'node:fs'

const target = 'src/api/generated/schema.d.ts'
const fresh = execFileSync('pnpm', ['exec', 'openapi-typescript', 'openapi/openapi.json'], {
  encoding: 'utf8',
  maxBuffer: 64 * 1024 * 1024,
  shell: process.platform === 'win32',
})

let current = ''
try {
  current = readFileSync(target, 'utf8')
} catch {
  // missing file counts as a mismatch
}

const normalize = (text) => text.replace(/\r\n/g, '\n').trimEnd()
if (normalize(fresh) !== normalize(current)) {
  console.error(`${target} is out of date with openapi/openapi.json. Run: pnpm api:generate`)
  process.exit(1)
}
console.log('Generated API types are up to date.')
