# ADR-178: Verejný dataset produkcie FVE pre simulátor a trénovanie modelu

- Stav: prijaté (Sprint 0)
- Dátum: 2026-10-15
- Issue: #178

> Dátum overenia: 2026-10-15. Všetky čísla označené "overené" som zistil priamo (curl / fetch stránky); "neoverené" znamená, že som to nevedel potvrdiť z primárneho zdroja.

## Kontext

SolarOps potrebuje (a) simulátor, ktorý generuje realistické dáta elektrární (invertory, DC/AC výkon, teplota modulu, ožiarenie), a (b) dáta na trénovanie ML modelov: predpoveď produkcie a detekcia podprodukcie. Pre podprodukciu potrebujeme viac invertorov na jednej elektrárni (porovnanie "peer" invertorov) a meteo senzory na mieste. Potrebujeme aj dostatočne dlhú históriu (aspoň 1 rok, aby model videl sezónnosť). Projekt je školský, takže licencia musí povoliť nekomerčné použitie a publikovanie výsledkov.

## Zvažované možnosti

| Dataset | História | Frekvencia | Počet systémov | Stĺpce | Licencia / podmienky |
|---|---|---|---|---|---|
| Kaggle Solar Power Generation Data (2 indické elektrárne) | 34 dní (2020-05-15 až 2020-06-17), overené | 15 min, overené | 2 elektrárne x 22 invertorov, overené | DC_POWER, AC_POWER, DAILY_YIELD, TOTAL_YIELD; senzor: AMBIENT_TEMPERATURE, MODULE_TEMPERATURE, IRRADIATION | Kaggle API vracia licenciu "Data files © Original Authors" (overené). Nie je to otvorená licencia, použitie je neisté, bez explicitného povolenia na redistribúciu |
| NREL PVDAQ (OEDI Data Lake, S3) | podľa systému; napr. systém 2: 2010-01 až 2020-01 (~10 r.), overené v `systems.csv` | zväčša 1 min až 15 min (systém 4 vzorka: 1 min, overené) | stovky systémov (1862 riadkov v `systems.csv`, overené), s metadátami (sklon, azimut, kWp, poloha) | podľa systému: ac_power, dc_power, module_temp, ambient_temp, poa_irradiance, napätia/prúdy (systém 4, overené) | CC BY 4.0 podľa katalógového záznamu (data.gov harvest, údaje k 07/2025); pred použitím overiť na landing page. Voľný S3 prístup bez účtu, overené |
| DKA Solar Centre (Alice Springs) | viac rokov (od ~2008), presný rozsah som nezistil | 5 min | ~20+ systémov rôznych technológií (neoverené presne) | výkon, napätia, meteo; presné stĺpce neoverené | "free, open access" podľa webu; text podmienok som nedokázal načítať (neoverené). Upozornenie z 08/2024: 5-min CSV občas neúplné, kompletné dáta na požiadanie e-mailom |
| NREL PVWatts / NSRDB | model, nie merania | hodinová | kdekoľvek | simulovaný výkon | verejné NREL dáta; PVWatts nie sú reálne merania, nehodí sa na detekciu anomálií |
| PVGIS (JRC) | satelitné ožiarenie 2005 až 2023 | hodinová | kdekoľvek, aj SR | simulovaná produkcia, nie merania | EU JRC, voľný; presné podmienky neoverené. Dobrý na dlhodobé očakávané hodnoty a ako "baseline" simulátora, nie ako merané dáta |
| PVOutput | podľa používateľa | 5 až 15 min | tisíce domácich systémov | len celkový výkon/výroba, bez invertorov | API: 60 req/h (300 v donation mode), Get Output max 50 záznamov na požiadavku (overené). Podmienky bulk/komerčného použitia na samostatných stránkach, neoverené. Nevhodné na hromadné sťahovanie |
| Open Power System Data | od ~2012 | 15 min / 60 min | agregát za krajinu (nie jednotlivé elektrárne) | výroba solár/vietor, kapacity | otvorená licencia (CC BY pri verejnom balíku, neoverené v tejto verzii); nehodí sa na invertorovú úroveň |

Kaggle vzorka bola počas spiku stiahnutá lokálne (nie je súčasťou repa). Kontrola chýbajúcich hodnôt (overené):

