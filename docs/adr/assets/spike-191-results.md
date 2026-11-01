# Spike #191: baseline vs. gradient boosting vs. LSTM

Generated: `2026-11-01T17:06:57.539054+00:00`

**Dáta:** syntetický simulátor #94 (5 polí: south_15, south_20, south_25, south_30, south_35; rovnaké počasie, hodinová granularita). Reálne DKASC dáta treba zopakovať pred produkčným rozhodnutím.

**Delenie:** expanding-window walk-forward a leave-one-array-out s 7-dňovou medzerou (2024H1, 2024H2). Metriky sú mediánom cez fold × pole; nMAE je v % menovitého DC výkonu.

## Walk-forward

| Model | nMAE medián | IQR | MAE | RMSE | wMAPE | skill vs physical | tréning [s] | inferencia [ms/1000] | veľkosť [B] |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| persistence | 3.60 | 0.16 | 0.0360 | 0.0694 | 6.88 | -22.471 | 0.00 | 0.000 | 0 |
| physical | 0.15 | 0.03 | 0.0015 | 0.0022 | 0.30 | +0.000 | 0.00 | 0.000 | 0 |
| linear | 0.79 | 0.19 | 0.0079 | 0.0103 | 1.56 | -4.270 | 0.01 | 0.000 | 128 |
| lightgbm | 0.15 | 0.01 | 0.0015 | 0.0021 | 0.29 | +0.037 | 4.36 | 0.156 | 6192259 |
| lstm | 0.29 | 0.03 | 0.0029 | 0.0042 | 0.56 | -0.931 | 19.62 | 0.012 | 27925 |

## Leave-one-array-out

| Model | nMAE medián | IQR | MAE | RMSE | wMAPE | skill vs physical | tréning [s] | inferencia [ms/1000] | veľkosť [B] |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| persistence | 3.60 | 0.16 | 0.0360 | 0.0694 | 6.88 | -22.471 | 0.00 | 0.000 | 0 |
| physical | 0.15 | 0.03 | 0.0015 | 0.0022 | 0.30 | +0.000 | 0.00 | 0.000 | 0 |
| linear | 0.89 | 0.27 | 0.0089 | 0.0121 | 1.72 | -4.554 | 0.01 | 0.000 | 128 |
| lightgbm | 0.40 | 0.22 | 0.0040 | 0.0081 | 0.78 | -1.526 | 3.80 | 0.175 | 6180156 |
| lstm | 0.43 | 0.22 | 0.0043 | 0.0060 | 0.83 | -1.991 | 15.73 | 0.017 | 27925 |

## Poznámky

- Persistence = výkon pred 24 h; physical = pvlib-like clear-sky/GTI model bez tréningu.
- LightGBM je trénovaný s early stopping na posledných 15 % tréningu (7-dňová medzera).
- LSTM má 1 vrstvu, 32 jednotiek, okno 24 h a rovnaké features; výsledok je orientačný.
- LightGBM v experimente nepreukázal zlepšenie voči fyzikálnemu baselineu (WF prakticky nerozhodné, LOAO horšie); baseline je preto MVP a GBM challenger.
- `temperature_c` z telemetrie sa nepoužíva (obsahuje časť cieľa); features používajú externú `air_temp_c`.
- Syntetické dáta majú rovnaký generátor ako fyzikálny baseline, preto je baseline silný; reálne dáta môžu poradie zmeniť.
