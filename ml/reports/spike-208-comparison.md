| Metóda | Precision | Recall | F1 | TP | FP | FN | Falošné / 30 dní | Medián oneskorenia (h) | Čas (s) |
|---|---|---|---|---|---|---|---|---|---|
| rule (ADR #194, ratio < 0.85, >= 2 h) | 0.45 | 1.00 | 0.62 | 13 | 16 | 0 | 0.86 | 2.0 | 0.15 |
| robust z-score < -2 | 0.33 | 0.92 | 0.49 | 12 | 22 | 1 | 1.29 | 2.0 | 0.14 |
| robust z-score < -3 | 0.73 | 0.85 | 0.79 | 11 | 4 | 2 | 0.21 | 2.0 | 0.14 |
| robust z-score < -4 | 0.77 | 0.77 | 0.77 | 10 | 0 | 3 | 0.00 | 2.0 | 0.14 |
| Isolation Forest, contamination 0.02 | 0.50 | 0.46 | 0.48 | 6 | 3 | 7 | 0.27 | 21.0 | 3.89 |
| Isolation Forest, contamination 0.05 | 0.25 | 0.77 | 0.38 | 10 | 17 | 3 | 1.34 | 12.0 | 3.84 |
| Isolation Forest, contamination 0.1 | 0.17 | 0.85 | 0.29 | 11 | 35 | 2 | 2.57 | 5.0 | 3.84 |
