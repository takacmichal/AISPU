# ADR spike #224: Knižnica na grafy časových radov (React: Recharts, záloha ECharts)

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #224 (nadradené #10)
- **Súvisí:** #225 (TS klient), #209 (pôvodné ADR o frontende)

## Zmena rozsahu (dôležité)

Issue je formulované pre **Angular** ("PrimeNG Chart (Chart.js) vs. ngx-echarts"). Frontend sa zmenil na **React + Vite + TypeScript** (Angular sa nepoužije). Preto sú **PrimeNG Chart a ngx-echarts mimo hry** (sú to Angular komponenty) a ADR porovnáva knižnice použiteľné v Reacte. Zadanie "prototyp na 10 000 bodoch" sa presúva do nadväzujúceho issue.

## Kontext

Dashboard výroby potrebuje čiarové grafy s stovkami až tisíckami bodov (hodinové a 5-min rady), zoom/pan, porovnanie skutočnej výroby s predikciou (`P_expected`) a zvýraznenie intervalov anomálií (ADR #194). V Sprinte 1 je frontend len scaffold, grafy prídu neskôr.

## Zvažované možnosti

| Knižnica (verzia npm k 2026-10-15) | Render | Zoom/pan | Anotácie intervalov | Veľké rady | Posúdenie |
|---|---|---|---|---|---|
| **Recharts 3.10.1** (MIT) | SVG, deklaratívne React komponenty | `Brush` (výber rozsahu), zoom vlastný | `ReferenceArea` / `ReferenceLine` (anomálie, prahy) | SVG sa spomaľuje pri tisíckach bodov (neoverené meraním) | **vybrané**: najjednoduchší React model, malé nároky, téma cez CSS/props |
| ECharts 6.1.0 (Apache-2.0) + `echarts-for-react` 3.0.6 (MIT) | Canvas (aj SVG) | `dataZoom` (slider + koliesko) vstavané | `markArea`, `markLine` | `sampling: "lttb"`, veľké rady; výrazne lepšie na desaťtisíce bodov (podľa dokumentácie projektu, neoverené lokálne) | **záloha** pre veľké rady |
| Chart.js 4.5.1 (MIT) cez wrapper | Canvas | pluginy (zoom plugin) | pluginy | dobré | zvažované, ale vyžaduje ďalšie pluginy, nič nepridáva oproti dvojici vyššie |
| uPlot 1.6.32 (MIT) | Canvas, minimalistický | vlastný | vlastné | najlepší výkon | nízkoúrovňové API, zbytočne pre MVP |
| PrimeNG Chart / ngx-echarts | n/a | n/a | n/a | n/a | **Angular, nepoužiteľné** po zmene na React |

## Rozhodnutie

**Recharts** je štandardná knižnica grafov pre React frontend (Sprint 1+). **ECharts** je určená záloha, ak sa splní niektorá podmienka:

- graf potrebuje > 5 000 bodov na sériu bez serverového zníženia rozlíšenia, alebo
- zmeraný render/interakcia (zoom) presiahne ~100 ms na bežnom notebooku (návrh prahu), alebo
- treba plynulý zoom/pan cez dataZoom na surových 5-min dátach.

Pravidlá používania Recharts (aby sa záloha nikdy nemusela používať zbytočne):

1. **Server alebo klient pred vykreslením zníži počet bodov** na <= 1 000 až 2 000 na sériu (hodinové/denné agregáty, pri zoome načítať jemnejší rozsah z API; LTTB alebo priemer po koši).
2. `dot={false}`, `isAnimationActive={false}` pre veľké rady, `ResponsiveContainer`.
3. Skutočná vs. predikovaná výroba = dve `Line` série; anomálie = `ReferenceArea` s farbou závažnosti (ADR #194); prahy = `ReferenceLine`.
4. Téma (svetlá/tmavá) cez CSS premenné, nie hardcodované farby; farby dostupné aj pre farboslepých.
5. Komponent grafu v `frontend/src/components/charts/` s vlastnými props (`series`, `anomalies`), tak aby sa implementácia (Recharts/ECharts) dala vymeniť bez zmeny stránok.

## Dôsledky

- Jednoduchá integrácia a údržba v Reacte, malá krivka učenia pre tím.
- Výkon pre veľké rady je závislý od redukcie bodov; riziko je ošetrené prahom pre ECharts a abstrakciou komponentu.
- Zmena rozsahu proti issue (Angular) je vedomá; PrimeNG nie je už relevantný pre grafy.

## Riziká / neoverené

- **Neoverené:** výkon Recharts a ECharts na 10 000 bodoch (prototyp nebol vytvorený), veľkosť bundle a presné časy renderu. Čísla v tabuľke sú kvalitatívne.
- Neoverené: že Recharts 3 `Brush` stačí na UX zoomu (alternatíva: vlastný výber rozsahu + refetch).
- Overené: verzie a licencie z npm registry (Recharts 3.10.1 MIT, peer React 16.8 až 19; ECharts 6.1.0 Apache-2.0; echarts-for-react 3.0.6 podporuje echarts 3 až 6; React 19.3.1).

## Zdroje

- Recharts: https://recharts.org , https://github.com/recharts/recharts
- Apache ECharts (`dataZoom`, `sampling`, `markArea`): https://echarts.apache.org/en/option.html
- echarts-for-react: https://github.com/hustcc/echarts-for-react
- npm registry: https://www.npmjs.com/package/recharts , https://www.npmjs.com/package/echarts

## Nadväzujúce issues

1. Prototyp: graf výroba vs. predikcia s anomáliami na 10 000 bodoch (Recharts), meranie renderu a bundle; ak neprejde prah, prepnúť na ECharts.
2. Komponent `ProductionChart` s abstrakciou knižnice a témou.
3. Backend: endpoint agregácie časových rád s parametrom rozlíšenia (hodina/deň) pre redukciu bodov.
