# ADR-178: Verejný dataset produkcie FVE pre simulátor a trénovanie modelu

- Stav: prijaté (rozhodnutie tímu, upravené po Sprinte 0; pôvodná verzia odporúčala PVDAQ)
- Dátum: 2026-10-23 (pôvodná verzia 2026-10-15)
- Issue: #178

> Dátum overenia: 2026-10-23 (DKASC) a 2026-10-15 (Kaggle, PVDAQ a ostatné riadky tabuľky). Všetky čísla označené "overené" som zistil priamo (curl / fetch stránky); "neoverené" znamená, že som to nevedel potvrdiť z primárneho zdroja. **Dáta DKASC za 2023 a 2024 som v tomto spiku nesťahoval** (stiahnutie ide cez formulár), takže všetko o obsahu súborov (názvy meračov, poruchy, medzery) je prevzaté z popisu tímu v issue #178 a je označené "podľa tímu".

## Kontext

SolarOps potrebuje (a) simulátor, ktorý generuje realistické dáta elektrární (invertory, DC/AC výkon, teplota modulu, ožiarenie), a (b) dáta na trénovanie ML modelov: predpoveď produkcie a detekcia podprodukcie. Pre podprodukciu potrebujeme viac meračov/invertorov na jednej elektrárni (porovnanie "peer" polí), meteo senzory na mieste a ideálne **reálne poruchy**, na ktorých vieme detekciu otestovať. Potrebujeme dostatočne dlhú históriu (aspoň 1 rok, aby model videl sezónnosť). Projekt je školský, takže licencia musí povoliť nekomerčné použitie a publikovanie výsledkov.

## Zvažované možnosti

| Dataset | História | Frekvencia | Počet systémov | Stĺpce | Licencia / podmienky | Posúdenie |
|---|---|---|---|---|---|---|
| **DKA Solar Centre, Alice Springs** | stránka ponúka ročné súbory 2008 až 2026 (overené), pre nás roky 2023 a 2024 | 5 min (overené: formulár ponúka 5 minút / hodina / deň / mesiac) | na stránke 38 číslovaných "Single Technologies" + súčet všetkých + vyradené systémy (overené); podľa tímu ~49 meračov polí + 2 merače areálu | výkon, napätia, meteo (podľa tímu žiarenie global + diffuse, teplota, vlhkosť); presné stĺpce neoverené | autorské právo DKA, spravuje Ekistica; "free, open access" na úvodnej stránke, ale pozri sekciu Licencia (limit 5 000 buniek bez súhlasu) | **vybrané (primárny)**: roky dát, 5 min, metadáta polí, reálne poruchy |
| NREL PVDAQ (OEDI Data Lake, S3) | podľa systému; napr. systém 2: 2010-01 až 2020-01 (~10 r.), overené v `systems.csv` | zväčša 1 min až 15 min (systém 4 vzorka: 1 min, overené) | stovky systémov (1862 riadkov v `systems.csv`, overené), s metadátami (sklon, azimut, kWp, poloha) | podľa systému: ac_power, dc_power, module_temp, ambient_temp, poa_irradiance, napätia/prúdy (systém 4, overené) | CC BY 4.0 podľa katalógového záznamu (data.gov harvest, údaje k 07/2025); pred použitím overiť na landing page. Voľný S3 prístup bez účtu, overené | **záloha**: najčistejšia licencia, ale heterogénne systémy, klíma USA a tím nemá vybrané známe poruchy; bola by väčšia ETL práca |
| Kaggle Solar Power Generation Data (2 indické elektrárne) | 34 dní (2020-05-15 až 2020-06-17), overené | 15 min, overené | 2 elektrárne x 22 invertorov, overené | DC_POWER, AC_POWER, DAILY_YIELD, TOTAL_YIELD; senzor: AMBIENT_TEMPERATURE, MODULE_TEMPERATURE, IRRADIATION | Kaggle API vracia licenciu "Data files © Original Authors" (overené). Nie je to otvorená licencia, použitie je neisté, bez explicitného povolenia na redistribúciu | **zamietnuté ako hlavný, použiteľné len na rýchly prototyp**: 34 dní = žiadna sezónnosť, nejasná licencia |
| NREL PVWatts / NSRDB | model, nie merania | hodinová | kdekoľvek | simulovaný výkon | verejné NREL dáta; PVWatts nie sú reálne merania | zamietnuté: nehodí sa na detekciu anomálií |
| PVGIS (JRC) | satelitné ožiarenie 2005 až 2023 | hodinová | kdekoľvek, aj SR | simulovaná produkcia, nie merania | EU JRC, voľný; presné podmienky neoverené | zamietnuté ako dataset; len kontrola rádovosti ročnej produkcie simulátora pre SR |
| PVOutput | podľa používateľa | 5 až 15 min | tisíce domácich systémov | len celkový výkon/výroba, bez invertorov | API: 60 req/h (300 v donation mode), Get Output max 50 záznamov na požiadavku (overené). Podmienky bulk/komerčného použitia neoverené | zamietnuté: bez invertorov, nevhodné na hromadné sťahovanie |
| Open Power System Data | od ~2012 | 15 min / 60 min | agregát za krajinu (nie jednotlivé elektrárne) | výroba solár/vietor, kapacity | otvorená licencia (CC BY pri verejnom balíku, neoverené v tejto verzii) | zamietnuté: nie invertorová úroveň |

