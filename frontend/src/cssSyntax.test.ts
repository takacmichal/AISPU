import { describe, expect, it } from 'vitest'

// An unclosed block (e.g. a missing "}" after @media) silently nests every later rule of the production
// bundle inside it, so whole screens lose their styles. Vite and the linter do not report it.
const sheets = import.meta.glob<string>('./**/*.css', { query: '?raw', import: 'default', eager: true })

function unbalancedLine(css: string): number | null {
  const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, (comment) => comment.replace(/[^\n]/g, ' '))
  let depth = 0
  const lines = withoutComments.split('\n')
  for (const [index, line] of lines.entries()) {
    for (const char of line) {
      if (char === '{') depth++
      if (char === '}') depth--
      if (depth < 0) return index + 1
    }
  }
  return depth === 0 ? null : lines.length
}

describe('CSS files', () => {
  it('finds the stylesheets', () => {
    expect(Object.keys(sheets).length).toBeGreaterThan(10)
  })

  it.each(Object.entries(sheets))('%s has balanced braces', (_path, css) => {
    expect(unbalancedLine(css)).toBeNull()
  })
})
