/** "1 234,50" or "1234.5" to a number; NaN when it is not a number. */
export function parseAmount(text: string): number {
  const normalized = text.replace(/\s/g, '').replace(',', '.')
  return normalized === '' ? NaN : Number(normalized)
}
