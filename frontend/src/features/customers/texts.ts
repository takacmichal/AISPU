/** Slovak UI strings of the customers feature kept in one place for review. */
export const TYPE_LABELS: Record<string, string> = { Person: 'Fyzická osoba', Company: 'Právnická osoba' }
export const STATUS_LABELS: Record<string, string> = { Active: 'Aktívny', Archived: 'Archivovaný' }
export const STATUS_BADGE: Record<string, string> = { Active: 'badge badge--success', Archived: 'badge badge--muted' }

export const CUSTOMER_TEXTS = {
  list: {
    title: 'Zákazníci',
    subtitle: (count: number) => `${count} ${count === 1 ? 'zákazník' : count >= 2 && count <= 4 ? 'zákazníci' : 'zákazníkov'}`,
    create: 'Nový zákazník',
    search: 'Hľadať podľa mena, IČO alebo e-mailu',
    error: 'Zoznam zákazníkov sa nepodarilo načítať.',
    emptySearch: 'Nenašli sme žiadneho zákazníka.',
    empty: 'Zatiaľ nie sú evidovaní žiadni zákazníci.',
  },
  detail: {
    loading: 'Načítavam zákazníka…',
    error: 'Zákazníka sa nepodarilo načítať.',
    tabLoading: 'Načítavam záložku…',
    edit: 'Upraviť',
    newQuote: 'Nová ponuka',
  },
  tabs: {
    label: 'Sekcie zákazníka',
    overview: 'Prehľad',
    supplyPoints: 'Odberné miesta',
    quotes: 'Ponuky',
    contracts: 'Zmluvy',
    installations: 'Inštalácie',
  },
  overview: {
    contact: 'Kontaktné údaje',
    email: 'E-mail',
    phone: 'Telefón',
    address: 'Adresa',
    ico: 'IČO',
    dic: 'DIČ',
    icDph: 'IČ DPH',
    since: 'Zákazníkom od',
    note: 'Poznámka',
    nextStep: 'Nasledujúci krok',
    nextLoading: 'Zisťujem nasledujúci krok…',
    nextNone: 'Momentálne nič nečaká. Nová ponuka sa dá vytvoriť tlačidlom hore.',
    activity: 'Aktivita',
    activityLoading: 'Načítavam aktivitu…',
    activityEmpty: 'Zatiaľ žiadna aktivita.',
  },
} as const
