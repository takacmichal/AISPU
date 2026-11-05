# Dizajn systém SolarOps (frontend)

Vzhľad je prevzatý z `docs/artefact-page-html/Main.dc.html` (landing page), rozloženie obrazoviek z `SolarOps-Wireframe.dc.html`. Plán prechodu je v `docs/frontend-redesign-plan.md`. Všetky hodnoty sú tokeny v `src/index.css` (`:root`); v CSS modulov sa farby nezadávajú natvrdo.

## Pravidlá

- **Jedna hlavná akcia na obrazovku:** `.btn--primary` (modrá `--color-primary`). Ostatné tlačidlá sú `.btn` (sekundárne, teplá sivá), `.btn--ghost` (obrys) alebo `.btn--danger`.
- **Akcent `--color-accent` (#0096f7) nie je na text**, má kontrast len ~3 : 1. Používa sa na ikony, bodky, čiary grafov a focus ring. Odkazy a text v modrej majú `--color-primary`.
- **Stav sa neoznačuje len farbou:** badge s tónom má bodku a text, kroky steppera majú ikonu (✓, !).
- **Biele plochy sú karty a tabuľky** na krémovom pozadí `--color-bg`. Piesková `--color-sand` je na zvýraznené bloky (KPI v portáli, kalkulačka, služby na landing page).
- **Ovládacie prvky majú aspoň 44 px** (`--control-height`); malé (`.btn--sm`) 32 px len v tabuľkách a pomocných akciách.

## Tokeny

| Skupina | Tokeny |
|---|---|
| Plochy | `--color-bg` #fdfdf7, `--color-surface` #fff, `--color-sand` #f5f2de, `--color-secondary` #e7e3e1, `--color-inverse` #141414 (toast, avatar) |
| Text | `--color-text` #000, `--color-text-body` #1f1f1f, `--color-text-muted` #666, `--color-text-disabled` |
| Čiary | `--color-border` (8 % čiernej), `--color-border-strong` (15 %), `--color-input-border` (45 %) |
| Značka | `--color-primary` #0072c0 (hover #005a99, active #00548c), `--color-accent` #0096f7 |
| Tóny | `success` #2c4229 na #e4ecdf, `info` #00548c na #e1f0fc, `warning` #6b4e00 na #f7ecc9, `danger` #a32900 na #ffe4d9 (chyba poľa: okraj #b83200, pozadie #fff6f2) |
| Grafy | `--color-chart-actual` (akcent), `--color-chart-predicted` (čierna, prerušovaná), `--color-chart-under`, `--color-chart-band`, `--color-chart-grid` |
| Zaoblenie | tlačidlá a inputy 12 px, karty 18 px, sekcie 24 px, badge a pilulky úplne oblé |
| Tiene | `--shadow-sm` (karty), `--shadow-md` (hover, rozbaľovacie menu), `--shadow-xl` (dialóg, toast) |

## Typografia

- Písmo **Plus Jakarta Sans** (variabilné, licencia OFL, `src/assets/fonts/`, podmnožiny latin a latin-ext kvôli diakritike). Načítava sa lokálne, bez Google Fonts.
- Číslice sú tabuľkové (`font-feature-settings: 'lnum', 'tnum'`), aby sa v tabuľkách a KPI nehýbali.
- Nadpisy stránok (`h2`) 30 px, hrúbka 300, letter-spacing −0,03 em. `h1` 36 px (landing page bude mať väčšie). `h3` 18 px/500.
- Telo 15 px, popisky 12–13 px, hlavičky tabuliek 11 px veľkými písmenami.

## Prístupnosť (WCAG 2.1 AA)

- Text: `--color-text-muted` má 5,6 : 1 na krémovej a 5,1 : 1 na pieskovej, `--color-primary` 5,0 : 1 na bielej.
- **Odchýlka od návrhu:** okraj inputov je tmavší (`--color-input-border`, 45 % čiernej, ~3,3 : 1) ako v mockupe (10 %), aby hranica poľa spĺňala 3 : 1 pre ovládacie prvky.
- Focus: dvojitý prstenec (biela + akcent) na všetkom interaktívnom.
- **Kontrast stráži test** `src/a11y/contrast.test.ts`: číta tokeny priamo z `index.css` a overí 4,5 : 1 pre text a tóny badge a 3 : 1 pre focus ring a okraje polí. Pri zmene farby ho spusti; ak padne, uprav token, nie test.
- **Klávesnica:** riadky `DataTable` s `onRowClick` sú v poradí Tab a otvárajú sa Enterom. Kanban má pre každý presun samostatné tlačidlo („→ Zatriedený“), `SegmentedControl` sa ovláda šípkami, `Dialog` drží focus vo vnútri a Escape ho zavrie.
- **Mobil:** tabuľky sú v `data-table__scroll` a posúvajú sa vodorovne. Na stránkach pre prácu v teréne (Moje výjazdy, detail tiketu) je hlavná akcia dole na dosah palca a ovládacie prvky majú aspoň 44 px (48 px v spodnej lište).
- Animácie sa pri `prefers-reduced-motion: reduce` vypínajú.

## Komponenty

Spoločné komponenty sú v `src/components/`, ukážka všetkých na `/dev/ui` (len `pnpm dev`, v produkčnom builde nie je). Vychádzajú z knižnice K-01 až K-10 vo wireframe.

| Komponent | Na čo | Poznámka |
|---|---|---|
| `PageHeader` | Nadpis stránky so stavom, podnadpisom a akciami vpravo (najviac jedna `btn--primary`) | Breadcrumbs kreslí shell nad ním |
| `Toolbar`, `SearchField`, `FilterSelect` | Riadok nad tabuľkou: vyhľadávanie, filtre ako „chip“ (Stav: Všetky), vpravo doplnkové info | Filter je natívny `select`, takže funguje klávesnica aj čítačka |
| `Pager` | Stránkovanie „Zobrazené 1–10 z 248“ s číslami strán | Používa ho `DataTable` |
| `KpiCard` | Ukazovateľ: ikona, hodnota s jednotkou, popis, trend alebo cieľ | Hodnotu formátuj vopred (`formatNumber`) |
| `Stepper` | Priebeh procesu (ponuka, inštalácia, pripojenie) | Stavy `done`, `current`, `blocked`, `upcoming`; aktuálny krok má `aria-current="step"` |
| `ProgressBar` | Lehota, SLA, plnenie plánu | Hodnota nad maximom (po lehote) sa kreslí plná a tmavá |
| `SegmentedControl` | Prepínač zobrazenia alebo obdobia (Kanban/Zoznam, Deň/Týždeň) | Radio group: jeden tab stop, šípky, Home/End |
| `Avatar` | Iniciály pri mene | Dekoratívny, meno musí byť vedľa ako text |
| `KeyValueList` | Údaje „Kontaktné údaje“, „Realizácia“, „Detaily“ | `<dl>` |
| `ActivityTimeline` | Aktivita zákazníka, história tiketu | Poradie určuje volajúci |
| `FormErrorSummary` | Súhrn chýb nad formulárom | Chyby pri poliach ostávajú (`FormField`) |
| `ConfirmDialog` | Potvrdenie; s `danger` má ikonu „!“ a červené tlačidlo | Zrušiť je prvé, dostane focus |
| `charts/ProductionAreaChart` | Skutočná vs. predikovaná výroba s pásmom podvýroby, `variant="dark"` pre landing page | Vlastné SVG za stabilným API (ADR #224, bod 5); `summary` a skrytá tabuľka ako textová alternatíva. `interactive` pridá kurzor ovládaný šípkami a myšou s tooltipom (monitoring), `showUnder` vypína zvýraznenie podvýroby |

Tóny badge: `badge--success`, `--info`, `--warning`, `--error`, `--attention` (vyžaduje akciu, obrys), `--muted` (neaktívne, prerušovaný obrys); bez tónu = neutrálny. Tlačidlo počas ukladania dostane `aria-busy="true"` (zobrazí sa spinner).

## Obal aplikácie

- Prehľad prihláseného používateľa je na `/app` (`HOME_PATH`), adresa `/` patrí verejnej landing page (#390; dovtedy presmeruje na `/app`).
- Menu, oprávnenia ciest a breadcrumbs sa skladajú z jedného zoznamu `NAVIGATION` v `src/auth/navigation.ts`. Skupiny: Obchod, Realizácia, Servis, Systém. Novú stránku pridaj tam, nie priamo do sidebaru.
- Sidebar sa dá zbaliť na ikony (pamätá sa v `localStorage`), texty ostávajú pre čítačky. Pod 760 px je z neho vysúvacie menu.
- Horná lišta: rýchly skok na moduly (`Ctrl/⌘+K`, ponúka len moduly povolené pre rolu) a menu používateľa s odhlásením.
- **403 a 404** sú karta s veľkým kódom, vysvetlením a cestou späť (`Späť`, `Prejsť na prehľad`). Stránka 403 vie, z ktorej sekcie používateľ prišiel (`RequireAuth` posiela `state.from`), a vypíše jeho rolu aj roly, ktoré sekciu smú otvoriť.

## Snímky

Aktuálne snímky obrazoviek sú v `docs/screenshots/` a v hlavnom README (ukážkové dáta).