Čo som pri DKASC priamo overil na stránkach (2026-10-23): zoznam 38 číslovaných polí (napr. "1A Trina, 10.5kW, mono-Si, Dual, 2009", "38 Q CELLS, 5.9kW, mono-Si, Fixed, 2017") s technológiou, výkonom, typom montáže (Fixed/Dual/Single tracking) a rokom inštalácie; ročné súbory 2008 až 2026; výber rozlíšenia 5 min / hodina / deň / mesiac; 5-min rok ≈ 7 MB a sťahovanie môže trvať ~10 min na rok; formulár vyžaduje súhlas s podmienkami a výber roly (napr. Student). Poznámka: stránka uvádza "Historical Weather Data (up to April 2019)" a Class A počasie od apríla 2019 odkazuje na projekt NT Solar Resource, takže **neviem potvrdiť, aké meteo stĺpce sú v ročných súboroch 2023 a 2024** (neoverené, treba zistiť po stiahnutí).

Rozpor na vyriešenie: názvy meračov z popisu tímu (napr. "M19 C", "M15 C Phase II", ~49 meračov, 2 merače areálu) som na verejných stránkach DKASC Alice Springs ani DKP Microgrid nenašiel (Alice Springs používa číslované systémy 1A až 38, Microgrid ponúka len BESS / Site Demand / Solar / Grid). Možno ide o označenie v stĺpcoch CSV alebo o iný export; neoverené, vyjasní sa pri prvom stiahnutí.

Kaggle vzorka bola počas pôvodného spiku stiahnutá lokálne (nie je súčasťou repa). Kontrola chýbajúcich hodnôt (overené):

- Plant 1: 68 778 riadkov výroby, 3 158 unikátnych časov, chýba 106 15-min slotov; senzor 3 182 riadkov, chýba 82 slotov. Plant 2: chýba po 5 slotov. Prázdne bunky: 0.
- Formát času sa líši medzi súbormi (Plant 1 generácia `dd-mm-yyyy HH:MM`, ostatné ISO), pozor pri parsovaní.
- Dáta sú čisté, ale s dierami a len 34 dní. Žiadna sezónnosť, nevhodné na trénovanie predpovede.

PVDAQ vzorka (stiahnutá lokálne počas pôvodného spiku, nie je v repe; dá sa znovu stiahnuť z OEDI S3): `systems.csv` s metadátami a `s4.csv` = 1 deň systému 4, 1 440 riadkov po 1 min, stĺpce `ac_power__315`, `dc_power__314`, `module_temp_1..3`, `ambient_temp__320`, `poa_irradiance__313`.

## Rozhodnutie

