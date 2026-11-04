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
