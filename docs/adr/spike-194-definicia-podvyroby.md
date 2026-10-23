# ADR spike #194: Definícia podvýroby (prah, rezíduum, trvanie)

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #194 (nadradené #7)
- **Súvisí:** #191 (model `P_expected`), #187 (injekcia, labely), #193 (metriky), #189 (hodinová os)

## Kontext

Podvýroba = skutočná výroba je trvalo nižšia než očakávaná. Pravidlo musí odlíšiť poruchu od zlého počasia, noci, orezania výkonu a výpadku komunikácie, a výstup má nastaviť prioritu tiketu. Pravidlo beží na **hodinových** dátach (ADR #189) a používa `P_expected` z modelu (ADR #191) napočítané z *nameraného/analyzovaného* počasia za danú hodinu, nie z predpovede.

## Zvažované možnosti

| Téma | Možnosť | Posúdenie |
|---|---|---|
| Rezíduum | absolútne `P_expected - P_actual` | závisí od veľkosti systému a hodiny dňa |
| | **relatívne `r = P_actual / P_expected`** | porovnateľné medzi systémami, intuitívne |
| | z-skóre rezídua | vyžaduje odhad rozptylu, zložitejšie; odložené |
| Prah | 0,80 (príklad v issue) | menej falošných poplachov, pomalšie zachytí soiling a čiastočné tienenie |
| | **0,85** | rozhodnutie tímu; rezerva ~2x nad očakávanou chybou modelu (nMAE ~6 %, neoverené) |
| | 0,90 | veľa falošných poplachov pri chybe modelu a oblačnosti |
| Trvanie | 1 h | šum, oblaky |
| | **>= 2 po sebe idúce platné hodiny** | rozhodnutie tímu; tlmí prechodné oblaky |
| | >= 1 deň | príliš pomalé pre výpadok stringu |

## Rozhodnutie

Hodina `t` je **podvýrobná**, ak platí súčasne:

1. `GHI_t > 50 W/m²` (denné svetlo a ďaleko od terminátora; GHI z Open-Meteo),
2. `P_expected_t >= 0,05 × P_rated` (vyhne sa deleniu malým číslom),
3. hodina je **platná**: dáta existujú (ADR #183, >= 75 % vzoriek) a stav striedača nebol `OFF/SLEEPING/FAULT` (tie riešia alerty výpadku/poruchy zvlášť),
4. `r_t = P_actual_t / P_expected_t < 0,85`,
5. `P_expected` je orezané na AC limit striedača (clipping), aby orezanie nebolo podvýroba.

**Udalosť podvýroby** vzniká, keď je **>= 2 po sebe idúce platné podvýrobné hodiny** (hodinová os UTC). Neplatná hodina (chýbajúce dáta, GHI <= 50, nízke očakávanie) udalosť **nepreruší, ani nepredĺži** (preskakuje sa); ak medzi dvoma podvýrobnými hodinami sú > 3 platné hodiny s `r >= 0,85`, udalosť sa ukončí. Udalosť sa uzavrie po 2 po sebe idúcich platných hodinách s `r >= 0,85`.

**Zlé počasie vs. porucha:** `P_expected` sa počíta z reálneho počasia danej hodiny, takže oblačnosť znižuje aj očakávanie (zníženie GHI sa v `r` kompenzuje). Chyba predpovede počasia sa tým vylúči; výpadok dát počasia = hodina neplatná.

**Úrovne závažnosti (návrh, mapované na prioritu tiketu):**

| Úroveň | Podmienka (medián `r` v udalosti) | Priorita tiketu |
|---|---|---|
| Nízka | 0,70 <= r < 0,85 | Low |
| Stredná | 0,50 <= r < 0,70, alebo Nízka trvajúca >= 3 dni | Medium |
| Vysoká | r < 0,50, alebo Stredná trvajúca >= 3 dni, alebo inverter fault | High |

**Odskúšanie variantov (na injektovaných poruchách, ADR #187):** mriežka prah ∈ {0,75; 0,80; 0,85; 0,90} × trvanie ∈ {1; 2; 3; 4} hodín; pre každú bunku udalostné precision/recall/F1 a počet FP/inštalácia/30 dní (ADR #193) na validačnej sade. Zvolené (0,85; 2 h) sa potvrdí, ak F1 nie je horšie o > 0,03 než najlepšia bunka a FP/30 dní <= 1; inak sa úprava zapíše do tohto ADR. Tento experiment **ešte neprebehol**.

## Dôsledky

- Jednoduché, vysvetliteľné pravidlo (technik vidí `r`, trvanie, závažnosť); parametre v konfigurácii, nie v kóde.
- Pomalý soiling (0,1 až 0,3 %/deň) sa zachytí až po prekročení 15 % straty (~50 až 150 dní), čo je zámerný kompromis proti falošným poplachom. Dodatočný trendový detektor (pokles `r` v čase) je mimo rozsahu.
- Jemné tienenie na jednom stringu (<= 20 % celkovej straty) pod prahom nebude zachytené bez string-level dát.
- Závisí od kvality `P_expected`; zhoršenie modelu (drift) zvyšuje FP, preto monitoring (ADR #191).

## Riziká / neoverené

- Neoverené: vhodnosť prahov 0,85 / 2 h / 50 W/m² na reálnych a injektovaných dátach (experiment mriežky v nadväzujúcom issue); nMAE ~6 % je cieľ, nie meranie.
- Neoverené: hranice závažnosti sú návrh produktového/prevádzkového charakteru; potvrdiť s doménovým expertom.
- Pravidlo pracuje s hodinami, rýchle poruchy trvajúce < 2 h pokrýva samostatný alert stavu striedača (`FAULT`).

## Zdroje

- Interné rozhodnutia SolarOps pre ML (prah 0,85, GHI > 50, >= 2 h): tabuľka v Sprint 1 podkladoch
- Open-Meteo `shortwave_radiation` (GHI, priemer predchádzajúcej hodiny): https://open-meteo.com/en/docs
- pvlib ModelChain (PVWatts, clipping `ac_model`): https://pvlib-python.readthedocs.io/en/stable/user_guide/modelchain.html

## Nadväzujúce issues

1. `solarops_ml.detect`: pravidlo podvýroby, udalosti, závažnosť, konfigurácia prahov + unit testy (hranice, neplatné hodiny, clipping).
2. Experiment mriežky prah × trvanie na injektovaných poruchách a zápis výsledku.
3. Backend: zaznamenanie udalosti podvýroby a vytvorenie tiketu s prioritou podľa závažnosti (integrácia s ML službou, #196).
4. Voliteľne: trendový detektor pomalého soilingu.
