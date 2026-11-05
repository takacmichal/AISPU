/** Slovak copy of the public landing page (docs/artefact-page-html/Main.dc.html). */
export const LANDING_TEXTS = {
  skip: 'Preskočiť na obsah',
  homeLabel: 'SolarOps – domov',
  nav: {
    label: 'Hlavná navigácia',
    mobileLabel: 'Mobilná navigácia',
    open: 'Otvoriť menu',
    close: 'Zavrieť menu',
    links: [
      { href: '#sluzby', label: 'Služby' },
      { href: '#ako', label: 'Ako to funguje' },
      { href: '#monitoring', label: 'Monitoring' },
      { href: '#kontakt', label: 'Kontakt' },
    ],
  },
  signIn: 'Prihlásiť sa',
  inquiryCta: 'Nezáväzný dopyt',
  openApp: 'Otvoriť aplikáciu',

  hero: {
    eyebrow: 'Predaj · Návrh · Pripojenie · Montáž · Servis',
    titleStart: 'Fotovoltika',
    titleMiddle: 'od návrhu po servis. Všetko na jednom mieste',
    lead: 'Navrhneme elektráreň na mieru vašej streche, vybavíme pripojenie u distribučky, namontujeme ju s revíziou a potom jej výrobu sledujeme 24/7.',
    primary: 'Chcem cenovú ponuku',
    secondary: 'Mám už elektráreň – prihlásiť',
    previewLabel: 'Ukážka zákazníckeho portálu s ilustratívnymi údajmi',
    previewNav: ['Prehľad', 'Moje elektrárne', 'Monitoring výroby', 'Servisné tikety'],
    plantMeta: 'FVE 9,6 kWp · Prešov',
    plantTitle: 'Moja elektráreň',
    plantStatus: 'V prevádzke',
    kpis: [
      { label: 'Výroba dnes', value: '31,8', unit: 'kWh' },
      { label: 'Tento mesiac', value: '512', unit: 'kWh' },
      { label: 'Výkon teraz', value: '3,6', unit: 'kW' },
    ],
    chartTitle: 'Výroba počas dňa',
    chartSummary: 'Skutočná výroba tesne sleduje predikciu, spolu 31,8 kWh oproti predikcii 33,8 kWh.',
  },

  numbers: {
    title: 'SolarOps v číslach',
    items: [
      { value: '120+', label: 'inštalovaných elektrární' },
      { value: '1,4 MWp', label: 'celkového inštalovaného výkonu' },
      { value: '≤ 2 h', label: 'do zachytenia poruchy' },
      { value: '10 dní', label: 'pracovných na vybavenie pripojenia malého zdroja' },
    ],
    note: 'Ilustratívne údaje.',
  },

  services: {
    title: 'Jedna firma. Celá cesta vašej elektrárne.',
    lead: 'Od prvej obhliadky až po servis po rokoch prevádzky – bez prehadzovania medzi dodávateľmi.',
    items: [
      {
        icon: 'design',
        title: 'Predaj a návrh',
        points: ['Obhliadka strechy a odberného miesta', 'Návrh konfigurácie a odhad ročnej výroby', 'Cenová ponuka z overeného katalógu'],
      },
      {
        icon: 'install',
        title: 'Inštalácia',
        points: ['Komponenty evidované so sériovými číslami', 'Montáž s checklistom a revízia', 'Preberací protokol v PDF'],
      },
      {
        icon: 'service',
        title: 'Servis a monitoring',
        points: ['Výroba meraná každých 5 minút', 'Automatické upozornenie na podvýrobu', 'Výjazd technika podľa priority tiketu'],
      },
    ],
  },

  steps: {
    title: 'Ako to funguje',
    lead: 'Sedem krokov, pri každom viete, kto ho práve rieši. Rovnaký priebeh potom uvidíte aj vo svojom portáli.',
    items: [
      { title: 'Dopyt', text: 'Zákazník a odberné miesto s EIC kódom.', who: 'Obchod' },
      { title: 'Ponuka', text: 'Panely, striedače a práca z katalógu, s DPH.', who: 'Obchod' },
      { title: 'Návrh FVE', text: 'Obhliadka, parametre strechy, konfigurácia a odhad výroby.', who: 'Projektant' },
      { title: 'Zmluva', text: 'Zmluva v PDF, podpis a zálohová platba.', who: 'Obchod' },
      { title: 'Pripojenie', text: 'Žiadosť u distribučky – malý zdroj ≤ 10,8 kW alebo väčší, s lehotami.', who: 'Distribučka' },
      { title: 'Inštalácia', text: 'Komponenty, montáž, revízia, uvedenie do prevádzky, odovzdanie.', who: 'Technik' },
      { title: 'Prevádzka a servis', text: 'Monitoring výroby, detekcia podvýroby, tikety a výjazdy.', who: 'Technik · Operátor' },
    ],
  },

  monitoring: {
    eyebrow: 'Inteligentná detekcia',
    title: 'Poruchu nájdeme skôr, ako si ju všimnete.',
    lead: 'Predikciu výroby počítame z predpovede počasia. Keď výroba klesne pod 85 % očakávanej aspoň na 2 hodiny, systém sám založí servisný tiket.',
    chartTitle: 'Výroba dnes · RD Novák, Košice',
    chartSummary: 'Od 11:00 do 14:00 je skutočná výroba približne na 66 % predikcie, úsek je označený ako podvýroba.',
    ticketIntro: 'Takto vyzerá automaticky založený tiket',
    ticketMeta: 'Tiket #318 · pred 3 h',
    ticketStatus: 'Nový',
    ticketTitle: 'Pokles výkonu',
    ticketText: 'RD Novák, Košice · výroba 3 h pod 85 % predikcie (66 %).',
    ticketSource: 'Automatická detekcia',
    ticketPriority: 'Vysoká priorita',
    ticketNextLabel: 'Ďalší krok',
    ticketNext: 'Diagnostika na diaľku',
    note: 'Ilustratívne údaje.',
  },

  portal: {
    title: 'Vaša elektráreň vo vrecku.',
    lead: 'V zákazníckom portáli vidíte, v akom kroku je montáž, či ste pripojení k sieti, koľko ste dnes vyrobili – a poruchu nahlásite jedným klikom.',
    points: ['Stav montáže krok po kroku', 'Výroba dnes, za mesiac aj za rok', 'Zmluva a preberací protokol v PDF'],
    previewLabel: 'Ukážka detailu elektrárne v zákazníckom portáli',
    meta: 'Moje elektrárne',
    plant: 'FVE 9,6 kWp · Prešov',
    eic: 'EIC 24ZVS0000XXXXXXX',
    report: 'Nahlásiť poruchu',
    progressLabel: 'Stav montáže',
    progressValue: 'Krok 6 zo 7',
    progressStep: 'Inštalácia',
    progressNote: 'Revízia vyhovela',
    tiles: [
      { label: 'Pripojenie k sieti', value: 'Schválené distribučkou' },
      { label: 'Ďalší krok', value: 'Uvedenie do prevádzky' },
    ],
  },

  calculator: {
    title: 'Koľko vyrobí vaša strecha?',
    lead: 'Orientačný výpočet za pár sekúnd.',
    power: 'Výkon elektrárne',
    orientation: 'Orientácia strechy',
    south: 'Juh',
    eastWest: 'Východ – západ',
    region: 'Lokalita',
    yearly: 'Odhad ročnej výroby',
    saving: 'Orientačná úspora',
    perYear: '€ / rok',
    disclaimer:
      'Ide o odhad z priemerného ročného koeficientu pre kraj; počíta s 60 % vlastnou spotrebou a cenou 0,20 €/kWh. Presný odhad dostanete po obhliadke.',
    cta: 'Chcem presnú ponuku',
  },

  faq: {
    title: 'Časté otázky',
    items: [
      {
        q: 'Ako dlho trvá pripojenie?',
        a: 'Pri malom zdroji je postup zjednodušený a žiadosť vybavíme spravidla do niekoľkých pracovných dní. Pri väčšom zdroji určuje lehoty distribučná spoločnosť. Každý krok a jeho termín uvidíte v portáli.',
      },
      {
        q: 'Čo je malý zdroj?',
        a: 'Elektráreň s výkonom do 10,8 kW pripojená k existujúcemu odbernému miestu. Má jednoduchší proces pripojenia ako väčšie zdroje.',
      },
      {
        q: 'Čo ak elektráreň vyrába menej?',
        a: 'Výrobu porovnávame s predikciou z predpovede počasia. Ak klesne pod 85 % aspoň na 2 hodiny, systém sám založí servisný tiket a technik začne diagnostikou na diaľku.',
      },
      {
        q: 'Ako vidím svoju výrobu?',
        a: 'Po prihlásení do zákazníckeho portálu. Údaje sa obnovujú každých 5 minút a vidíte skutočnú aj predikovanú výrobu.',
      },
      {
        q: 'Kto robí revíziu?',
        a: 'Revízny technik pred uvedením do prevádzky. Revíznu správu aj preberací protokol nájdete v portáli v PDF.',
      },
    ],
  },

  contact: {
    title: 'Nezáväzný dopyt',
    lead: 'Ozveme sa do jedného pracovného dňa a dohodneme obhliadku.',
    leadUnavailable: 'Prvým krokom k vlastnej elektrárni je zákaznícky účet.',
    accountQuestion: 'Radšej hneď vlastný účet?',
    createAccount: 'Vytvoriť účet',
    // Shown until the API has an anonymous inquiry endpoint.
    unavailableTitle: 'Online dopyt pripravujeme',
    unavailableText:
      'Formulár dopytu zatiaľ nie je spustený. Vytvorte si zákaznícky účet a v portáli uvidíte svoju elektráreň od ponuky až po servis.',
  },

  inquiry: {
    name: 'Meno a priezvisko',
    email: 'E-mail',
    phone: 'Telefón',
    phonePlaceholder: '+421',
    consumption: 'Približná ročná spotreba (kWh)',
    consumptionPlaceholder: 'napr. 4 500',
    address: 'Adresa odberného miesta',
    message: 'Správa',
    messagePlaceholder: 'Typ strechy, termín, otázky…',
    requiredNote: '* povinné polia',
    submit: 'Odoslať dopyt',
    failed: 'Dopyt sa nepodarilo odoslať. Skúste to znova o chvíľu.',
    sentTitle: 'Ďakujeme, dopyt sme prijali.',
    sentText: 'Potvrdenie sme poslali na váš e-mail. Obchodník vás bude kontaktovať do jedného pracovného dňa.',
    another: 'Poslať ďalší dopyt',
    errors: {
      nameRequired: 'Vyplňte meno a priezvisko.',
      emailExample: 'Zadajte platný e-mail, napr. jana@domena.sk.',
      phoneInvalid: 'Telefón môže obsahovať len číslice, medzery a znak +.',
      consumptionInvalid: 'Spotrebu zadajte ako celé číslo v kWh, napr. 4500.',
      addressRequired: 'Vyplňte adresu odberného miesta.',
      messageTooLong: 'Správa môže mať najviac 2 000 znakov.',
    },
  },

  footer: {
    tagline: 'Fotovoltika od návrhu po servis. Všetko na jednom mieste.',
    pageLinks: 'Na stránke',
    account: 'Účet',
    accountLinksLabel: 'Odkazy v pätičke',
    login: 'Prihlásenie',
    register: 'Registrácia',
    copyright: '© 2026 SolarOps',
    school: 'Školský projekt AISPU',
  },
} as const
