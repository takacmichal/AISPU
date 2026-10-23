# ADR spike #191: Baseline vs. gradient boosting vs. LSTM

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15 (revízia po rozhodnutí tímu o datasete DKA)
- **Issue:** Refs #191 (nadradené #6); issue sa týmto ADR nezatvára, kým neexistujú výsledky experimentu
- **Súvisí:** #178 (dataset DKA), #180 (počasie Open-Meteo), #189 (join), #193 (metriky), #194 (podvýroba), #196 (REST ML služba)

## Kontext

Model predikuje hodinovú výrobu (W/kWp) z počasia; jeho výstup `P_expected` je základ detekcie podvýroby (#194). ML beží ako FastAPI služba (Python 3.12) v Azure Container Apps (#196, #211), takže záleží na veľkosti image, štarte a cene CPU inferencie. Dáta: **DKA Solar Centre, Alice Springs, roky 2023 až 2024** (5-minútové záznamy ~49 polí, agregované na hodiny, #178) + počasie z Open-Meteo: **Historical Forecast** na tréning a **Forecast** v prevádzke (#180); hodinová granularita (#189). Dataset má len **2 roky** (2023 s ~11 chýbajúcimi dňami), preto je každý návrh delenia nižšie podriadený tomuto obmedzeniu. **Experimenty (notebook) v tomto spiku neboli spustené**; ADR stanovuje dizajn experimentu a rozhodnutie vychádza z vlastností modelov a podmienok projektu. Odporúčanie sa potvrdí akceptačnou bránou nižšie.

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

- **Cieľ:** `y = P_hourly / dc_capacity_kW` (W/kWp) pre každé pole zvlášť (metadáta poľa: výkon, technológia, sklon, azimut), len hodiny so Slnkom nad horizontom a GHI > 50 W/m² (denné hodiny); vzorky podľa ADR #183. Konvencia AC/DC meranej veličiny sa musí zhodovať s ADR #193 (nMAE).
- **Vstupné polia:** len zdravé polia. Merače bez dát, prázdny stĺpec vetra a obdobia reálnych porúch (M19 C od 2024-02-05, M15 C Phase II január až apríl 2024) sa **nepoužijú v tréningu**; slúžia na hodnotenie detekcie (ADR #187, #193). Chýbajúce dni 2023 sú neplatné hodiny, nie nulová výroba.
- **Features (bez únikov, len to, čo je známe v čase predpovede):** `ghi`, `dni`, `dhi`, `cloud_cover`, `temperature_2m`, `wind_speed_10m` (m/s; z Open-Meteo, nie z prázdneho stĺpca DKASC), `clearsky_ghi` (pvlib Ineichen), `kt = ghi/clearsky_ghi`, `solar_zenith`, `solar_azimuth`, `sin/cos(hour)`, `sin/cos(day_of_year)`, `tilt`, `azimuth_panel`, `p_baseline` (výstup fyzikálneho modelu). **`weather_source` nie je feature modelu** (pozri skew nižšie). Lagy výkonu (napr. t-24 h) len pre variantu "nowcast", nie v MVP.
- **Delenie (2 roky dát, bez leakage):** pôvodný návrh "test aspoň 6 mesiacov na samostatnom úseku" sa s 2 rokmi nedá splniť spolu s tréningom cez celú sezónu (po odobratí validácie a testu by tréning pokryl < 12 mesiacov a chýbala by sezóna). Namiesto jedného holdoutu sa použije:
  1. **Walk-forward (expanding window) s medzerou 7 dní:** fold A = tréning 2023-01 až 2023-12, test 2024-01 až 2024-06; fold B = tréning 2023-01 až 2024-06, test 2024-07 až 2024-12. Každý test okno má ~6 mesiacov, ale nie je to jeden pevný test, teda výsledok je priemer 2 foldov (a nie nezávislý holdout). Validácia na early stopping: posledných ~15 % tréningového obdobia (s 7-dňovou medzerou), nie test.
  2. **Leave-one-array-out (grouped CV):** polia sa delia do K skupín (napr. K = 5, podľa sklonu/azimutu/technológie, aby skupina nebola len susedné identické polia); model sa trénuje bez skupiny a hodnotí sa na vynechaných poliach v rovnakom časovom okne. Meria zovšeobecnenie na nové pole (iné metadáta), nie na nové počasie.
  3. **Kombinácia:** hlavné číslo = walk-forward fold × vynechané polia (čas aj pole nevidené). Reportujú sa všetky tri pohľady zvlášť.
  - **Obmedzenia:** polia sú v tom istom areáli s rovnakým počasím, takže LOAO nehodnotí zovšeobecnenie na iné počasie ani lokalitu, a hodnoty polí sú silne korelované (efektívna veľkosť vzorky je výrazne menšia ako 49 polí). Sezónnosť: každý fold vidí v tréningu najviac 1 až 1,5 roka, takže extrémne roky sa nevyhodnotia. Konečný model sa natrénuje na všetkých zdravých poliach a oboch rokoch a **nemá nezávislý holdout**; jeho kvalita je odhad z CV.
  - Hodnotenie na reálnych poruchách je samostatné (ADR #187, #193) a používa pole s poruchou len v teste.
- **Train/serve skew počasia:** tréning používa Open-Meteo **Historical Forecast**, prevádzka **Forecast** (#180). Obe majú rovnaké modely, premenné a formát, ale Historical Forecast je archív zreťazených predpovedí (zmeny verzie modelu v čase môžu spôsobiť nespojitosti, neoverené) a nemusí byť bit-po-bite zhodný s tým, čo Forecast vráti v prevádzke. Opatrenia: (a) `weather_source` sa **nepoužije ako feature**, lebo v tréningu by bol konštantný a v prevádzke by mal nevidenú hodnotu; ukladá sa len ako metadáta predikcie a do monitoringu; (b) citlivostná kontrola: porovnať model s meteostaničným GHI z DKASC (merané, horná hranica kvality) a s Open-Meteo vstupmi (produkčný scenár), čím sa vyčíslí strata kvôli počasiu; (c) po spustení porovnať rozdelenie `ghi`/`kt` z Forecast API (`past_days`) s tréningovým a sledovať drift (#196). Pozn.: pre `P_expected` v detekcii podvýroby ADR #194 počíta s analyzovaným/nameraným počasím danej hodiny, takže skew sa týka najmä predikcie dopredu.
- **Prenos na Slovensko:** model sa učí na púštnom podnebí Alice Springs (vysoké GHI, málo oblačnosti, žiadny sneh). Fyzikálny `p_baseline` a geometria (sklon, azimut) sa prenášajú, ML korekcia nemusí. Pred slovenskou produkciou treba validáciu na SK telemetrii (simulátor #187/#94 nie je dôkaz).
- **LightGBM (štart):** `objective="regression"`, `learning_rate=0.03`, `n_estimators=2000` s `early_stopping(100)` na validácii, `num_leaves=31`, `min_child_samples=100`, `feature_fraction=0.8`, `bagging_fraction=0.8`, `bagging_freq=1`, `seed=42`. Ladenie len na validácii (napr. Optuna 50 pokusov), test sa otvorí raz.
- **LSTM varianta pre porovnanie (ak sa spustí):** 1 vrstva 64 jednotiek, vstupné okno 24 h, rovnaké features a split, early stopping; porovnáva sa čas tréningu, inferencie a veľkosť.
- **Metriky:** podľa ADR #193 (MAE, RMSE, nMAE; wMAPE doplnkovo), plus čas tréningu a inferencie na 1 000 riadkov a veľkosť modelu.

**Akceptačná brána:** LightGBM je prijatý, ak na walk-forward foldoch × vynechaných poliach (denné hodiny, zdravé polia) zníži nMAE aspoň o 20 % relatívne oproti fyzikálnemu baseline a splní cieľ z ADR #193. Ak nie, ostáva fyzikálny baseline a rieši sa kvalita dát/features. **LSTM sa vráti na stôl**, keď (a) bude >= 12 mesiacov reálnej telemetrie z SK (DKASC má len 2 roky, čo na LSTM pri hodinových dátach nestačí na spoľahlivé porovnanie), a (b) LightGBM nesplní cieľ po zlepšení features.

## Dôsledky

- Malý image ML služby (lightgbm, pvlib, pandas bez torch), rýchly štart v Container Apps, inferencia na CPU v milisekundách (neoverené meranie).
- Vysvetliteľnosť: dôležitosť features, porovnanie s fyzikálnym `P_baseline` (užitočné aj pre technikov).
- Model sa trénuje na DKASC (Austrália, 2 roky); pre SK treba doladenie/validáciu (#178, #187). Reálne poruchy DKASC slúžia na hodnotenie detekcie, nie na tréning.
- `P_baseline` ako feature robí LightGBM v podstate korekciou fyzikálneho modelu, čo znižuje nároky na dáta.

## Riziká / neoverené

- Neoverené: skutočné metriky, časy a veľkosti modelov (experimenty neprebehli); tvrdenie, že LSTM prináša malý zisk, je očakávanie, nie meranie.
- Verzie overené na PyPI k 2026-10-15: lightgbm 4.7.0, pvlib 0.16.1, scikit-learn 1.9.1, torch 2.14.1.
- Train/serve skew počasia (Historical Forecast vs. Forecast, #180), pozri vyššie; `weather_source` je len metadáta.
- Len 2 roky dát: sezónnosť a odľahlé roky nie sú pokryté; chýbajúce dni 2023 znižujú vzorku.
- Polia DKASC sú korelované (jeden areál, jedno počasie); LOAO nadhodnocuje nezávislosť.
- Domain gap Alice Springs -> SK a syntetika -> realita.

## Zdroje

- LightGBM: https://lightgbm.readthedocs.io/ , PyPI https://pypi.org/project/lightgbm/
- pvlib ModelChain: https://pvlib-python.readthedocs.io/en/stable/user_guide/modelchain.html
- scikit-learn TimeSeriesSplit (gap): https://scikit-learn.org/stable/modules/generated/sklearn.model_selection.TimeSeriesSplit.html
- PyTorch LSTM: https://pytorch.org/docs/stable/generated/torch.nn.LSTM.html

## Nadväzujúce issues

1. Notebook/skript experimentu: baseline vs. LightGBM (vs. voliteľne LSTM) na poliach DKASC s walk-forward a leave-one-array-out delením. Až jeho výsledky umožnia zatvoriť #191.
2. `solarops_ml`: modul features + tréning LightGBM + uloženie modelu (verzia, hash dát).
3. Endpoint predikcie vo FastAPI službe (#196) a monitoring driftu.
