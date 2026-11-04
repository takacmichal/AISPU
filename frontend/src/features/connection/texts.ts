/** Slovak UI strings of the connection feature, kept in one place for review. */
export const STATE_LABELS: Record<string, string> = {
  Draft: 'Koncept',
  Submitted: 'Podaná distribučke',
  InfoRequested: 'Distribučka žiada doplnenie',
  Rejected: 'Zamietnutá',
  ContractOffered: 'Zmluva ponúknutá',
  ContractSigned: 'Zmluva podpísaná zákazníkom',
  ContractSubmitted: 'Podpísaná zmluva odoslaná',
  Concluded: 'Uzavretá',
}

/** Steps of the happy path, in order (see STEP_STATES in model.ts). */
export const STEP_LABELS = ['Príprava žiadosti', 'Posúdenie distribučkou', 'Zmluva o pripojení', 'Podpis zákazníka', 'Odoslanie zmluvy', 'Uzavreté'] as const

export const TYPE_LABELS: Record<string, string> = {
  SmallSource: 'Malý zdroj (do 10,8 kW)',
  LargeSource: 'Väčší zdroj (nad 10,8 kW)',
}

export const ATTACHMENT_KIND_LABELS: Record<string, string> = {
  SingleLineDiagram: 'Jednopólová schéma',
  OwnerConsent: 'Súhlas vlastníka',
  Datasheet: 'Technický list',
  SwitchboardPhoto: 'Fotka rozvádzača',
  PowerOfAttorney: 'Plnomocenstvo',
  CadastreExtract: 'Výpis z katastra',
}

/** Slovak plural of "deň": 1 deň, 2-4 dni, otherwise dní. */
export function daysText(days: number): string {
  return `${days} ${days === 1 ? 'deň' : days >= 2 && days <= 4 ? 'dni' : 'dní'}`
}

export const CONNECTION_TEXTS = {
  stepperLabel: 'Priebeh žiadosti o pripojenie',
  loading: 'Načítavam žiadosť o pripojenie…',
  loadError: 'Žiadosť o pripojenie sa nepodarilo načítať.',
  stepperLoading: 'Načítavam priebeh pripojenia…',
  stepperError: 'Priebeh pripojenia sa nepodarilo načítať.',
  stepperEmpty: 'Žiadosť o pripojenie zatiaľ nebola vytvorená.',
  stepperNoInstallation: 'Zmluva zatiaľ nemá inštaláciu, žiadosť o pripojenie nie je možné vytvoriť.',
  openRequest: 'Otvoriť žiadosť o pripojenie',
  infoRequestedNote: 'Distribučka žiada doplnenie údajov.',
  rejectedNote: 'Distribučka žiadosť zamietla.',
  dueDate: (date: string) => `Lehota na návrh zmluvy: ${date}`,
  deadlineOk: (days: number) => `Do konca lehoty zostáva ${daysText(days)}.`,
  deadlineWarning: (days: number) =>
    days === 0
      ? 'Lehota distribučky na návrh zmluvy uplynie dnes.'
      : `Blíži sa lehota distribučky na návrh zmluvy, zostáva ${daysText(days)}.`,
  deadlineOverdue: (days: number) => `Distribučka mešká s návrhom zmluvy o ${daysText(days)}.`,
  back: 'Späť',
  title: 'Žiadosť o pripojenie',
  data: 'Údaje žiadosti',
  eic: 'EIC odberného miesta',
  power: 'Menovitý výkon (AC)',
  breaker: 'Hlavný istič',
  type: 'Typ zdroja',
  state: 'Stav',
  attachments: 'Prílohy',
  attachmentsEmpty: 'Žiadosť nemá žiadne prílohy.',
  attachmentMetadataOnly: 'bez obsahu',
  download: 'Stiahnuť',
  history: 'História stavu',
  historyEmpty: 'Zatiaľ nie sú žiadne záznamy.',
  actions: 'Ďalší krok',
  installation: 'Inštalácia',
} as const

export const TIMELINE_LABELS = {
  created: 'Žiadosť vytvorená',
  submitted: 'Žiadosť podaná distribučke',
  decided: 'Rozhodnutie distribučky',
  conditionsReviewed: 'Technické podmienky skontrolované',
  contractSent: 'Zmluva odoslaná zákazníkovi',
  updated: 'Posledná zmena',
} as const

export interface TransitionDef {
  action: string
  label: string
  confirm: string
  success: string
}

export const TRANSITIONS: Record<string, TransitionDef> = {
  submit: {
    action: 'submit',
    label: 'Podať distribučke',
    confirm: 'Žiadosť sa odošle distribučke a začne plynúť lehota na návrh zmluvy. Pokračovať?',
    success: 'Žiadosť bola podaná distribučke.',
  },
  resubmit: {
    action: 'resubmit',
    label: 'Odoslať znova',
    confirm: 'Doplnená žiadosť sa odošle distribučke a lehota začne plynúť odznova. Pokračovať?',
    success: 'Žiadosť bola odoslaná znova.',
  },
  'review-conditions': {
    action: 'review-conditions',
    label: 'Potvrdiť kontrolu podmienok',
    confirm: 'Potvrdzujete, že technické podmienky ponúknutej zmluvy sú skontrolované?',
    success: 'Kontrola podmienok bola zaznamenaná.',
  },
  'send-contract': {
    action: 'send-contract',
    label: 'Zmluva odoslaná zákazníkovi',
    confirm: 'Zaznamenať, že zmluva bola odoslaná zákazníkovi?',
    success: 'Odoslanie zmluvy bolo zaznamenané.',
  },
  'customer-signed': {
    action: 'customer-signed',
    label: 'Zákazník podpísal zmluvu',
    confirm: 'Zaznamenať podpis zmluvy zákazníkom?',
    success: 'Podpis zákazníka bol zaznamenaný.',
  },
  'submit-signed-contract': {
    action: 'submit-signed-contract',
    label: 'Odoslať podpísanú zmluvu distribučke',
    confirm: 'Podpísaná zmluva sa odošle distribučke. Pokračovať?',
    success: 'Podpísaná zmluva bola odoslaná distribučke.',
  },
}
