/** Slovak strings of the app shell (sidebar, top bar, quick jump). */
export const LAYOUT_TEXTS = {
  mainNav: 'Hlavná navigácia',
  breadcrumbs: 'Breadcrumbs',
  brandSubtitle: (customerOnly: boolean) => (customerOnly ? 'Zákaznícky portál' : 'Riadiace centrum'),
  collapse: 'Zbaliť menu',
  expand: 'Rozbaliť menu',
  openMenu: 'Otvoriť menu',
  closeMenu: 'Zavrieť menu',
  quickJumpButton: 'Hľadať zákazníkov, ponuky, inštalácie, tikety…',
  topGroup: 'Prehľad',
  help: 'Pomocník',
  notifications: 'Notifikácie',
  userMenu: 'Používateľ',
  signOut: 'Odhlásiť',
  quickJump: {
    title: 'Prejsť na modul',
    inputLabel: 'Názov modulu',
    placeholder: 'Napíšte názov, napr. ponuky',
    listLabel: 'Moduly',
    empty: 'Žiadny modul s takým názvom.',
    hint: 'Šípkami vyberte, Enter otvorí, Esc zavrie.',
  },
} as const
