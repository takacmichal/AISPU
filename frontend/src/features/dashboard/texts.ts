/** Slovak strings of the dashboard cards (production chart, alarms). */
export const DASHBOARD_PARTS = {
  retry: 'Skúsiť znova',
  production: {
    title: 'Výroba v čase',
    subtitle: () => 'Všetky dostupné inštalácie · kWh/deň',
    range: 'Obdobie',
    ranges: { '14d': '14 dní', '30d': '30 dní', '12m': '12 mesiacov' },
    loading: 'Načítavam výrobu…',
    error: 'Výrobu sa nepodarilo načítať.',
    empty: 'Za vybrané obdobie nie sú žiadne údaje.',
    noInstallations: 'Zatiaľ nie sú dostupné žiadne inštalácie.',
    summary: (total: string) => `Súčet výroby za obdobie: ${total} kWh`,
  },
  alarms: {
    title: 'Posledné alarmy',
    subtitle: 'Za posledných 24 hodín',
    all: 'Všetky',
    loading: 'Načítavam hlásenia…',
    error: 'Hlásenia sa nepodarilo načítať.',
    empty: 'Žiadne otvorené hlásenia.',
    ticket: 'Servisný tiket',
    autoDetected: 'Automatická detekcia',
    underproduction: (installation: string) => `Podvýroba: ${installation}`,
    severity: { critical: 'Kritický', warning: 'Varovanie', info: 'Info' },
  },
} as const