Rozhodnutie tímu (komentár v issue #178): **primárny dataset je DKA Solar Centre, Alice Springs** (https://dkasolarcentre.com.au/download?location=alice-springs), roky 2023 a 2024.

- **Rozsah (podľa tímu):** 5-min záznamy z ~49 meračov jednotlivých polí, 2 hlavných meračov areálu a meteostanice (globálne a difúzne žiarenie, teplota, vlhkosť). Každé pole má známe metadáta (výkon, technológia, sklon, azimut).
- **Reálne poruchy na test detekcie (podľa tímu):** merač M19 C nevyrába od 2024-02-05; M15 C Phase II takmer nevyrába od januára do apríla 2024. To je hlavný dôvod výberu: máme označené skutočné podprodukcie, nie len injektované.
- **Vylúčené (podľa tímu):** v roku 2023 chýba ~11 dní, stĺpec rýchlosti vetra je prázdny, niektoré merače sú bez dát. Tieto merače a stĺpce sa z datasetu vylúčia; zoznam vylúčených meračov a dôvod sa zapíše do ETL skriptu (config), nie ručne do kódu.
- **Počasie:** na tréning použijeme Open-Meteo Historical Forecast API aj pre Alice Springs (pozri ADR-180); vlastná meteostanica DKASC slúži ako **referencia/validácia** (porovnanie GHI predpovede a merania), kým nepotvrdíme, ktoré meteo stĺpce sú v súboroch 2023 a 2024.
- **Záloha:** NREL PVDAQ (CC BY 4.0) ak by licencia DKASC pre naše použitie nevyšla (pozri Licencia) alebo by dáta 2023 a 2024 boli horšie, než sa očakáva. Kaggle len ako rýchly prototyp tvaru API a peer-porovnania, nie ako zdroj trénovania.
- **Simulátor:** syntetický generátor slovenských elektrární (clear-sky model + reálne košické počasie z ADR-180 + šum + injektované poruchy), **kalibrovaný na tvar DKASC dát** (denný profil, vzťah výkon/ožiarenie, vplyv teploty). PVGIS len na kontrolu rádovosti ročnej produkcie pre SR.

## Dôsledky

- **Klíma a pologuľa:** Alice Springs (cca 23,8° j. š., púšť, výrazne vyššie ožiarenie a teploty) nie je Slovensko (cca 48,7° s. š.). Sezóna je opačná (leto je v decembri až februári), uhol slnka a optimálny sklon/azimut sú iné (panely smerujú na sever, nie na juh). Overené z Open-Meteo Historical Forecast (2023 až 2024, hodinový priemer GHI): Alice Springs 177 W/m² v júli 2023 a 345 W/m² v decembri 2023, Košice 33 W/m² v januári 2023 a 247 W/m² v júni 2023, s oblačnosťou Košíc 54 až 89 % oproti 4 až 63 % v Alice Springs. Model natrénovaný len na DKASC by sa na Slovensko neprenášal; preto **model na Slovensko trénujeme/dolaďujeme na syntetických slovenských elektrárňach** s reálnym košickým počasím, a DKASC používame hlavne na detekciu podprodukcie a validáciu tvaru simulátora (relatívne metriky voči peer poliam, nie absolútny výkon). Príznaky musia byť normalizované (napr. clearness index, výkon/kWp), nie absolútne.
- **Časová zóna:** Alice Springs používa Australia/Darwin, UTC+9:30 bez letného času (offset 34 200 s potvrdený v odpovediach Open-Meteo). DKASC stránky časovú zónu časových pečiatok v CSV neuvádzajú (neoverené); ETL ju musí overiť na vzorke (východ slnka, nulový výkon v noci) a hneď previesť na UTC, v súlade s #189. Polhodinový posun oproti celým hodinám znamená, že pri hodinovom joine s počasím treba dať pozor na zarovnanie intervalov (hodina počasia = priemer za predchádzajúcu hodinu).
- **Agregácia 5 min -> hodina:** surové dáta ostanú 5-min (zhodné s ADR-185, simulátor tiež 5 min). Pre ML a join s počasím sa počíta hodinový agregát (priemer výkonu, počet platných vzoriek). Hodinu s menej ako dohodnutým podielom vzoriek (napr. 80 %, prahová hodnota je na ETL issue) označiť ako neplatnú, nepoužívať ju ani ako nulu. DKASC formulár vie rozlíšenie "hodina" aj priamo, ale lokálna agregácia z 5 min je kontrolovateľnejšia.
- **Medzery a nespoľahlivé dáta:** v 2023 chýba ~11 dní (podľa tímu). Poznámky DKASC uvádzajú výpadky v roku 2023: odstávka lokality 2023-01-24 až 2023-01-30, výpadok sťahovania dát a údržba webu v septembri a októbri 2023 (overené na stránke Notes on the Data); možno čiastočne vysvetľujú chýbajúce dni (neoverené, len domnienka). Poruchu (M19 C, M15 C Phase II) nesmieme zamieňať s výpadkom merania: výpadok = chýbajúce záznamy, porucha = záznamy s nulovým/nízkym výkonom pri dostatočnom ožiarení. ETL musí tieto dva stavy rozlišovať.
- **Vylúčené merače a stĺpce:** prázdny stĺpec rýchlosti vetra sa z meteo dát DKASC nepoužije (vietor beriem z Open-Meteo). Merače bez dát sa vynechajú; ostatné sa evidujú s metadátami (kWp, technológia, sklon, azimut, rok inštalácie).
- **Heterogénne polia:** polia majú rôznu technológiu (mono/poly-Si, CdTe, CIGS, CPV) a časť je s trackerom (Dual/Single). Peer porovnanie podprodukcie robiť v rámci skupín s podobnou technológiou a sklonom, výkon normalizovať na kWp.
- **Veľkosť:** 5-min rok je podľa stránky ≈ 7 MB na pole (teda rádovo 100 000 riadkov na pole a rok); pre ~49 meračov a 2 roky ide o desiatky MB, zvládnuteľné lokálne. Sťahovanie je pomalé (~10 min na rok na pole podľa stránky), preto ho robiť raz a výsledok držať v parquet cache mimo gitu.
- **Pre produkčné nasadenie** (reálny zákazník, komerčné použitie) DKASC nie je zdrojom produkcie; slúži len na vývoj a validáciu modelu (pozri aj licencia).

## Licencia a podmienky (DKASC)

Overené 2026-10-23 na https://dkasolarcentre.com.au/download/terms-conditions:

- Úvodná stránka: dáta sú "available for public use on a free, open access basis". **Nie je to však otvorená licencia typu CC.** Podmienky: všetky dáta DKASC Alice Springs sú chránené autorským právom (vlastník Desert Knowledge Australia, spravuje Ekistica); okrem výnimiek zákona a výslovne udelených práv "all other rights are reserved".
- Bez formálneho súhlasu možno zobraziť, reprodukovať a distribuovať **najviac 5 000 buniek dát, len v nezmenenej forme**. Nad tento rozsah treba formálny súhlas DKA cez Ekistica (info@dkasolarcentre.com.au).
- Povinnosti pri publikovaní: presná a férová prezentácia (uviesť presné dátumy, jednotky, rok inštalácie technológie, vplyvy ako výpadky), uvedenie zdroja DKA Solar Centre, **povinné vyhlásenie (disclaimer) tesne pri publikovaných dátach** ("Desert Knowledge Australia, the Australian Government, the Northern Territory Government and the project managers, Ekistica do not endorse, and accept no legal liability ... ") a citácia v tvare "Desert Knowledge Australia Centre. dd/mm/yyyy. Download Data. Location (e.g. Alice Springs). http://dkasolarcentre.com.au/download, date accessed: dd/mm/yyyy." Požaduje sa aj informovať o publikáciách (nie povinnosť, "would also appreciate").
- Pred sťahovaním treba v formulári zaškrtnúť súhlas s podmienkami a uviesť rolu.

Čo z toho plynie (naše posúdenie, nie právne stanovisko, **neoverené u vlastníka**): 
- Text podmienok výslovne neupravuje interné použitie na analýzu a trénovanie modelu, upravuje len zobrazenie/reprodukciu/distribúciu. Interná analýza je pravdepodobne v duchu "free open access", ale 2 roky x ~49 meračov x 5 min je rádovo milióny buniek, teda ďaleko nad 5 000, ak by sme dáta zdieľali.
- Preto: **surové DKASC súbory a odvodené datasety s mnohými bunkami nedávať do repozitára ani verejného úložiska**, v repe len skript na stiahnutie a konfiguráciu; v dokumentácii a výstupoch (grafy, tabuľky, správa) dodržať limit 5 000 buniek v nezmenenej forme, uviesť citáciu a disclaimer.
- Ak chceme dáta zdieľať v tíme/GitHube alebo ich zahrnúť do demo prostredia, **požiadať písomne o súhlas** (info@dkasolarcentre.com.au) a odpoveď uložiť do `docs/`. Ak súhlas nepríde alebo bude nevyhovujúci, prejsť na zálohu (PVDAQ).
- Slovensko/EÚ: použitie na výučbu a výskum ani prenos dát z Austrálie som nehodnotil (neoverené).

## Záložný dataset a spúšťače prechodu

- **Záloha: NREL PVDAQ (OEDI)**, CC BY 4.0 (údaje o licencii z 07/2025, pred použitím overiť na landing page, atribúcia DOI 10.25984/1846021). Vyberieme 3 až 5 systémov s `qa_status` bez problému a aspoň 2 rokmi dát, agregácia na hodinu (rovnaký ETL ako DKASC).
- Prechod na zálohu, ak: (a) DKA odmietne súhlas, ktorý budeme potrebovať, (b) po stiahnutí zistíme, že 2023 a 2024 nemajú aspoň 2 reálne poruchy použiteľné na testy, alebo (c) chýbajúce dáta presiahnu rozumný limit (napr. viac ako ~20 % času vybraných meračov).
- Kaggle (indické elektrárne, 34 dní) sa nevyužije ako zdroj, len ako rýchla ukážka tvaru dát. Nepridávať surové súbory do repa.

## Zdroje

- DKASC download (Alice Springs): https://dkasolarcentre.com.au/download?location=alice-springs ; (DKP Microgrid): https://dkasolarcentre.com.au/download?location=dkp-microgrid
- DKASC Terms & Conditions: https://dkasolarcentre.com.au/download/terms-conditions ; Notes on the Data: https://dkasolarcentre.com.au/download/notes-on-the-data ; Glossary: https://dkasolarcentre.com.au/glossary ; úvodná stránka: https://dkasolarcentre.com.au/
- Komentár tímu v issue #178 (rozsah datasetu, poruchy, vylúčenia)
- Kaggle dataset: https://www.kaggle.com/datasets/anikannal/solar-power-generation-data (licencia z API: https://www.kaggle.com/api/v1/datasets/view/anikannal/solar-power-generation-data)
- PVDAQ landing: https://data.openei.org/submissions/4568 (DOI 10.25984/1846021), S3: https://oedi-data-lake.s3.amazonaws.com/pvdaq/csv/systems_20250729.csv
- PVDAQ primer: https://openei.org/wiki/PVDAQ/Primer
- PVOutput API: https://pvoutput.org/help/api_specification.html
- PVGIS: https://pvgis.com/ (komerčný front-end; oficiálny JRC PVGIS som nefetchoval)
- OPSD: https://data.open-power-system-data.org/time_series/2019-06-05/README.md

## Nadväzujúce issues

- Požiadať DKA/Ekistica o písomný súhlas na použitie a zdieľanie dát v tíme (info@dkasolarcentre.com.au) a uložiť odpoveď; do vtedy nič z dát necommitovať.
- Stiahnuť DKASC 2023 a 2024, spraviť profil dát (skutočné názvy meračov, časová zóna pečiatok, meteo stĺpce, medzery, overenie porúch M19 C a M15 C Phase II) a upraviť tento ADR podľa skutočnosti.
- ETL: DKASC CSV -> 5-min parquet -> hodinový agregát v UTC (výber meračov, vylúčenia v configu, rozlíšenie výpadok vs porucha).
- Simulátor FVE (clear-sky + počasie + injekcia porúch), kalibrovaný na DKASC tvar, slovenské lokality.
- Prototyp detekcie podprodukcie na DKASC (peer-porovnanie v skupinách, test na M19 C a M15 C Phase II).
- Záloha: overiť licenciu PVDAQ na landing page a pripraviť ETL (len ak sa spustí prechod na zálohu).
