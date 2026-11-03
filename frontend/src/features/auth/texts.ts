/** Slovak texts of the public account screens (registration, password reset) kept in one place for review. */
export const AUTH_TEXTS = {
  email: 'E-mail',
  password: 'Heslo',
  newPassword: 'Nové heslo',
  confirmPassword: 'Potvrdenie hesla',
  backToLogin: 'Späť na prihlásenie',
  passwordRules: 'Heslo musí mať aspoň 10 znakov, veľké aj malé písmeno, číslicu a aspoň 4 rôzne znaky.',

  login: {
    registerPrompt: 'Nemáte účet?',
    registerLink: 'Zaregistrujte sa',
    forgotLink: 'Zabudnuté heslo?',
  },

  register: {
    title: 'Registrácia',
    intro: 'Vytvorte si zákaznícky účet a sledujte svoje elektrárne.',
    submit: 'Zaregistrovať sa',
    submitting: 'Registrujem…',
    success: 'Účet bol vytvorený. Teraz sa môžete prihlásiť.',
    emailTaken: 'Účet s týmto e-mailom nie je možné vytvoriť.',
    haveAccount: 'Už máte účet?',
    loginLink: 'Prihláste sa',
  },

  forgot: {
    title: 'Zabudnuté heslo',
    intro: 'Zadajte e-mail, ktorým sa prihlasujete. Pošleme vám odkaz na nastavenie nového hesla.',
    submit: 'Poslať odkaz',
    submitting: 'Odosielam…',
    sent: 'Ak k tomuto e-mailu existuje účet, poslali sme naň odkaz na obnovu hesla. Odkaz platí 1 hodinu.',
  },

  reset: {
    title: 'Nastavenie nového hesla',
    intro: 'Zvoľte si nové heslo. Po zmene sa odhlásite zo všetkých zariadení.',
    submit: 'Nastaviť heslo',
    submitting: 'Ukladám…',
    success: 'Heslo bolo zmenené. Prihláste sa novým heslom.',
    invalidLink: 'Odkaz na obnovu hesla je neplatný alebo mu vypršala platnosť.',
    requestNew: 'Požiadať o nový odkaz',
  },

  validation: {
    emailRequired: 'Vyplňte e-mail.',
    emailInvalid: 'E-mail nemá platný tvar.',
    emailTooLong: 'E-mail môže mať najviac 256 znakov.',
    passwordRequired: 'Vyplňte heslo.',
    passwordTooLong: 'Heslo môže mať najviac 128 znakov.',
    passwordWeak: 'Heslo nespĺňa pravidlá.',
    confirmRequired: 'Zopakujte heslo.',
    confirmMismatch: 'Heslá sa nezhodujú.',
  },
} as const
