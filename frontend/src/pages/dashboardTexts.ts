/** Slovak UI strings of the dashboard (Prehľad) kept in one place for review. */
export const DASHBOARD_TEXT = {
  title: 'Prehľad',
  subtitle: 'Správa fotovoltických elektrární',
  kpiRegion: 'Kľúčové ukazovatele',
  kpiLoading: 'Načítavam ukazovateľ…',
  kpiError: 'Údaje sa nepodarilo načítať.',
  customers: { label: 'Zákazníci', detail: 'Evidovaní zákazníci' },
  alarms: {
    label: 'Aktívne alarmy',
    detail: (high: number) => `Vysoká priorita: ${high}`,
  },
  tickets: {
    label: 'Otvorené tikety',
    detail: (fresh: number) => `Nové, čakajú na zatriedenie: ${fresh}`,
    /** The list endpoint returns at most 100 tickets per page. */
    partial: (count: number) => `Počítané z posledných ${count} tiketov.`,
  },
  alarmList: {
    title: 'Posledné alarmy',
    subtitle: 'Tikety z automatickej detekcie podvýroby',
    all: 'Všetky alarmy',
    loading: 'Načítavam alarmy…',
    error: 'Alarmy sa nepodarilo načítať.',
    emptyTitle: 'Žiadne aktívne alarmy',
    emptyText: 'Keď monitoring zistí podvýrobu, založí tiket a zobrazí sa tu.',
  },
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
