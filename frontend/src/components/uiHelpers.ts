/** Pure helpers of the shared components (kept apart so component files export only components). */

const LEGAL_FORMS = /\b(s\.\s?r\.\s?o\.|a\.\s?s\.|spol\.|k\.\s?s\.|v\.\s?o\.\s?s\.)/gi

/** Up to two initials from a person, company or e-mail ("Ján Novák" → "JN", "demo@solarops.local" → "D"). */
export function initials(name: string): string {
  const base = name.includes('@') ? name.split('@')[0] : name.replace(LEGAL_FORMS, '')
  const words = base.split(/[\s._-]+/).filter(Boolean)
  const letters = words.slice(0, 2).map((w) => w[0].toUpperCase())
  return letters.join('') || '?'
}

/** "Formulár obsahuje 2 chyby. …" with the Slovak plural (1 chybu, 2–4 chyby, 5+ chýb). */
export function formErrorsTitle(count: number): string {
  if (count === 1) return 'Formulár obsahuje 1 chybu. Opravte ju a odošlite znova.'
  const noun = count >= 2 && count <= 4 ? 'chyby' : 'chýb'
  return `Formulár obsahuje ${count} ${noun}. Opravte ich a odošlite znova.`
}

export type PageItem = number | 'gap'

/** First, last and the pages around the current one; a single skipped page is shown instead of a gap. */
export function pageItems(page: number, pageCount: number): PageItem[] {
  const wanted = new Set([1, pageCount, page - 1, page, page + 1].filter((p) => p >= 1 && p <= pageCount))
  if (page === 1) wanted.add(2)
  if (page === pageCount) wanted.add(pageCount - 1)
  const pages = [...wanted].filter((p) => p >= 1 && p <= pageCount).sort((a, b) => a - b)
  const items: PageItem[] = []
  pages.forEach((p, i) => {
    const prev = pages[i - 1]
    if (prev !== undefined && p - prev === 2) items.push(p - 1)
    else if (prev !== undefined && p - prev > 2) items.push('gap')
    items.push(p)
  })
  return items
}
