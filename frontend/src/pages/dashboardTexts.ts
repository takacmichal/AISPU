/** Slovak UI strings of the dashboard (Prehľad) kept in one place for review. */
export const DASHBOARD_TEXT = {
  title: 'Prehľad',
  subtitle: 'Správa fotovoltických elektrární',
  kpiRegion: 'Kľúčové ukazovatele',
  kpiLoading: 'Načítavam ukazovateľ…',
  kpiError: 'Údaje sa nepodarilo načítať.',
  customers: { label: 'Zákazníci', detail: 'Evidovaní zákazníci' },
  openQuotes: {
    label: 'Otvorené ponuky',
    detail: (sent: number, draft: number) => `Odoslané: ${sent} · Rozpracované: ${draft}`,
  },
  contracts: { label: 'Zmluvy na podpis', detail: 'Zmluvy v koncepte čakajú na podpis' },
  installations: {
    label: 'Inštalácie v realizácii',
    detail: (handedOver: number) => `Odovzdané zákazníkom: ${handedOver}`,
  },
  pulse: {
    title: 'Posledná aktivita',
    subtitle: 'Naposledy zmenené ponuky a inštalácie',
    loading: 'Načítavam poslednú aktivitu…',
    error: 'Poslednú aktivitu sa nepodarilo načítať.',
    empty: 'Zatiaľ tu nie je žiadna aktivita.',
    quote: 'Ponuka',
    installation: 'Inštalácia',
  },
  noKpi: {
    title: 'Vitajte v SolarOps',
    description: 'Pre vašu rolu tu zatiaľ nie sú žiadne ukazovatele.',
    myPlants: 'Moje elektrárne',
  },
  api: {
    loading: 'Overujem stav API…',
    error: 'API je nedostupné.',
    label: 'Stav API',
  },
} as const
