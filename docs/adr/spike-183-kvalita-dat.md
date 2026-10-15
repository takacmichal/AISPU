# ADR spike #183: Kvalita dát, chýbajúce hodnoty a licencie

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #183 (nadradené #5)
- **Súvisí:** #178 (dataset), #180 (počasie), #189 (join), #187 (poruchy)

## Kontext

Pred tréningom modelu treba vedieť, aké chyby majú vstupné dáta (PVDAQ, Open-Meteo, prípadne Kaggle), ako ich čistiť a či ich smieme dať do repa. Dáta idú do ML služby (Python 3.12, pandas) a všetky časy sa držia v UTC (viď ADR #189).

### Profilovanie (čo je zmerané)

| Zdroj | Zistenie | Stav |
|---|---|---|
| PVDAQ systém 4, vzorka 1 deň (`2015-06-20`, 1440 riadkov po 1 min) | 0 prázdnych buniek vo všetkých 17 stĺpcoch; `ac_power__315` min -0,92 (záporná noc), `poa_irradiance__313` min -5,5 W/m² (záporný offset senzora), max `ac_power` 755 | overené (vlastná kontrola) |
| PVDAQ `systems.csv` | obsahuje `timezone_or_utc_offset` (napr. `America/Denver`), `latitude`, `longitude`, `dc_capacity_kW`, `tilt`, `azimuth`, `first_timestamp`, `last_timestamp`, `qa_status` (`pass`/`fail`) a `qa_issue` (napr. "less than 1.0 years data", "Percent clipping exceeded threshold of 10%") | overené |
| Kaggle Plant 1 (34 dní, 15 min) | chýba 106 slotov výroby a 82 slotov senzora, Plant 2 po 5; prázdne bunky 0; rôzne formáty času medzi súbormi (`dd-mm-yyyy HH:MM` vs ISO) | overené (spike #178) |
| Open-Meteo (Košice, ukážka) | hodinové dáta bez dier; vietor v **km/h** (predvolená jednotka), žiarenie W/m², teplota °C; grid bod sa zaokrúhli (48.68/21.28) | overené (spike #180) |

Celé PVDAQ systémy (roky dát) zatiaľ neprofilované: profilovací skript je súčasť nadväzujúceho issue.

## Zvažované možnosti (čistenie chýbajúcich hodnôt)

| Možnosť | Výhody | Nevýhody |
|---|---|---|
| A. Vyradiť všetky riadky s chýbaním | jednoduché, bez umelých dát | strata dát, diery v časových oknách |
| B. Interpolovať všetko (lineárne) | súvislý rad | vymyslí cieľovú premennú (výkon), skreslí metriky, falošne "zahladí" výpadky, ktoré chceme detegovať |
| C. Hybrid: označiť (`is_missing`), cieľ nikdy neinterpolovať, krátke diery len vo vstupoch | zachová pravdivosť cieľa, výpadok je informácia | zložitejšie pravidlá |

## Rozhodnutie

Možnosť **C**. Konkrétne pravidlá (konfigurovateľné konštanty v `ml/`):

1. **Duplicity:** kľúč `(system_id, timestamp_utc)`; pri duplicite ponechať posledný záznam a počítať ich v reporte.
2. **Časová os:** všetko v UTC. PVDAQ lokálny čas sa konvertuje podľa `timezone_or_utc_offset` zo `systems.csv`; správnosť sa overí per systém: čas dennej špičky výkonu vs. solárne poludnie z `pvlib` musí sedieť na ±30 min (inak systém vyradiť/opraviť posun). Pri DST: nejednoznačné/neexistujúce časy sa neodhadujú, riadky sa označia a vyradia.
3. **Medzery:** hodinový agregát (pozri #189) je platný, len ak je prítomných aspoň 75 % vzoriek v hodine (9 z 12 pri 5 min). Inak `NaN` a v tréningu sa vynechá. Výkon (cieľ) sa **neinterpoluje**. Počasie (vstup) sa smie lineárne doplniť len pri dierach ≤ 2 h; dlhšie diery = riadok vynechať.
4. **Noc:** pri výške Slnka < 0° (z `pvlib`) sa výkon v rozsahu ±1 % `dc_capacity_kW` nastaví na 0 (offset senzora, príklad: -0,92 W v systéme 4); väčšia nenulová hodnota v noci = riadok označiť `night_nonzero` a vyradiť.
5. **Outliery:** výkon < -1 % alebo > 120 % `dc_capacity_kW` = vyradiť; záporné žiarenie orezať na 0; GHI nad 1,5 × clear-sky GHI označiť (cloud enhancement môže byť reálny, preto len flag, nie vyradiť); zamrznutá nenulová hodnota ≥ 6 po sebe idúcich vzoriek = `stuck`, vyradiť.
6. **Výber systémov (PVDAQ):** `qa_status = pass`, aspoň 2 roky dát, chýbajúce denné hodiny ≤ 10 % (návrh prahu, doladiť po profilovaní). Systémy s `qa_issue` o clippingu > 10 % nepoužívať pre model očakávanej výroby.
7. **Jednotky:** výkon W (kontrola rádu proti `dc_capacity_kW`), teplota °C, žiarenie W/m². Pri Open-Meteo vždy žiadať `wind_speed_unit=ms` a `timezone=UTC`. Do ML sa výkon normalizuje na W/kWp.
8. **Reportovanie:** profilovací skript vytvorí pre každý systém JSON/Markdown report (podiel chýbajúcich, duplicity, počet outlierov, vyradené riadky podľa dôvodu); report sa verziuje, surové dáta nie.

### Licencie a čo smieme commitnúť

| Zdroj | Licencia | Commit do repa? |
|---|---|---|
| NREL PVDAQ | CC BY 4.0 podľa katalógového záznamu (DOI 10.25984/1846021); na OEDI landing page je pole *License* prázdne, preto **neoverené priamo** | **Nie surové dáta.** Len skript na stiahnutie, zoznam `system_id` a atribúcia. Malé testovacie fixtury (do ~100 kB) až po písomnom potvrdení licencie; do vtedy syntetické |
| Open-Meteo | dáta CC BY 4.0 (atribúcia), free API len nekomerčne | Nie surové odpovede; cache lokálne/v DB, atribúcia v dokumentácii a UI |
| Kaggle Solar Power Generation | "Data files © Original Authors" (nie otvorená) | **Len odkaz**, nič nenahrávať |

Veľké súbory patria do `.gitignore` (`ml/data/`), stahujú sa skriptom.

## Dôsledky

- Cieľová premenná ostáva pravdivá, výpadky sa dajú detegovať (nie sú zamaskované interpoláciou).
- ETL musí riešiť časové pásma per systém a mať testy na DST (jar/jeseň).
- V dokumentácii a README treba atribúciu (PVDAQ DOI, Open-Meteo CC BY 4.0).
- Prahy (75 %, ±1 %, 120 %, 10 %) sú návrhy; po profilovaní celých systémov sa môžu upraviť, no zmena musí byť zapísaná v tomto ADR.

## Riziká / neoverené

- Neoverené: licencia PVDAQ priamo na landing page (prázdne pole), jednotky a časová konvencia (lokálny štandardný čas vs. DST) pre každý systém; PVDAQ files môžu byť v lokálnom čase bez DST.
- Neoverené: podiel chýbajúcich hodnôt na celých PVDAQ systémoch (profilovaná iba 1-dňová vzorka a Kaggle).
- Riziko: údaje z USA nie sú reprezentatívne pre SK (pozri ADR #178/#187).

## Zdroje

- PVDAQ: https://data.openei.org/submissions/4568 , primer https://openei.org/wiki/PVDAQ/Primer , `systems.csv` https://oedi-data-lake.s3.amazonaws.com/pvdaq/csv/systems_20250729.csv
- Open-Meteo licencia a limity: https://open-meteo.com/en/pricing
- Kaggle dataset: https://www.kaggle.com/datasets/anikannal/solar-power-generation-data
- pvlib solar position: https://pvlib-python.readthedocs.io/

## Nadväzujúce issues

1. Profilovací skript a report kvality (PVDAQ celé systémy, Kaggle) + výber 3 až 5 systémov.
2. ETL: S3 -> parquet, UTC, hodinové agregáty s pravidlami z tohto ADR, testy na DST.
3. Písomné potvrdenie licencie PVDAQ (landing page / NREL kontakt) a doplnenie atribúcie do README.
