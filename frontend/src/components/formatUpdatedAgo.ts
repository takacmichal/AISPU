export const UPDATED_TEXT = {
  justNow: 'Aktualizované práve teraz',
  minutes: (n: number) => `Aktualizované pred ${n} min`,
  hours: (n: number) => `Aktualizované pred ${n} h`,
  failed: 'Obnovenie zlyhalo, zobrazené sú posledné načítané dáta.',
  retry: 'Skúsiť znova',
}

/** Age of the last successful load as a Slovak sentence ("Aktualizované pred 5 min"). */
export function formatUpdatedAgo(updatedAt: Date, now: Date): string {
  const minutes = Math.floor((now.getTime() - updatedAt.getTime()) / 60_000)
  if (minutes < 1) return UPDATED_TEXT.justNow
  if (minutes < 60) return UPDATED_TEXT.minutes(minutes)
  return UPDATED_TEXT.hours(Math.floor(minutes / 60))
}