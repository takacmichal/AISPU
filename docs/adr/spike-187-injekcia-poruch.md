# ADR spike #187: Dáta s poruchami, dataset anomálií vs. syntetická injekcia

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #187 (nadradené #7)
- **Súvisí:** #185 (kontrakt telemetrie, `fault_labels`), #194 (definícia podvýroby), #193 (metriky)

## Kontext

Bez označených porúch nevieme počítať precision/recall. Verejné datasety (PVDAQ, Kaggle) zvyčajne nemajú labely príčin porúch; PVDAQ má len `qa_status`/`qa_issue` na úrovni systému (napr. clipping, málo dát), nie časové intervaly porúch. Simulátor (spike #185) má generovať telemetriu s pravdivými labelmi.

## Zvažované možnosti

| Možnosť | Výhody | Nevýhody |
|---|---|---|
| A. Verejný dataset s poruchami | reálne chyby | v rámci spiku sa nenašiel vhodný dataset s labelmi porúch na úrovni DC/AC telemetrie a s jasnou licenciou (prieskum nebol vyčerpávajúci, neoverené); iné frekvencie/stĺpce; malý počet porúch |
| B. Manuálne označiť PVDAQ | reálne dáta | nákladné, subjektívne, prah nejasný |
| **C. Syntetická injekcia do čistých dát/simulátora** | pravdivé labely, riadená frekvencia a závažnosť, reprodukovateľnosť (seed), škálovanie | domain gap (model naučený na syntetike), realizmus závisí od modelu porúch |
| D. Hybrid C + injekcia do reálnych PVDAQ čistých období | menší domain gap, reálne počasie a šum | zložitejšie; použiť ako validáciu |

## Rozhodnutie

**C ako základ, D ako validácia.** Poruchy sa injektujú syntetickou transformáciou *očakávaného* výkonu; 5 typov podľa rozhodnutia tímu:

| Typ | Efekt | Parametre (návrh) | Trvanie |
|---|---|---|---|
| `soiling` | multiplikatívna strata rastúca lineárne | 0,1 až 0,3 %/deň, strop 25 %, reset pri daždi (>= 5 mm/deň) alebo čistení | 20 až 90 dní |
| `shading` | strata v časovom okne dňa (jeden string, strom/komín) | 20 až 60 % straty, okno 2 až 4 h okolo pevného času, opakuje sa denne | 14 až 60 dní |
| `string_outage` | string = 0 A, výkon -(1/počet stringov) | 1 string z 2 až 8; `strings[i]` = 0 | 1 až 14 dní |
| `derating` | strop výkonu pri vysokej teplote, `status = THROTTLED` | cap 60 až 80 % menovitého výkonu pri T_modulu > 65 °C | 3 až 21 dní |
| `inverter_fault` | `status = FAULT`, `errorCode != 0`, výkon 0 | celé inštalácie | 1 až 48 h |

Plus negatívna trieda (nie podvýroba): výpadok komunikácie (chýbajúce vzorky), zamrznutá hodnota, odľahlá hodnota (robustnosť, ADR #183).

**Frekvencia a prevalencia:** poruchy ako Poissonov proces; 3 až 8 % inštalácií má v danom týždni aktívnu poruchu (nie 50 %); typy v pomere soiling 35 %, shading 20 %, string_outage 20 %, derating 15 %, inverter_fault 10 %. Fixný `seed` na inštaláciu a beh.

**Značenie ground truth (dve vrstvy):**

1. `fault_events` (príčina): `fault_id`, `device_id`, `type`, `start`, `end`, `severity` (veľkosť straty). Samostatná tabuľka/súbor `fault_labels`, **mimo ingest kontraktu** (rozhodnutie #185).
2. Per vzorku (hodinu): `fault_active` (príčina aktívna), `expected_power_w` (výkon bez poruchy), `is_underperforming = P_actual/P_expected < 0,85` cez deň (GHI > 50 W/m²), definícia z ADR #194.

Dôvod dvoch vrstiev: pri pomalom soilingu (0,2 %/deň) prekročí strata 15 % až po ~75 dňoch; hodiny pred prekročením prahu nie sú "podvýroba" podľa pravidla, hoci porucha už existuje. Hodnotenie detekcie používa `is_underperforming` ako pravdu, `fault_active` slúži na rozbor podľa typu.

**Ťažké negatívy:** zámerne dodať oblačnosť, orezanie (clipping) a noc, aby model nezamieňal počasie s poruchou.

**Delenie:** train/validácia/test podľa času a podľa inštalácií (žiadne náhodné delenie riadkov, únik dát). Test obsahuje inštalácie, ktoré model nevidel.

**Realizovateľnosť:** pvlib `ModelChain` (PVWatts) dáva `P_expected`; porucha je transformácia výstupu (násobenie, orezanie, nulovanie stringu), teda realizovateľné v jednom module simulátora. Overenie implementáciou je v nadväzujúcom issue.

## Dôsledky

- Presné labely a reprodukovateľnosť; metriky detekcie (#193) sa dajú spočítať.
- Výsledky na syntetike sú optimistické; povinná validácia na reálnych (neporuchových) dátach PVDAQ (D) a zdokumentovanie domain gap.
- `fault_labels` a injektor sú modul simulátora, nie API.

## Riziká / neoverené

- Neoverené: hodnoty parametrov (straty, trvanie) sú inžinierske odhady z literatúry/domény, nie merané; soiling rate 0,1 až 0,3 %/deň je typický rád, nie z konkrétneho zdroja v tomto spiku.
- Neoverené: neexistencia vhodného verejného datasetu porúch (prieskum rýchly).
- Model sa môže naučiť artefakty injektora (napr. ostré hrany); mitigácia: náhodné tvary prechodov, šum, D validácia.

## Zdroje

- Kontrakt telemetrie a injekcia (interný spike #185): `docs/adr/` po merge príslušného PR
- pvlib ModelChain: https://pvlib-python.readthedocs.io/en/stable/user_guide/modelchain.html
- PVDAQ `systems.csv` (`qa_status`, `qa_issue`): https://oedi-data-lake.s3.amazonaws.com/pvdaq/csv/systems_20250729.csv

## Nadväzujúce issues

1. Modul injekcie porúch v simulátore + export `fault_labels` (5 typov, seed, parametre z tabuľky).
2. Validačná sada: injekcia do čistých období PVDAQ a porovnanie s čisto syntetickou.
3. Prehľad verejných datasetov porúch FVE (dôkladnejší prieskum) a rozhodnutie, či ich použiť na externú validáciu.
