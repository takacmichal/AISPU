# ADR spike #191: Baseline vs. gradient boosting vs. LSTM

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #191 (nadradené #6)
- **Súvisí:** #189 (join), #193 (metriky), #194 (podvýroba), #196 (REST ML služba)

## Kontext

Model predikuje hodinovú výrobu (W/kWp) z počasia; jeho výstup `P_expected` je základ detekcie podvýroby (#194). ML beží ako FastAPI služba (Python 3.12) v Azure Container Apps (#196, #211), takže záleží na veľkosti image, štarte a cene CPU inferencie. Dáta: PVDAQ (>= 2 roky na systém) + Open-Meteo, hodinová granularita (#189). **Experimenty (notebook) v tomto spiku neboli spustené**; ADR stanovuje dizajn experimentu a rozhodnutie vychádza z vlastností modelov a podmienok projektu. Odporúčanie sa potvrdí akceptačnou bránou nižšie.

## Zvažované možnosti

| Model | Presnosť (očakávanie) | Zložitosť | Nasadenie | Posúdenie |
|---|---|---|---|---|
| Persistence (výkon pred 24 h) | nízka, ignoruje počasie | triviálna | triviálna | len kontrolný dolný limit |
| **Baseline fyzikálny:** `pvlib` ModelChain (PVWatts) s Open-Meteo GHI/DNI/DHI + teplota | stredná, bez trénovania, vysvetliteľný | nízka | knižnica pvlib 0.16.1 | povinný baseline a základ features |
| Lineárna regresia | nízka-stredná, nelineárne vzťahy (teplota, clipping) | nízka | triviálna | referencia |
| **LightGBM** (4.7.0, Python >= 3.10) | vysoká na tabuľkových dátach, dobre zvláda nelineárne a interakcie | stredná | model súbor (rádovo kB až MB), CPU inferencia, bez GPU | **vybrané** |
| LSTM (PyTorch 2.14.1) | porovnateľná alebo o niečo lepšia pri dlhých sekvenciách a veľa dátach; pri hodinových dátach s vonkajším počasím malý zisk (neoverené) | vysoká (okná, škálovanie, ladenie) | veľká závislosť (torch), pomalšia inferencia, viac pamäte | odložené |

## Rozhodnutie

**Baseline = fyzikálny model `pvlib` (clear-sky/Open-Meteo × počasie); MVP model = LightGBM; LSTM odložené.** Zhodné s rozhodnutím tímu (LightGBM po baseline, LSTM mimo Sprint 1 až 2).

Parametre experimentu a produkčného modelu:

- **Cieľ:** `y = P_ac_hourly / dc_capacity_kW` (W/kWp), len hodiny so Slnkom nad horizontom a GHI > 50 W/m² (denné hodiny); vzorky podľa ADR #183.
- **Features (bez únikov, len to, čo je známe v čase predpovede):** `ghi`, `dni`, `dhi`, `cloud_cover`, `temperature_2m`, `wind_speed_10m` (m/s), `clearsky_ghi` (pvlib Ineichen), `kt = ghi/clearsky_ghi`, `solar_zenith`, `solar_azimuth`, `sin/cos(hour)`, `sin/cos(day_of_year)`, `tilt`, `azimuth_panel`, `p_baseline` (výstup fyzikálneho modelu), `weather_source`. Lagy výkonu (napr. t-24 h) len pre variantu "nowcast", nie v MVP.
- **Split po čase (bez leakage):** per systém chronologicky 70 % train / 15 % validácia / 15 % test; medzi úsekmi **7-dňová medzera** (purge). Test aspoň 6 mesiacov, aby obsahoval sezónu. Okrem toho walk-forward CV s 3 expandujúcimi foldmi. Pre zovšeobecnenie aspoň 1 systém úplne vynechaný z trénovania (leave-one-system-out).
- **LightGBM (štart):** `objective="regression"`, `learning_rate=0.03`, `n_estimators=2000` s `early_stopping(100)` na validácii, `num_leaves=31`, `min_child_samples=100`, `feature_fraction=0.8`, `bagging_fraction=0.8`, `bagging_freq=1`, `seed=42`. Ladenie len na validácii (napr. Optuna 50 pokusov), test sa otvorí raz.
- **LSTM varianta pre porovnanie (ak sa spustí):** 1 vrstva 64 jednotiek, vstupné okno 24 h, rovnaké features a split, early stopping; porovnáva sa čas tréningu, inferencie a veľkosť.
- **Metriky:** podľa ADR #193 (MAE, RMSE, nMAE; wMAPE doplnkovo), plus čas tréningu a inferencie na 1 000 riadkov a veľkosť modelu.

**Akceptačná brána:** LightGBM je prijatý, ak na teste (denné hodiny) zníži nMAE aspoň o 20 % relatívne oproti fyzikálnemu baseline a splní cieľ z ADR #193. Ak nie, ostáva fyzikálny baseline a rieši sa kvalita dát/features. **LSTM sa vráti na stôl**, keď (a) bude >= 12 mesiacov reálnej telemetrie z SK, a (b) LightGBM nesplní cieľ po zlepšení features.

## Dôsledky

- Malý image ML služby (lightgbm, pvlib, pandas bez torch), rýchly štart v Container Apps, inferencia na CPU v milisekundách (neoverené meranie).
- Vysvetliteľnosť: dôležitosť features, porovnanie s fyzikálnym `P_baseline` (užitočné aj pre technikov).
- Model sa trénuje na PVDAQ (USA); pre SK treba doladenie/validáciu (#178, #187).
- `P_baseline` ako feature robí LightGBM v podstate korekciou fyzikálneho modelu, čo znižuje nároky na dáta.

## Riziká / neoverené

- Neoverené: skutočné metriky, časy a veľkosti modelov (experimenty neprebehli); tvrdenie, že LSTM prináša malý zisk, je očakávanie, nie meranie.
- Verzie overené na PyPI k 2026-10-15: lightgbm 4.7.0, pvlib 0.16.1, scikit-learn 1.9.1, torch 2.14.1.
- Mix zdrojov počasia (ERA5 vs. ICON) môže skresliť model (príznak `weather_source`, #180).
- Domain gap USA -> SK a syntetika -> realita.

## Zdroje

- LightGBM: https://lightgbm.readthedocs.io/ , PyPI https://pypi.org/project/lightgbm/
- pvlib ModelChain: https://pvlib-python.readthedocs.io/en/stable/user_guide/modelchain.html
- scikit-learn TimeSeriesSplit (gap): https://scikit-learn.org/stable/modules/generated/sklearn.model_selection.TimeSeriesSplit.html
- PyTorch LSTM: https://pytorch.org/docs/stable/generated/torch.nn.LSTM.html

## Nadväzujúce issues

1. Notebook/skript experimentu: baseline vs. LightGBM (vs. voliteľne LSTM) na 2 až 3 PVDAQ systémoch s časovým splitom.
2. `solarops_ml`: modul features + tréning LightGBM + uloženie modelu (verzia, hash dát).
3. Endpoint predikcie vo FastAPI službe (#196) a monitoring driftu.
