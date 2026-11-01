# ADR spike #191: Baseline vs. gradient boosting vs. LSTM

- **Stav:** Navrhnuté (experiment vykonaný na syntetických dátach; čaká rerun na DKASC a SK telemetrii)
- **Dátum:** 2026-11-01 (revízia po experimente; pôvodné rozhodnutie 2026-10-15)
- **Issue:** Refs #191 (nadradené #6); issue sa zatvára až po rerune na reálnych dátach
- **Súvisí:** #178 (dataset DKA), #180 (počasie Open-Meteo), #189 (join), #193 (metriky), #194 (podvýroba), #196 (REST ML služba)

## Kontext

Model predikuje hodinovú výrobu (W/kWp) z počasia; jeho výstup `P_expected` je základ detekcie podvýroby (#194). ML beží ako FastAPI služba (Python 3.12) v Azure Container Apps (#196, #211), takže záleží na veľkosti image, štarte a cene CPU inferencie. Dáta: **DKA Solar Centre, Alice Springs, roky 2023 až 2024** (5-minútové záznamy ~49 polí, agregované na hodiny, #178) + počasie z Open-Meteo: **Historical Forecast** na tréning a **Forecast** v prevádzke (#180); hodinová granularita (#189). Dataset má len **2 roky** (2023 s ~11 chýbajúcimi dňami), preto je každý návrh delenia nižšie podriadený tomuto obmedzeniu.

**Stav experimentu:** Reálne DKASC dáta nie sú v repe (licencia a chýbajúce stiahnutie, ADR #183). Experiment preto bežal na **deterministických syntetických dátach zo simulátora #94** – 5 polí, rovnaké počasie, 2023–2024, hodinová granularita. Je to metodický dry-run a overenie pipeline, **nie dôkaz o produkčnej presnosti**. Výsledky sú v [`assets/spike-191-results.json`](assets/spike-191-results.json) a [`assets/spike-191-results.md`](assets/spike-191-results.md); notebook je `ml/notebooks/191-model-comparison.ipynb`, reprodukovateľný kód `solarops_ml.forecast`.

## Zvažované možnosti

| Model | Presnosť (očakávanie) | Zložitosť | Nasadenie | Posúdenie |
|---|---|---|---|---|
| Persistence (výkon pred 24 h) | nízka, ignoruje počasie | triviálna | triviálna | len kontrolný dolný limit |
| **Baseline fyzikálny:** clear-sky/GTI model s počasím a metadátami poľa | stredná, bez trénovania, vysvetliteľný | nízka | numpy/pandas, žiadny model | **vybrané pre MVP** |
| Lineárna regresia | nízka-stredná, nevie nelinearity (teplota, clipping) | nízka | triviálna | referencia |
| **LightGBM** (4.7.0) | vysoká na tabuľkových dátach, zvláda nelinearity a interakcie | stredná | súbor modelu (rádovo MB), CPU inferencia, bez GPU | challenger, čaká na reálne dáta |
| LSTM (PyTorch 2.15.0) | porovnateľná; na syntetických dátach horšia ako baseline aj GBM | vysoká (okná, škálovanie, ladenie, torch) | veľká závislosť (torch), pomalší tréning, malý model | odložené, nie zamietnuté |

## Experiment

### Dáta a features

- 5 simulovaných južne orientovaných polí: sklon 15/20/25/30/35°, 6–15 kWp, DC/AC 1,15–1,30. Všetky polia zdieľajú rovnaký seed počasia, takže leave-one-array-out meria zovšeobecnenie na **inú geometriu a veľkosť poľa**, nie na iné počasie.
- Cieľ `y = ac_power_w / (capacity_kwp * 1000)`; hodnotenie len v denných hodinách (`gti > 50 W/m²`), s dostupným cieľom a 24 h lagom pre perzistenciu.
- Features: `gti_wm2`, `cloud_cover_pct`, `air_temp_c`, `clearsky_gti`, `kt`, `solar_zenith`, `solar_azimuth`, `sin/cos(hour)`, `sin/cos(day_of_year)`, `tilt`, `azimuth`, `capacity_kwp`, `dc_ac_ratio`.
- **`temperature_c` z telemetrie nie je feature.** Simulátor ho počíta ako `air_temp + 25 * ac_power / rated_ac`, takže obsahuje časť cieľa (leakage). Používa sa len pôvodná, na výkone nezávislá `air_temp_c`; fyzikálny baseline počíta teplotu bunky cez NOCT model z `air_temp_c` a `gti_wm2`.
- **`p_baseline` nie je vstupom ML modelov.** Fyzikálny baseline je samostatný kandidát; jeho pridanie ako feature by z neho spravilo stacking a znevýhodnilo porovnanie rodín modelov. Stacking je follow-up.
- Chýbajúce hodiny sa nezamaskujú nulou; hodiny s menej ako 9 z 12 päťminútových vzoriek sú `NaN` (ADR #183).

### Delenie bez leakage

1. **Walk-forward (expanding window) s 7-dňovou medzerou:**
   - `2024H1`: tréning 2023-01-01 až 2023-12-25, test 2024-01-01 až 2024-07-01.
   - `2024H2`: tréning 2023-01-01 až 2024-06-24, test 2024-07-01 až 2025-01-01.
2. **Leave-one-array-out (LOAO):** v každom folde sa natrénuje na 4 poliach a vyhodnotí na vynechanom poli. Reportujú sa všetky polia.
3. Validácia pre early stopping je posledných 15 % tréningu s 7-dňovou medzerou, nikdy test.
4. Metriky sú mediánom cez fold × pole v oboch režimoch. Spolu 100 vyhodnotení (5 modelov × 2 foldy × 5 polí v oboch režimoch).

### Modely

- **Persistence:** `y_lag_24h`, bez tréningu.
- **Fyzikálny baseline:** clear-sky/GTI výpočet z `solarops_ml.simulator.solar`, NOCT teplota bunky z externej teploty vzduchu, teplotný koeficient a AC clipping; bez tréningu.
- **Lineárna regresia:** scikit-learn 1.9.1, rovnaké features.
- **LightGBM 4.7.0:** `objective=regression`, `learning_rate=0.03`, `num_leaves=31`, `min_child_samples=100`, `feature_fraction=0.8`, `bagging_fraction=0.8`, `bagging_freq=1`, max 2000 kôl, early stopping 100, `seed=42`, 1 thread.
- **LSTM:** PyTorch 2.15.0, 1 vrstva, 32 jednotiek, vstupné okno 24 h, štandardizované features, Adam 1e-3, 20 epoch, early stopping s trpezlivosťou 4. Model sa ukladá ako `state_dict` (28 kB).

## Výsledky

### Walk-forward (medián cez fold × pole)

| Model | nMAE [%] | IQR | MAE | RMSE | wMAPE [%] | skill vs physical | tréning [s] | inferencia [ms/1000] | veľkosť [B] |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| persistence | 3,60 | 0,16 | 0,0360 | 0,0694 | 6,88 | −22,471 | 0,00 | 0,000 | 0 |
| **physical** | **0,15** | **0,03** | **0,0015** | **0,0022** | **0,30** | **+0,000** | **0,00** | **0,000** | **0** |
| linear | 0,79 | 0,19 | 0,0079 | 0,0103 | 1,56 | −4,270 | 0,01 | 0,000 | 128 |
| lightgbm | 0,15 | 0,01 | 0,0015 | 0,0021 | 0,29 | +0,037 | 4,36 | 0,156 | 6 192 259 |
| lstm | 0,29 | 0,03 | 0,0029 | 0,0042 | 0,56 | −0,931 | 19,62 | 0,012 | 27 925 |

### Leave-one-array-out (medián cez fold × vynechané pole)

| Model | nMAE [%] | IQR | MAE | RMSE | wMAPE [%] | skill vs physical | tréning [s] | inferencia [ms/1000] | veľkosť [B] |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| persistence | 3,60 | 0,16 | 0,0360 | 0,0694 | 6,88 | −22,471 | 0,00 | 0,000 | 0 |
| **physical** | **0,15** | **0,03** | **0,0015** | **0,0022** | **0,30** | **+0,000** | **0,00** | **0,000** | **0** |
| linear | 0,89 | 0,27 | 0,0089 | 0,0121 | 1,72 | −4,554 | 0,01 | 0,000 | 128 |
| lightgbm | 0,40 | 0,22 | 0,0040 | 0,0081 | 0,78 | −1,526 | 3,80 | 0,175 | 6 180 156 |
| lstm | 0,43 | 0,22 | 0,0043 | 0,0060 | 0,83 | −1,991 | 15,73 | 0,017 | 27 925 |

Persistence a fyzikálny baseline majú v oboch režimoch rovnaké čísla, lebo sa netrénujú a používajú tie isté testovacie riadky.

### Pozorovania

- **Fyzikálny baseline vyhráva.** Vo walk-forward má nMAE 0,155 %, LightGBM 0,148 % (rozdiel 0,007 percentuálneho bodu, medián skillu +0,037). V LOAO je baseline 0,155 %, LightGBM 0,405 % (skill −1,526) a LSTM 0,428 %. GBM teda nepreukázal zovšeobecnenie na inú geometriu/veľkosť poľa.
- **Výsledok je z veľkej časti tautologický:** syntetický generátor počíta výkon takmer rovnakým fyzikálnym modelom, aký používa baseline. Baseline je preto takmer „pravý“ model a jeho 0,15 % nie je dôkaz, že na reálnych dátach porazí GBM. Je to však dôkaz, že pipeline, split a metriky fungujú, a že fyzikálny baseline je veľmi silný fallback.
- **Persistence** má 3,60 % a je len kontrolný dolný limit.
- **Lineárna regresia** je horšia než baseline (0,79 % WF, 0,89 % LOAO), lebo nevie nelineárny teplotný efekt a AC clipping.
- **LSTM** má 0,29 % WF a 0,43 % LOAO, teda horšie než baseline aj GBM. Má malý model (28 kB) a rýchlu inferenciu, ale tréning je ~20 s a závislosť torch sa nevyplatí.
- **Cena:** LightGBM má ~6,2 MB a ~4 s tréningu; fyzikálny baseline 0 B a 0 s. Inferencia GBM je 0,156 ms/1000 riadkov, baseline prakticky 0; pre hodinovú prevádzku sú obe zanedbateľné.

## Rozhodnutie

**Pre MVP: fyzikálny baseline. LightGBM je challenger, nie MVP. LSTM sa odkladá.**

- Fyzikálny baseline je najpresnejší v LOAO a vo WF je s GBM nerozlíšiteľný; netreba trénovať, je vysvetliteľný, má nulovú veľkosť modelu a funguje aj pri chýbajúcich historických dátach. Pre MVP je to najnižšie riziko.
- LightGBM neprešiel akceptačnou bránou: vo WF je rozdiel prakticky nulový, v LOAO je horší než baseline. Jeho 6 MB model a závislosť `lightgbm` sú pre MVP zbytočná réžia.
- Ak sa na reálnych dátach ukáže, že GBM znižuje nMAE aspoň o 20 % voči baseline a dosahuje nMAE ≤ 6 % (#193), prepne sa MVP na GBM. Dovtedy ostáva baseline a GBM sa vyvíja ako challenger.
- LSTM sa vráti na stôl, keď budú reálne dáta a ak GBM po doladení features nebude stačiť. Na syntetických dátach nepriniesol zlepšenie.
- Pred produkciou sa musí experiment zopakovať na **reálnych DKASC dátach s Open-Meteo Historical Forecast** a rozhodnutie potvrdiť. Syntetický beh je dôkaz pipeline, nie presnosti.

**Akceptačná brána:** LightGBM mal znížiť nMAE aspoň o 20 % voči fyzikálnemu baseline a dosiahnuť nMAE ≤ 6 % (#193). Na syntetických dátach je rozdiel vo WF +0,037 skill (prakticky nula) a v LOAO −1,526 (horšie), takže brána **nie je splnená** a MVP zostáva baseline. Brána sa vyhodnotí znova na reálnych dátach.

### Parametre pre MVP (fyzikálny baseline)

- Vstup: Open-Meteo GHI/DNI/DHI/GTI, teplota vzduchu, vietor, oblačnosť, geometria a metadáta poľa.
- Výpočet: clear-sky/GTI, NOCT teplota bunky, teplotný koeficient a AC clipping na `capacity_kwp / dc_ac_ratio`.
- Žiadny tréning ani modelový súbor; verzia je verzia kódu a konfigurácie. Výstup sa dá porovnať s GBM a použiť ako fallback.

### Parametre pre challenger LightGBM

- Cieľ `y = P_hourly / kWp`, denné hodiny, zdravé polia, žiadne lagy výkonu v MVP.
- Features podľa ADR #191 (Open-Meteo GHI/DNI/DHI/GTI, oblačnosť, teplota vzduchu, vietor, clear-sky, `kt`, geometria, metadáta poľa). `weather_source` a `temperature_c` z telemetrie nie sú feature.
- `objective=regression`, `learning_rate=0.03`, `num_leaves=31`, `min_child_samples=100`, `feature_fraction=0.8`, `bagging_fraction=0.8`, `bagging_freq=1`, early stopping na časovej validácii, `seed=42`.
- Uloženie modelu s verziou, hashom tréningových dát a metadátami; monitoring driftu (#196).

## Dôsledky

- MVP image ML služby môže byť bez `lightgbm` a `torch` (len numpy/pandas + FastAPI), takže menší image a rýchlejší štart v Container Apps; inferencia je deterministický výpočet.
- Fyzikálny baseline je zároveň fallback, vysvetliteľná referencia a vstup pre monitoring. Ak sa neskôr prijme GBM, baseline ostáva ako poistka.
- Vysvetliteľnosť: výpočet sa dá rozložiť na žiarenie, teplotu bunky a clipping; technici vidia, prečo model očakáva daný výkon.
- Baseline sa kalibruje/validuje na DKASC (Austrália, 2 roky); pre SK treba validáciu (#178, #187). Reálne poruchy DKASC slúžia na hodnotenie detekcie, nie na tréning.
- `p_baseline` sa v tomto experimente **nepoužil** ako feature. Stacking (GBM nad fyzikálnym baseline) je samostatný experiment; môže zlepšiť presnosť, ale zvyšuje previazanosť modelov.
- Ak sa GBM prijme neskôr, experiment sa rozšíri o modelovú verziu a hash dát; dovtedy stačí verzia kódu a konfigurácie baselineu.

## Riziká / neoverené

- **Syntetické dáta:** rovnaký generátor ako fyzikálny baseline, preto je baseline takmer „pravý“ model a poradie modelov sa na reálnych dátach môže zmeniť. Toto je najväčšie obmedzenie výsledkov.
- **Chýba reálny DKASC dataset** a reálne poruchy; výsledky nie sú produkčný dôkaz.
- Fyzikálny baseline v experimente používa rovnaký NOCT/teplotný model ako simulátor; reálny pvlib ModelChain sa môže líšiť, preto treba kalibráciu na DKASC.
- Train/serve skew počasia (Historical Forecast vs. Forecast, #180); `weather_source` je len metadáta.
- Len 2 roky dát: sezónnosť a odľahlé roky nie sú pokryté; chýbajúce dni 2023 znižujú vzorku.
- Polia DKASC sú korelované (jeden areál, jedno počasie); LOAO nadhodnocuje nezávislosť.
- Domain gap Alice Springs → SK a syntetika → realita.
- LightGBM môže na reálnych dátach vyhrať; rozhodnutie sa preto musí potvrdiť rerunom, nie týmto behom.
- LSTM je malá a neladená; jej zlé výsledky na syntetike nie sú dôkazom, že LSTM je vo všeobecnosti nevhodná.
- OpenMP konflikt medzi LightGBM a PyTorch na macOS sa rieši `OMP_NUM_THREADS=1`; MVP baseline sa ho netýka, challenger GBM áno.

## Timebox

Timebox 10 h. Analytická časť, experiment a rozhodnutie sú hotové. Timebox bol prekročený o čas na implementáciu znovupoužiteľného modulu `solarops_ml.forecast`, testov a ladenie OpenMP konfliktu; výstupom je preto aj reprodukovateľný kód a testy, nielen notebook. Prekročenie je zdôvodnené tým, že bez kódu by experiment nebol opakovateľný na reálnych dátach.

## Zdroje

- LightGBM: https://lightgbm.readthedocs.io/ , PyPI https://pypi.org/project/lightgbm/
- pvlib ModelChain: https://pvlib-python.readthedocs.io/en/stable/user_guide/modelchain.html
- scikit-learn TimeSeriesSplit (gap): https://scikit-learn.org/stable/modules/generated/sklearn.model_selection.TimeSeriesSplit.html
- PyTorch LSTM: https://pytorch.org/docs/stable/generated/torch.nn.LSTM.html

## Nadväzujúce issues

1. **Rerun na reálnych dátach:** rovnaký experiment na DKASC 2023–2024 + Open-Meteo Historical Forecast; to je primárna brána pre prepnutie MVP z baselineu na GBM. Blokované prístupom k dátam (#178).
2. **Kalibrácia fyzikálneho baselineu:** overiť pvlib ModelChain, NOCT/teplotu bunky a AC clipping na DKASC; zmerať, o koľko sa líši od simulátora.
3. **LightGBM challenger:** na reálnych dátach doladiť hyperparametre len na validácii, zmenšiť 6 MB model (počet kôl, hĺbka, kvantizácia) a prepnúť MVP len pri splnení gate (≥ 20 % zlepšenie nMAE, nMAE ≤ 6 %, #193).
4. **Model mismatch v simulátore:** pridať do generátora efekt, ktorý fyzikálny baseline nepozná (napr. znečistenie, degradácia, nepresnosť GTI), aby syntetické porovnanie nebolo tautologické.
5. **Stacking s fyzikálnym baseline:** otestovať `p_baseline` ako feature a porovnať s čistým GBM; vyhodnotiť aj riziko previazanosti.
6. **LSTM revisit:** až po reálnych dátach a ak GBM nesplní cieľ; zvážiť dlhšie okno a viac vrstiev.
7. **Produkčný endpoint a monitoring:** #196 – načítanie baselineu/GBM, verzia/hash, drift vstupov a porovnanie s `P_baseline`.
8. **Notebook a report v CI:** voliteľný job s `--group experiment` (mimo produkčného image) na reprodukciu výsledkov.
