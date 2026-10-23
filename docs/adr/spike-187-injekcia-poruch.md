# ADR spike #187: Dáta s poruchami, reálne poruchy z DKASC vs. syntetická injekcia

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15 (revízia po rozhodnutí tímu o datasete DKA)
- **Issue:** #187 (nadradené #7)
- **Súvisí:** #178 (produkčný dataset DKA), #180 (počasie Open-Meteo), #185 (kontrakt telemetrie, `fault_labels`), #194 (definícia podvýroby), #193 (metriky)

## Kontext

Bez označených porúch nevieme počítať precision/recall. Pôvodný záver tohto spiku bol, že vhodný verejný dataset s poruchami sa nenašiel (PVDAQ a Kaggle majú najmä `qa_status`/`qa_issue` na úrovni systému, nie časové intervaly porúch). Tím medzitým rozhodol (#178), že produkčným datasetom je **DKA Solar Centre (Alice Springs), roky 2023 až 2024**: 5-minútové záznamy z ~49 meračov jednotlivých FV polí, 2 hlavných meračov areálu a meteostanice, s metadátami každého poľa (výkon, technológia, sklon, azimut). Dataset obsahuje **reálne poruchy**:

- M19 C nevyrába od 2024-02-05,
- M15 C Phase II takmer nevyrába od januára do apríla 2024.

Odpoveď na otázku "nájde sa dataset s poruchami?" je teda **áno**, aspoň čiastočne: poruchy sú zistené inšpekciou/prevádzkou areálu, nie označené v samostatnej tabuľke udalostí. Časové hranice a príčinu treba doplniť sami (pozri nižšie). PVDAQ a Kaggle už nie sú primárne dáta.

Simulátor (spike #185) ďalej generuje telemetriu pre slovenské inštalácie, ktoré reálne poruchy nemajú, s pravdivými labelmi.

## Zvažované možnosti

| Možnosť | Výhody | Nevýhody |
|---|---|---|
| A. Len reálne poruchy z DKASC | reálne dáta, reálne počasie a šum, žiadny domain gap voči Austrálii | veľmi málo udalostí (2 známe polia, 1 až 2 dlhé poruchy, žiadny soiling/shading label); jeden typ porúch (útlm celého poľa); Austrália, nie Slovensko; nie je pokryté `string_outage`, `derating`, `inverter_fault` |
| B. Len syntetická injekcia | pravdivé labely, riadená frekvencia a závažnosť, reprodukovateľnosť (seed), 5 typov | domain gap, realizmus závisí od modelu porúch, riziko učenia artefaktov injektora |
| **C. Hybrid: reálne poruchy = hodnotenie, syntetika = tréning/augmentácia a simulátor** | reálna validácia bez úniku z injektora; syntetika pokrýva typy, ktoré v DKASC nie sú | zložitejšie; treba ručne vymedziť reálne udalosti |
| D. Injekcia aj do čistých polí DKASC | reálne počasie a šum, väčší počet udalostí | stále syntetické labely, nie nezávislé hodnotenie; použiť len ako doplnok |

## Rozhodnutie

**C: reálne poruchy z DKASC sú držaná (held-out) hodnotiaca sada; syntetická injekcia slúži ako tréningová augmentácia a ako generátor porúch v slovenskom simulátore.** Syntetika sa **nikdy nepoužije ako jediný dôkaz** kvality detekcie.

### 1. Reálne poruchy (hodnotenie)

- **Detekcia porovnaním polí (peer comparison):** ~49 polí je na jednom mieste, s rovnakým počasím. Pole s abnormálne nízkym pomerom `P_actual / P_expected` oproti ostatným poliam s podobným sklonom/azimutom je podozrivé. Toto slúži (a) na nájdenie a spresnenie hraníc reálnych udalostí a (b) ako nezávislá kontrola voči modelovému `P_expected`.
- **Vymedzenie udalostí:** pre každú reálnu poruchu (M19 C od 2024-02-05, M15 C Phase II január až apríl 2024, prípadne ďalšie nájdené pri prehľade polí) sa ručne zapíše `fault_event` (`device_id`, `start`, `end`, `type = array_outage`, zdroj = "inšpekcia/dáta DKASC"). Pôvod labelu (inšpekcia vs. odvodené z dát) sa eviduje, aby sa nepoužila kruhová pravda (pozri nižšie).
- **Vylúčené:** merače bez dát, prázdny stĺpec rýchlosti vetra, ~11 chýbajúcich dní v 2023 (rieši ADR #183: neplatné hodiny, nie poruchy).
- **Pole s poruchou sa nikdy nepoužije v tréningu modelu `P_expected`** (ani pred poruchou, pokiaľ sa nepoužije leave-one-array-out, pozri ADR #191), inak sa model naučí poruchu ako normu.
- **Limit:** reálnych udalostí je len niekoľko (rádovo 2 až 4). Výsledky sa reportujú s absolútnymi počtami a nie ako štatisticky spoľahlivé percentá; ide o dôkaz realizovateľnosti, nie o presný odhad recall.

### 2. Syntetická injekcia (tréning, augmentácia, simulátor)

Poruchy sa injektujú syntetickou transformáciou *očakávaného* výkonu; 5 typov podľa rozhodnutia tímu:

| Typ | Efekt | Parametre (návrh) | Trvanie |
|---|---|---|---|
| `soiling` | multiplikatívna strata rastúca lineárne | 0,1 až 0,3 %/deň, strop 25 %, reset **len čistením alebo dažďom >= 5 mm/deň** (pozri nižšie) | 20 až 90 dní (do resetu) |
| `shading` | strata v časovom okne dňa (jeden string, strom/komín) | 20 až 60 % straty, okno 2 až 4 h okolo pevného času, opakuje sa denne | 14 až 60 dní |
| `string_outage` | string = 0 A, výkon -(1/počet stringov) | 1 string z 2 až 8; `strings[i]` = 0 | 1 až 14 dní |
| `derating` | strop výkonu pri vysokej teplote, `status = THROTTLED` | cap 60 až 80 % menovitého výkonu pri T_modulu > 65 °C | 3 až 21 dní |
| `inverter_fault` | `status = FAULT`, `errorCode != 0`, výkon 0 | celé inštalácie | 1 až 48 h |

Plus negatívna trieda (nie podvýroba): výpadok komunikácie (chýbajúce vzorky), zamrznutá hodnota, odľahlá hodnota (robustnosť, ADR #183).

**Konzistencia soilingu a dažďa.** Predchádzajúca verzia mala súčasne reset dažďom >= 5 mm/deň a budovanie 20 až 90 dní, čo sa pri častých dažďoch (Slovensko) vylučuje. Pravidlo je teraz:

- Trvanie 20 až 90 dní je **maximálna dĺžka epizódy bez resetu**. Epizóda končí buď vypršaním trvania, alebo resetom.
- Reset dažďom (>= 5 mm/deň) sa aplikuje len ak je dážď v predpovedi/archíve počasia danej lokality; pri injekcii sa epizóda pred zvolením trvania **orezáva na prvý dážď >= 5 mm/deň** a nasledujúce obdobie je čisté. Zámer: soiling s dlhým trvaním sa generuje len v suchých obdobiach (letné obdobie Slovenska, dlhé suché obdobia Alice Springs); v daždivých obdobiach vznikajú len krátke epizódy.
- Dôsledok: skutočné trvanie je `min(zvolené trvanie, čas do prvého dažďa >= 5 mm)`; epizódy kratšie ako 20 dní sa zahodia (nespĺňajú tabuľku). Pri 0,2 %/deň je potrebných ~75 dní na prekročenie prahu 15 % (ADR #194), takže **len epizódy dlhšie ako ~50 až 150 dní môžu byť `is_underperforming`**; väčšina soilingu bude teda mať `fault_active = 1`, ale nikdy `is_underperforming = 1`. Je to zámerné a v hodnotení sa to reportuje (pozri recall podľa typu).

**Podiel `string_outage` a prah 15 %.** Strata pri výpadku 1 z N stringov je 1/N. Prah podvýroby (`r < 0,85`, strata > 15 %) sa prekročí len pre N <= 6 (1/6 = 16,7 %); pre N = 7 (14,3 %) a N = 8 (12,5 %) výpadok **nikdy** nebude `is_underperforming`. Pri rovnomernom výbere N z {2, ..., 8} je teda očakávane **5/7 (~71 %)** udalostí `string_outage` označiteľných ako podvýroba a ~29 % (N = 7, 8) nie. Tieto udalosti zostávajú vo `fault_events` a v `fault_active`, ale detektor ich podľa pravidla #194 nemá zachytiť. Pri vyhodnotení sa reportujú samostatne ako "pod prahom detekcie", nie ako zlyhanie recall. Rovnaká logika platí pre `shading` (celková strata na zdroji = (strata stringu) / N).

**Frekvencia a prevalencia:** poruchy ako Poissonov proces; 3 až 8 % inštalácií má v danom týždni aktívnu poruchu (nie 50 %); typy v pomere soiling 35 %, shading 20 %, string_outage 20 %, derating 15 %, inverter_fault 10 %. Fixný `seed` na inštaláciu a beh. Pomery sú rozdelenie *injektovaných* udalostí, nie predpoklad o realite.

### 3. Značenie ground truth (dve vrstvy)

1. `fault_events` (príčina): `fault_id`, `device_id`, `type`, `start`, `end`, `severity` (veľkosť straty), `source` (`real_dkasc` | `synthetic`). Samostatná tabuľka/súbor `fault_labels`, **mimo ingest kontraktu** (rozhodnutie #185).
2. Per vzorku (hodinu): `fault_active` (príčina aktívna), `expected_power_w` (výkon bez poruchy), `is_underperforming = P_actual/P_expected < 0,85` cez deň (GHI > 50 W/m²) **a hodina je platná podľa ADR #194**.

Dôvod dvoch vrstiev: pri pomalom soilingu (0,2 %/deň) prekročí strata 15 % až po ~75 dní; hodiny pred prekročením prahu nie sú "podvýroba" podľa pravidla, hoci porucha už existuje.

**Zosúladenie s ADR #194 (pravidlo 3): `inverter_fault`.** Pôvodný text definoval `is_underperforming` aj pre `inverter_fault` (status `FAULT`, výkon 0), no ADR #194 pravidlo 3 hodiny so stavom `OFF/SLEEPING/FAULT` označuje ako **neplatné** a rieši ich samostatným alertom výpadku/poruchy. Rozhodnutie:

- `inverter_fault` **nie je trieda detektora podvýroby.** Pre jeho hodiny je `is_underperforming = NA` (hodina neplatná), nie 0 ani 1, takže sa nezapočítavajú ani ako FP, ani ako FN detektora podvýroby.
- Zostáva vo `fault_events` a `fault_active = 1`, a hodnotí sa **samostatne** ako udalostný alert stavu striedača (pravidlo: `status = FAULT`), s vlastným recall/precision (triviálne vysoký, kontrola správnosti pipeline), mimo metriky podvýroby (#193).
- Závažnosť v #194 ("inverter fault" = High) sa týka tiketu z alertu stavu striedača, nie z pravidla `r < 0,85`.
- V reálnych dátach DKASC nie je stav striedača k dispozícii na úrovni poľa; úplný výpadok poľa s výkonom 0 pri GHI > 50 je tam `r = 0` a stav je neznámy, teda platná hodina podvýroby (typ `array_outage`).

### 4. Kruhová pravda a recall podľa typu

Syntetické labely sú z definície konzistentné s injektorom, ktorý vychádza z rovnakého `P_expected` ako detektor, takže presnosť na syntetike je optimistická (kruhová pravda). Preto:

- Hlavné čísla kvality sa reportujú na **reálnych poruchách DKASC** (held-out), syntetika slúži na tréning a ladenie.
- **Recall sa reportuje po typoch porúch** (`soiling`, `shading`, `string_outage`, `derating`, `array_outage` reálne) a nie len jedno agregované číslo; agregát by miešal ľahké typy (hlboký výpadok) s ťažkými (pomalý soiling, malé tienenie) a závisel by od zvoleného pomeru typov.
- Pri každom type sa zvlášť uvedie podiel udalostí pod prahom detekcie (soiling < 15 %, `string_outage` N >= 7).
- Injektor nepoužíva ten istý model `P_expected` ako detektor, ak je to možné (iný seed šumu, iná parametrizácia), a prechody majú náhodný tvar a šum.

**Ťažké negatívy:** zámerne dodať oblačnosť, orezanie (clipping) a noc, aby model nezamieňal počasie s poruchou. V DKASC sú ťažké negatívy prirodzené: ostatné polia pri tom istom počasí bez poruchy.

**Delenie:** podľa času a podľa polí/inštalácií (žiadne náhodné delenie riadkov, únik dát). Test = polia s reálnymi poruchami (a/alebo polia vynechané z tréningu); detaily walk-forward / leave-one-array-out v ADR #191.

**Realizovateľnosť:** pvlib `ModelChain` (PVWatts) dáva `P_expected`; porucha je transformácia výstupu (násobenie, orezanie, nulovanie stringu), teda realizovateľné v jednom module simulátora. Overenie implementáciou je v nadväzujúcom issue.

## Dôsledky

- Reálna validácia na skutočných poruchách; pravdivé labely a reprodukovateľnosť pre syntetické typy; metriky (#193) sa dajú počítať na oboch zdrojoch, ale s rôznou výpovednou hodnotou.
- Výsledky na syntetike sú optimistické; vždy sa uvádzajú vedľa výsledkov na reálnych poruchách a po typoch.
- Veľmi malý počet reálnych udalostí obmedzuje štatistickú silu hodnotenia.
- `fault_labels` a injektor sú modul simulátora, nie API.

## Riziká / neoverené

- Neoverené: presné začiatky a konce reálnych porúch (M19 C, M15 C Phase II) a prípadné ďalšie poruchy; podľa popisu datasetu (#178), treba overiť v dátach.
- Neoverené: hodnoty parametrov injekcie (straty, trvanie) sú inžinierske odhady, nie merané; soiling rate 0,1 až 0,3 %/deň je typický rád, nie z konkrétneho zdroja v tomto spiku.
- Domain gap Alice Springs (púšť, vysoké GHI, málo dažďa) vs. Slovensko: reálne poruchy overujú metódu, nie presné prahy pre slovenské podmienky.
- Model sa môže naučiť artefakty injektora (napr. ostré hrany); mitigácia: náhodné tvary prechodov, šum, hodnotenie na reálnych poruchách.
- Peer comparison predpokladá, že väčšina polí v danom čase nie je porucha; pri spoločnej príčine (napr. celoareálový výpadok) zlyhá.

## Zdroje

- Dataset DKA Solar Centre, Alice Springs (issue #178): https://dkasolarcentre.com.au/download?location=alice-springs
- Open-Meteo Historical Forecast API (issue #180): https://open-meteo.com/en/docs/historical-forecast-api
- Kontrakt telemetrie a injekcia (interný spike #185): `docs/adr/` po merge príslušného PR
- Definícia podvýroby (interný spike #194), pravidlo 3 (platná hodina)
- pvlib ModelChain: https://pvlib-python.readthedocs.io/en/stable/user_guide/modelchain.html

## Nadväzujúce issues

1. Modul injekcie porúch v simulátore + export `fault_labels` (5 typov, seed, parametre z tabuľky, reset soilingu dažďom).
2. Vymedzenie a zápis reálnych udalostí DKASC (`fault_events`, `source = real_dkasc`) vrátane peer-comparison prehľadu ~49 polí.
3. Hodnotenie na reálnych poruchách: recall podľa typu, počty udalostí, samostatné vyhodnotenie alertu `inverter_fault`.
4. Voliteľne: augmentácia tréningu syntetikou a porovnanie modelu s/bez augmentácie.
