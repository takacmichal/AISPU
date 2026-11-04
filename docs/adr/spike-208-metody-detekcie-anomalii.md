# ADR #208: Metódy detekcie anomálií (pravidlo vs. štatistika vs. Isolation Forest)

- Stav: Navrhnuté (výstup spiku #208, nadradené #7)
- Dátum: 2026-11-04
- Súvisí s: [ADR #194](spike-194-definicia-podvyroby.md) (pravidlo podvýroby), [ADR #187](spike-187-injekcia-poruch.md) (simulované poruchy), ADR #193 (metriky udalostí)
- Experiment: `ml/experiments/spike_208_anomaly_methods.py`, výsledky `ml/reports/spike-208-comparison.{md,json}`

## Kontext

Otázka: stačí pravidlová detekcia nad rezíduami (podiel `r = P_actual / P_expected`), alebo použiť štatistické metódy či Isolation Forest? Porovnanie beží na rovnakých simulovaných poruchách ako vyhodnotenie pravidla (`detection.evaluation`: 40 scenárov × 14 dní, seed 42, 13 labelov porúch). Všetky metódy majú rovnaké vstupy a rovnaké kroky okolo:

- hodiny sa validujú rovnako (`classify_hours`: noc, chýbajúce dáta, stav striedača, sneh), rozhodovacie pravidlo je jediný rozdiel;
- hodinové príznaky sa menia na udalosti rovnako (>= 2 po sebe idúce označené platné hodiny otvoria udalosť, 2 normálne hodiny ju zatvoria);
- udalosti sa párujú s labelmi podľa ADR #193 (`match_events`).

Kontrola správnosti rámca: pravidlo v experimente dáva 0,45 / 1,00 / 0,62 a 13 TP / 16 FP, čo sa zhoduje s existujúcim reportom `detection-eval.md`.

## Porovnávané metódy

1. **Pravidlo (ADR #194):** `r < 0,85` aspoň 2 hodiny, s filtrami pre oblačnosť a sneh.
2. **Robustný z-score:** medián a MAD z `r` platných hodín jednej inštalácie, príznak `z < -k`.
3. **Isolation Forest** (scikit-learn, 200 stromov, `random_state=42`) nad `[r, P_actual/P_rated, sin/cos hodiny]` na jednu inštaláciu; príznak len pre outliery s `r` pod mediánom (podvýrobná strana).

## Výsledky

| Metóda | Precision | Recall | F1 | TP | FP | FN | Falošné / 30 dní | Medián oneskorenia (h) | Čas (s) |
|---|---|---|---|---|---|---|---|---|---|
| Pravidlo (0,85; >= 2 h) | 0,45 | 1,00 | 0,62 | 13 | 16 | 0 | 0,86 | 2,0 | 0,15 |
| Z-score < -2 | 0,33 | 0,92 | 0,49 | 12 | 22 | 1 | 1,29 | 2,0 | 0,14 |
| Z-score < -3 | 0,73 | 0,85 | 0,79 | 11 | 4 | 2 | 0,21 | 2,0 | 0,14 |
| Z-score < -4 | 0,77 | 0,77 | 0,77 | 10 | 0 | 3 | 0,00 | 2,0 | 0,14 |
| Isolation Forest, contamination 0,02 | 0,50 | 0,46 | 0,48 | 6 | 3 | 7 | 0,27 | 21,0 | 3,92 |
| Isolation Forest, contamination 0,05 | 0,25 | 0,77 | 0,38 | 10 | 17 | 3 | 1,34 | 12,0 | 3,85 |
| Isolation Forest, contamination 0,10 | 0,17 | 0,85 | 0,29 | 11 | 35 | 2 | 2,57 | 5,0 | 3,86 |

Pre porovnanie: pravidlo s inými parametrami (mriežka v `detection-eval.md`) dosahuje F1 0,90 pri prahu 0,85 a 4 hodinách, F1 0,96 pri prahu 0,75 a 3 až 4 hodinách (0,05 falošných poplachov / 30 dní), pri recalle 1,00.

## Zhodnotenie

- **Isolation Forest je najhorší na všetkých osiach.** F1 najviac 0,48, oneskorenie 5 až 21 h (kým pravidlo 2 h), je ~25× pomalší na spracovanie a vyžaduje nastaviť `contamination`, teda dopredu odhadnúť podiel porúch, ktorý nepoznáme. Je to nesupervizovaná metóda bez pojmu „podvýroba“: označuje aj neobvyklé, ale zdravé hodiny. Vysvetliteľnosť technikovi je slabá (skóre izolácie namiesto „výroba je 62 % očakávania“).
- **Z-score je porovnateľný s pravidlom, nie lepší.** Pri `k = 3` F1 0,79 oproti 0,62 pri predvolenom pravidle, ale pravidlo po ladení parametrov dosiahne 0,96. Z-score má navyše štrukturálnu slabinu: baseline je medián vlastnej histórie inštalácie, takže dlhá alebo trvalá porucha posunie medián a metóda ju prestane vidieť (FN pri `k >= 3` sú pravdepodobne takéto prípady; príčinu som v tomto spiku nerozložil na jednotlivé scenáre). Pravidlo porovnáva s nezávislým očakávaním z modelu, takže tento problém nemá.
- **Zložitosť a vysvetliteľnosť:** pravidlo má 4 čitateľné parametre (prah, trvanie, GHI, pokrytie), výstup je „r, trvanie, závažnosť“ priamo pre tiket. Z-score pridáva len jeden parameter, ale jeho význam závisí od rozptylu konkrétnej inštalácie. Isolation Forest je čierna skrinka.

## Rozhodnutie

1. **Ostať pri pravidle nad rezíduom `r` (ADR #194) ako primárnej detekcii.** Je najpresnejšie po naladení, najrýchlejšie, deterministické a vysvetliteľné.
2. **Isolation Forest nepoužiť** na detekciu podvýroby. Mohol by mať zmysel len pre iný problém (neznáme typy porúch bez `P_expected`), ktorý teraz nemáme.
3. **Z-score nezavádzať ako samostatnú metódu.** Ak by sa pravidlo ukázalo príliš citlivé na rozptyl rôznych inštalácií, doplniť ho ako adaptívny prah (prah z reziduálneho rozptylu inštalácie), nie ako náhradu.
4. **Upozornenie k ADR #194:** zvolená dvojica (0,85; 2 h) dáva F1 0,62, kým najlepšia bunka mriežky 0,96. ADR #194 pritom pripúšťa zvolenú dvojicu len pri rozdiele F1 <= 0,03 a ≤ 1 falošnom poplachu / 30 dní (0,86 splnené, F1 nie). Kritérium z ADR #194 teda **nie je splnené** a tím by mal prah a trvanie prehodnotiť (napr. 0,80 a 3 h dáva F1 0,90). Konfiguráciu v tomto PR nemením.

## Obmedzenia a neoverené

- **Simulované poruchy sú optimistická horná hranica** (ADR #187); výsledky treba potvrdiť na reálnych poruchách (DKASC).
- **Jedna sada scenárov (seed 42), 13 labelov porúch.** Rozdiely o 1 až 2 udalosti sú v rámci šumu; neurobil som opakovanie cez viac seedov ani intervaly spoľahlivosti.
- **Parametre z-score a `contamination` boli vyberané na tej istej sade**, na ktorej sa hodnotia, takže ich čísla sú optimistické. Pravidlo má v tabuľke predvolené parametre, v poznámke ladené; porovnanie metód nie je úplne symetrické.
- Príznaky Isolation Forest som neladil (len `[r, actual/rated, hodina]`); iná sada príznakov (napr. rolling štatistiky, GHI, oblačnosť) môže výsledok zlepšiť. Neskúšané: CUSUM/EWMA, seasonal-decomposition, autoenkodér.
- Čas v tabuľke je celkový čas na 40 scenárov na jednom stroji (macOS), len orientačný.

## Nadväzujúce kroky

- Prehodnotiť prah a trvanie v ADR #194 (viď bod 4) vrátane experimentu na viacerých seedoch.
- Opakovať porovnanie na reálnych poruchách DKASC.
