# Vyhodnotenie detekcie podvýroby na simulovaných poruchách (#118)

Scenáre: 40 × 14 dní, seed 42. Pravidlo ADR #194 (prah 0.85, >= 2 h). Chyba predikcie (nMAE voči bezporuchovému výkonu): priemer 2.9%, max 4.4%.

Syntetické poruchy sú optimistická horná hranica (ADR #187); hlavné hodnotenie zostáva na reálnych poruchách DKASC.

## Hlavné metriky (udalosti, ADR #193)

| Metrika | Primárna | Overlap-only |
|---|---|---|
| Precision | 0.45 | 0.45 |
| Recall | 1.00 | 1.00 |
| F1 | 0.62 | 0.62 |

TP 13, DUP 0, LATE 0, FP 16, FN 0 (labelov 13, ignorovaných alarmov 5). Medián oneskorenia 2.00 h, alerts-per-event 1.00, falošné poplachy (FP + DUP) na inštaláciu za 30 dní 0.86.

## Podľa typu scenára

| Typ | Scenáre | Labely | Detegované | Neskoro | Recall | Alarmy | Falošné |
|---|---|---|---|---|---|---|---|
| below_threshold | 4 | 0 | 0 | 0 | – | 7 | 2 |
| data_outage | 4 | 0 | 0 | 0 | – | 0 | 0 |
| healthy | 8 | 0 | 0 | 0 | – | 4 | 4 |
| inverter_fault | 4 | 0 | 0 | 0 | – | 1 | 1 |
| power_drop, loss 15-35 %, < 24 h | 1 | 1 | 1 | 0 | 1.00 | 1 | 0 |
| power_drop, loss 15-35 %, >= 24 h | 1 | 1 | 1 | 0 | 1.00 | 2 | 1 |
| power_drop, loss 35-65 %, >= 24 h | 7 | 7 | 7 | 0 | 1.00 | 9 | 2 |
| power_drop, loss >= 65 %, < 24 h | 1 | 1 | 1 | 0 | 1.00 | 1 | 0 |
| power_drop, loss >= 65 %, >= 24 h | 3 | 3 | 3 | 0 | 1.00 | 5 | 2 |
| snow | 4 | 0 | 0 | 0 | – | 2 | 2 |
| unobservable | 3 | 0 | 0 | 0 | – | 2 | 2 |

`below_threshold` = strata < 15 % (pod prahom detekcie, nepočíta sa ako FN), `unobservable` = porucha bez 2 denných hodín, `inverter_fault` = rieši alert stavu striedača (hodiny sú neplatné).

## Falošné poplachy s pravidlami #114 a bez nich

| Variant | Precision | Recall | FP | DUP | Falošné / 30 dní |
|---|---|---|---|---|---|
| Pravidlo ADR #194 + filtre #114 | 0.45 | 1.00 | 16 | 0 | 0.86 |
| Naivné (výpadok = 0 W, bez stavu, oblačnosti, snehu) | 0.33 | 1.00 | 26 | 0 | 1.39 |

## Mriežka prah × trvanie (ADR #194)

| Prah r | Hodiny | Precision | Recall | F1 | F0,5 | Falošné / 30 dní | Oneskorenie (h) |
|---|---|---|---|---|---|---|---|
| 0.75 | 1 | 0.81 | 1.00 | 0.90 | 0.84 | 0.16 | 1.00 |
| 0.75 | 2 | 0.87 | 1.00 | 0.93 | 0.89 | 0.11 | 2.00 |
| 0.75 | 3 | 0.93 | 1.00 | 0.96 | 0.94 | 0.05 | 3.00 |
| 0.75 | 4 | 0.93 | 1.00 | 0.96 | 0.94 | 0.05 | 4.00 |
| 0.80 | 1 | 0.50 | 1.00 | 0.67 | 0.56 | 0.70 | 1.00 |
| 0.80 | 2 | 0.72 | 1.00 | 0.84 | 0.76 | 0.27 | 2.00 |
| 0.80 | 3 | 0.81 | 1.00 | 0.90 | 0.84 | 0.16 | 3.00 |
| 0.80 | 4 | 0.81 | 1.00 | 0.90 | 0.84 | 0.16 | 4.00 |
| 0.85 | 1 | 0.19 | 1.00 | 0.33 | 0.23 | 2.89 | 1.00 |
| 0.85 | 2 | 0.45 | 1.00 | 0.62 | 0.50 | 0.86 | 2.00 |
| 0.85 | 3 | 0.62 | 1.00 | 0.76 | 0.67 | 0.43 | 3.00 |
| 0.85 | 4 | 0.81 | 1.00 | 0.90 | 0.84 | 0.16 | 4.00 |
| 0.90 | 1 | 0.07 | 1.00 | 0.13 | 0.09 | 9.21 | 1.00 |
| 0.90 | 2 | 0.13 | 1.00 | 0.23 | 0.15 | 4.77 | 2.00 |
| 0.90 | 3 | 0.19 | 1.00 | 0.33 | 0.23 | 2.89 | 3.00 |
| 0.90 | 4 | 0.28 | 1.00 | 0.43 | 0.32 | 1.82 | 4.00 |