- Plant 1: 68 778 riadkov výroby, 3 158 unikátnych časov, chýba 106 15-min slotov; senzor 3 182 riadkov, chýba 82 slotov. Plant 2: chýba po 5 slotov. Prázdne bunky: 0.
- Formát času sa líši medzi súbormi (Plant 1 generácia `dd-mm-yyyy HH:MM`, ostatné ISO), pozor pri parsovaní.
- Teda dáta sú čisté, ale s dierami a len 34 dní. Žiadna sezónnosť, nevhodné na trénovanie predpovede, použiteľné len na ukážku a detekciu anomálií.

PVDAQ vzorka (stiahnutá lokálne počas spiku, nie je v repe; dá sa znovu stiahnuť z OEDI S3): (`systems.csv` s metadátami a `s4.csv` = 1 deň systému 4, 1 440 riadkov po 1 min, stĺpce `ac_power__315`, `dc_power__314`, `module_temp_1..3`, `ambient_temp__320`, `poa_irradiance__313`).

## Rozhodnutie

- **Primárny dataset: NREL PVDAQ (OEDI)**. Dlhá história (roky), merania s POA ožiarením a teplotou modulu, metadáta systému (sklon, azimut, kWp), čistá licencia CC BY 4.0 a anonymný prístup cez S3. Vyberieme 3 až 5 systémov s `qa_status` bez problému, s aspoň 2 rokmi dát, a agregujeme na 15 min, aby zodpovedali očakávanému formátu SolarOps.
- **Záloha / rýchla ukážka: Kaggle Solar Power Generation Data**. Je to jediný zdroj s viacerými invertormi na jednej elektrárni a 15-min krokom, takže je dobrý na prototyp detekcie podprodukcie a na tvar API dát. Vzhľadom na nejasnú licenciu a 34 dní ho nepoužívať ako jediný zdroj, v repozitári nenahrávať surové súbory, len odkaz a skript na stiahnutie.
- **Simulátor**: syntetický generátor (clear-sky model + počasie z ADR-180 + šum + injektované poruchy), kalibrovaný na tvar PVDAQ a Kaggle dát. PVGIS len na kontrolu rádovosti ročnej produkcie pre SR.

## Dôsledky

- Dáta PVDAQ sú z USA (iná klíma ako SK). Model sa musí pretrénovať alebo doladiť na simulovaných slovenských dátach, inak hrozí posun rozdelenia. Pre SK testy použijeme synteticky generované elektrárne s reálnym počasím z Košíc.
- Treba napísať ETL (S3 -> parquet, resampling na 15 min, riešenie diery a rozdielov časových pásiem).
- V dokumentácii uviesť atribúciu (CC BY 4.0, DOI 10.25984/1846021).
- Pred použitím znovu overiť licenciu PVDAQ na landing page, katalógový záznam bol naposledy aktualizovaný 07/2025.
- DKASC ostáva neoverený kandidát; ak by sme ho chceli, treba najprv získať text podmienok (e-mail info@dkasolarcentre.com.au).

## Zdroje

- Kaggle dataset: https://www.kaggle.com/datasets/anikannal/solar-power-generation-data (licencia z API: https://www.kaggle.com/api/v1/datasets/view/anikannal/solar-power-generation-data)
- PVDAQ landing: https://data.openei.org/submissions/4568 (DOI 10.25984/1846021), S3: https://oedi-data-lake.s3.amazonaws.com/pvdaq/csv/systems_20250729.csv
- PVDAQ primer: https://openei.org/wiki/PVDAQ/Primer
- DKASC: https://dkasolarcentre.com.au/ a https://dkasolarcentre.com.au/source/dkp-microgrid/dkasc-alice-springs
- PVOutput API: https://pvoutput.org/help/api_specification.html
- PVGIS: https://pvgis.com/ (komerčný front-end; oficiálny JRC PVGIS som nefetchoval)
- OPSD: https://data.open-power-system-data.org/time_series/2019-06-05/README.md

## Nadväzujúce issues

- ETL skript: PVDAQ -> 15-min parquet (výber systémov, QA filter).
- Simulátor FVE (clear-sky + počasie + injekcia porúch).
- Prototyp detekcie podprodukcie na Kaggle dátach (peer-invertor porovnanie).
- Overiť licenciu DKASC a PVDAQ (písomne do dokumentácie).
