# ADR spike #193: Metriky pre predikciu výroby a detekciu anomálií

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15 (revízia po rozhodnutí tímu o datasete DKA)
- **Issue:** #193 (nadradené #7)
- **Súvisí:** #178 (dataset DKA), #180 (počasie), #183 (kvalita dát), #189 (hodinová os), #191 (model), #194 (definícia podvýroby), #187 (labely)

## Kontext

MAPE je pri nulovej (nočnej) výrobe nedefinované/nestabilné. Pri detekcii anomálií stojí falošný poplach výjazd technika. Metriky musia byť jednotné pre baseline, LightGBM aj pravidlo podvýroby, aby sa dali porovnávať experimenty.

Produkčný dataset je **DKA Solar Centre, Alice Springs, 2023 až 2024** (#178): 5-minútové záznamy ~49 polí (každé s metadátami výkonu, sklonu, azimutu), takže hodnotenie je **po poliach** a labely udalostí sú čiastočne **reálne** (M19 C nevyrába od 2024-02-05; M15 C Phase II takmer nevyrába január až apríl 2024), čiastočne syntetické (ADR #187).

## Zvažované možnosti

| Oblasť | Možnosť | Posúdenie |
|---|---|---|
| Regresia | MAPE | zamietnuté: delenie nulou v noci a nestabilné pri nízkej výrobe |
| | **MAE, RMSE, nMAE** (denné hodiny) | MAE v jednotkách výkonu, RMSE penalizuje veľké chyby, nMAE porovnateľné medzi systémami |
| | wMAPE = Σ\|e\| / Σ\|y\| | doplnková, stabilná, ale závislá od výroby (v zime vyššia) |
| Detekcia | na úrovni bodu (hodiny) | citlivé na hranice, nadhodnocuje dlhé udalosti |
| | **na úrovni udalosti** (primárne) + bod (doplnkovo) | zodpovedá tomu, čo vidí technik: jeden tiket = jedna udalosť |
| Súhrn | accuracy | zamietnuté: trieda je silno nevyvážená (3 až 8 % porúch) |
| | **precision, recall, F1** (+ PR krivka) | rozhodnutie tímu |

## Rozhodnutie

### Regresia (predikcia výroby)

- **Populácia hodnotenia:** len denné hodiny (GHI > 50 W/m², konzistentne s ADR #194); chýbajúce a vyradené hodiny podľa ADR #183 sa nehodnotia (vrátane ~11 chýbajúcich dní 2023). Hodnotí sa **hodinová** granularita (ADR #189): 5-minútové záznamy DKASC sa agregujú na hodinový priemer výkonu, hodina platí pri >= 75 % vzoriek (>= 9 z 12).
- **Denné hodiny v Alice Springs:** lokálny čas je ACST (UTC+9:30, bez letného času). Pri GHI > 50 W/m² je to rádovo 9 až 10 hodín v zime a 12 až 13 v lete, teda ~4 000 až 4 500 hodnotených hodín za rok a pole. Slnečný deň v UTC prechádza cez polnoc (približne 20:00 až 10:00 UTC), preto sa agregácie "po dni" robia podľa **lokálneho** dňa (`Australia/Darwin`), nie podľa UTC dátumu. Hodina je označená koncom intervalu a priemer sa zarovná na Open-Meteo (priemer predchádzajúcej hodiny, ADR #194). Neoverené: časová zóna a konvencia časových pečiatok v súboroch DKASC; treba overiť pred prvým výpočtom (posun o hodinu systematicky zvýši chybu).
- **Hodnotenie po poliach:** metriky sa počítajú pre každé pole zvlášť a agregujú sa po poliach (medián a IQR, priemer ako doplnok), nie cez všetky riadky. Do regresných metrík idú len zdravé polia a len obdobia mimo reálnych porúch (M19 C po 2024-02-05, M15 C Phase II január až apríl 2024). Polia v areáli zdieľajú počasie a sú silno korelované, preto intervaly spoľahlivosti vznikajú blokovým bootstrapom po týždňoch (rovnaké týždne naprieč poľami), nie po riadkoch.
- **MAE** a **RMSE** v W/kWp (alebo kW pre pole), s `e = ŷ - y`.
- **Konvencia AC/DC pre nMAE:** `y` aj `ŷ` sú **rovnaká veličina**, a to výkon, ktorý meria merač poľa/striedača (u telemetrie SolarOps `acPowerW`, teda AC strana), a `ŷ` z fyzikálneho baseline (pvlib ModelChain) sa berie tiež ako **AC výstup** (vrátane orezania na AC limit striedača). Normalizácia je `P_rated = dc_capacity_kW` (inštalovaný DC výkon z metadát poľa), teda **nMAE = MAE(AC) / P_DC,rated** v %. Dôsledok: účinnosť striedača (~3 až 4 %) a orezanie sú v chybe aj v mierke; hodnoty nie sú priamo porovnateľné s literatúrou, ktorá normalizuje AC výkonom. Neoverené: či merač poľa v DKASC meria AC alebo DC stranu; ak DC, zvolí sa DC aj pre baseline (DC výstup pvlib) a zapíše sa to do `metrics.json`. Zmiešanie AC a DC medzi `y` a `ŷ` je zakázané. Doplnkovo **wMAPE** a skill score `1 - MAE_model / MAE_baseline`.
- **Report:** po systémoch a agregát (medián/priemer cez polia, nie len cez riadky), rozdelenie po mesiacoch/sezónach a po triede oblačnosti (kt < 0,3 / 0,3 až 0,7 / > 0,7), aby sa neskrývala slabosť v zamračených hodinách.
- **Cieľové prahy (návrh, kalibrovať po prvom experimente, neoverené):** nMAE <= 8 % pre baseline (len kontrola rádu), **nMAE <= 6 %** pre LightGBM na walk-forward foldoch × vynechaných poliach (ADR #191), skill score voči fyzikálnemu baseline >= 0,20 (ADR #191).

### Detekcia anomálií (podvýroby)

- **Udalosť (ground truth)** = súvislý interval labelu: (a) **reálne** poruchy DKASC, intervaly `fault_events` s `source = real_dkasc` (ADR #187; M19 C, M15 C Phase II, prípadné ďalšie nájdené porovnaním polí), (b) **syntetické** udalosti (`is_underperforming`, ADR #187). Reálne a syntetické výsledky sa reportujú **zvlášť**; hlavná výpoveď je z reálnych (held-out) porúch. Vyhlásená udalosť ("alert") = výstup pravidla ADR #194. Alert aj label majú `start`, `end`.
- **Reálnych udalostí je málo** (rádovo 2 až 4): reportujú sa absolútne počty (napr. 2 z 2 detegované), nie percentá s falošnou presnosťou.

**Párovanie alertov s labelmi** (každý alert dostane práve jednu triedu):

| Trieda alertu | Podmienka | Do precision | Do recall |
|---|---|---|---|
| **TP** | prvý alert prekrývajúci labelovanú udalosť, ktorého `start` je v intervale `<label.start - 2 h ; label.start + 24 h>` | TP | udalosť detegovaná |
| **DUP** (duplikát) | ďalší alert prekrývajúci tú istú labelovanú udalosť, ktorá už má TP (napr. udalosť sa uzavrela a znovu otvorila) | **FP** | bez vplyvu |
| **LATE** (neskoré prekrytie) | alert prekrýva label, ale `start > label.start + 24 h` a pre label nie je TP | **FP** v primárnej metrike | udalosť sa počíta ako **FN** v primárnej metrike |
| **FP** | alert bez prekrytia s labelom, alebo `start < label.start - 2 h` (predčasný) | FP | bez vplyvu |

- **Viac alertov na jednu udalosť sa penalizuje.** Každý alert vytvorí tiket (#194), a teda výjazd alebo prácu technika, preto DUP = FP. Pôvodné pravidlo "viac alertov = 1 TP, zvyšok sa nepenalizuje" by umožnilo detektoru, ktorý udalosť štiepi na desiatky tiketov, dosiahnuť perfektnú precision. Doplnkovo sa reportuje **alerts-per-event** = (TP + DUP + LATE) / počet detegovaných labelovaných udalostí; cieľ <= 1,5 (návrh, neoverené).
- **Neskoré prekrytie (LATE):** alert, ktorý začal viac ako 24 h po začiatku labelu, ale label stále trvá, nie je v primárnej metrike zásah (SLA 24 h nesplnené), takže neskoro detegovaná udalosť je FN a jej alert je FP. Aby sa neskrývala informácia, reportuje sa aj **relaxovaná metrika "overlap-only"**, kde LATE je TP (a udalosť detegovaná), vedľa primárnej. Rozdiel medzi nimi je mierou "pomalosti" detektora (typicky pomalý soiling).
- **Metriky:** precision, recall, F1 na úrovni udalostí (primárne; precision = TP / všetky alerty, recall = detegované udalosti / všetky labelované); doplnkovo na úrovni bodu (hodiny); **mediánové oneskorenie detekcie** (h, z TP aj LATE); **alerts-per-event**; **falošné poplachy (FP + DUP) na pole za 30 dní**, osobitne aj len pre DUP.
- **Cena falošného poplachu:** precision je prioritná. Primárny F1 sa dopĺňa PR krivkou a hodnotou F0,5 pre výber prahu; prahy sa ladia na validácii, test sa neotvára.
- **Cieľové prahy akceptácie (návrh):** precision >= 0,80, recall >= 0,70, F1 >= 0,75, <= 1 falošný poplach (FP + DUP) na pole za 30 dní, mediánové oneskorenie <= 24 h; pre ťažké typy (soiling pod 20 % straty) sa recall hodnotí zvlášť (po typoch, ADR #187).
- **Falošné poplachy na zdravých poliach DKASC:** ~45 zdravých polí × 24 mesiacov dáva rádovo tisíc pole-mesiacov na odhad FP rate, čo je oveľa viac než počet reálnych porúch. Pozor: alert na "zdravom" poli nemusí byť FP; v 49 poliach môžu byť nezdokumentované poruchy. Preto sa každý alert na poli bez labelu najprv posúdi (porovnanie s ostatnými poľami v rovnakej hodine, vizuálna kontrola) a až potom sa zapíše ako FP alebo ako nový label (so záznamom, že label vznikol z dát, nie z inšpekcie; kruhová pravda, ADR #187). Počty FP sú korelované medzi poľami (spoločné počasie), nie nezávislé.
- **Delenie:** podľa času aj polí (walk-forward, leave-one-array-out, ADR #191). Hodnotenie na syntetických poruchách je horná hranica; hlavné hodnotenie je na reálnych poruchách DKASC a na neporuchových poliach.

### Implementácia a reportovanie

- Metriky cez `scikit-learn` (`mean_absolute_error`, `root_mean_squared_error`, `precision_recall_fscore_support`), udalostné párovanie vlastnou funkciou s unit testami.
- Každý beh vytvorí `metrics.json` (verzia dát, hash configu, seed, hodnoty podľa systému) a Markdown tabuľku do reportu/PR; porovnanie baseline vs. LightGBM vždy na rovnakom splite.

## Dôsledky

- Jednotná, nulou-odolná definícia; porovnateľné výsledky medzi experimentmi.
- Udalostné hodnotenie vyžaduje jasné labely udalostí (rieši ADR #187).
- Prahy sú návrh, ich zmena sa musí zapísať s odôvodnením.

## Riziká / neoverené

- Neoverené: všetky cieľové hodnoty (6 %, 0,80, 0,70, ...) sú návrhy, neboli porovnané s meraním ani s konkrétnym zdrojom; literatúra o nMAE pri hodinovej predpovedi FVE nebola v tomto spiku vyhľadaná. Cieľové hodnoty pochádzajú z úvah pre iné podmienky; na DKASC (vysoké GHI, malá oblačnosť) bude nMAE pravdepodobne nižšie než na Slovensku, takže 6 % z DKASC sa neprenáša automaticky.
- Tolerancia 24 h je kompromis; pre rýchle poruchy by mala byť kratšia (prípadne 2 h), spresniť v #194. `inverter_fault` (status `FAULT`) nie je udalosť podvýroby (ADR #187/#194 pravidlo 3) a vyhodnocuje sa samostatne, mimo týchto metrík.
- Veľmi málo reálnych udalostí (rádovo 2 až 4): udalostné precision/recall na reálnych poruchách majú extrémne široké intervaly.
- Neoverené: časová zóna/konvencia pečiatok DKASC a to, či merač poľa meria AC alebo DC (pozri nMAE).
- Názvy funkcií sklearn (`root_mean_squared_error`) over podľa nainštalovanej verzie (scikit-learn 1.9.1 na PyPI).

## Zdroje

- scikit-learn metriky: https://scikit-learn.org/stable/modules/model_evaluation.html
- scikit-learn `precision_recall_fscore_support`: https://scikit-learn.org/stable/modules/generated/sklearn.metrics.precision_recall_fscore_support.html

## Nadväzujúce issues

1. `solarops_ml.metrics`: regresné a udalostné metriky + unit testy (nulové hodiny, prekryvy, tolerancia).
2. Párovanie alertov s labelmi (TP / DUP / LATE / FP) s unit testami: duplikáty, neskoré prekrytie, predčasný alert, udalosti cez polnoc UTC.
3. Generátor reportu (`metrics.json` + Markdown) a CI krok pre ml.
4. Spresnenie tolerancií podľa typu poruchy po experimentoch (#194).
