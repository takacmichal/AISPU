# ADR spike #193: Metriky pre predikciu výroby a detekciu anomálií

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #193 (nadradené #7)
- **Súvisí:** #191 (model), #194 (definícia podvýroby), #187 (labely)

## Kontext

MAPE je pri nulovej (nočnej) výrobe nedefinované/nestabilné. Pri detekcii anomálií stojí falošný poplach výjazd technika. Metriky musia byť jednotné pre baseline, LightGBM aj pravidlo podvýroby, aby sa dali porovnávať experimenty.

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

- **Populácia hodnotenia:** len denné hodiny (GHI > 50 W/m², konzistentne s ADR #194); chýbajúce a vyradené hodiny podľa ADR #183 sa nehodnotia. Hodnotí sa **hodinová** granularita (ADR #189).
- **MAE** a **RMSE** v W/kWp (alebo kW pre systém), s `e = ŷ - y`.
- **nMAE = MAE / P_rated** v %, kde `P_rated = dc_capacity_kW` (normalizácia inštalovaným výkonom, bežná pri predpovedi FVE; rozhodnutie tímu používa názov nMAE). Doplnkovo **wMAPE** a skill score `1 - MAE_model / MAE_baseline`.
- **Report:** po systémoch a agregát (priemer cez systémy, nie len cez riadky), rozdelenie po mesiacoch/sezónach a po triede oblačnosti (kt < 0,3 / 0,3 až 0,7 / > 0,7), aby sa neskrývala slabosť v zamračených hodinách.
- **Cieľové prahy (návrh, kalibrovať po prvom experimente, neoverené):** nMAE <= 8 % pre baseline (len kontrola rádu), **nMAE <= 6 %** pre LightGBM na testovacom období, skill score voči fyzikálnemu baseline >= 0,20 (ADR #191).

### Detekcia anomálií (podvýroby)

- **Udalosť** = súvislý interval vyhlásený pravidlom (ADR #194) alebo labelom (`is_underperforming`, ADR #187).
- **TP:** vyhlásená udalosť sa časovo prekrýva s labelovanou udalosťou a jej začiatok je najviac **24 h po** začiatku labelovanej udalosti (tolerancia oneskorenia; alert pred začiatkom o <= 2 h sa tiež uzná).
- **FP:** vyhlásená udalosť bez prekrytia s labelovanou. **FN:** labelovaná udalosť bez TP. Viac alertov na jednu labelovanú udalosť sa počíta ako 1 TP (zvyšok sa nepenalizuje, ale reportuje sa počet).
- **Metriky:** precision, recall, F1 na úrovni udalostí (primárne); doplnkovo na úrovni bodu (hodiny); **mediánové oneskorenie detekcie** (h); **falošné poplachy na inštaláciu za 30 dní**.
- **Cena falošného poplachu:** precision je prioritná. Primárny F1 sa dopĺňa PR krivkou a hodnotou F0,5 pre výber prahu; prahy sa ladia na validácii, test sa neotvára.
- **Cieľové prahy akceptácie (návrh):** precision >= 0,80, recall >= 0,70, F1 >= 0,75, <= 1 falošný poplach na inštaláciu za 30 dní, mediánové oneskorenie <= 24 h; pre ťažké typy (soiling pod 20 % straty) sa recall hodnotí zvlášť.
- **Delenie:** podľa času aj inštalácií (ADR #187). Hodnotenie na syntetických poruchách je horná hranica; dopĺňa sa kontrola falošných poplachov na reálnych neporuchových dátach PVDAQ.

### Implementácia a reportovanie

- Metriky cez `scikit-learn` (`mean_absolute_error`, `root_mean_squared_error`, `precision_recall_fscore_support`), udalostné párovanie vlastnou funkciou s unit testami.
- Každý beh vytvorí `metrics.json` (verzia dát, hash configu, seed, hodnoty podľa systému) a Markdown tabuľku do reportu/PR; porovnanie baseline vs. LightGBM vždy na rovnakom splite.

## Dôsledky

- Jednotná, nulou-odolná definícia; porovnateľné výsledky medzi experimentmi.
- Udalostné hodnotenie vyžaduje jasné labely udalostí (rieši ADR #187).
- Prahy sú návrh, ich zmena sa musí zapísať s odôvodnením.

## Riziká / neoverené

- Neoverené: všetky cieľové hodnoty (6 %, 0,80, 0,70, ...) sú návrhy, neboli porovnané s meraním ani s konkrétnym zdrojom; literatúra o nMAE pri hodinovej predpovedi FVE nebola v tomto spiku vyhľadaná.
- Tolerancia 24 h je kompromis; pre rýchle poruchy (inverter_fault) by mala byť kratšia (prípadne 2 h), spresniť v #194.
- Názvy funkcií sklearn (`root_mean_squared_error`) over podľa nainštalovanej verzie (scikit-learn 1.9.1 na PyPI).

## Zdroje

- scikit-learn metriky: https://scikit-learn.org/stable/modules/model_evaluation.html
- scikit-learn `precision_recall_fscore_support`: https://scikit-learn.org/stable/modules/generated/sklearn.metrics.precision_recall_fscore_support.html

## Nadväzujúce issues

1. `solarops_ml.metrics`: regresné a udalostné metriky + unit testy (nulové hodiny, prekryvy, tolerancia).
2. Generátor reportu (`metrics.json` + Markdown) a CI krok pre ml.
3. Spresnenie tolerancií podľa typu poruchy po experimentoch (#194).
