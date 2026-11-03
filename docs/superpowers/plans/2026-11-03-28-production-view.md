# Plán: API pre prehľady výroby (#28: #100, #101, #102)

## Cieľ

Čítanie výroby inštalácie z `telemetry_readings` (#97/#350) agregovanej po 5 min / hodinách / dňoch / mesiacoch,
porovnanie s predikciou a CSV export. Bez novej schémy (PK `(device_id, ts)` dotaz pokryje).

## Endpointy (modul `Features/Production`, skupina `/api/installations/{installationId}/production`)

| Endpoint | Politika | Story |
|---|---|---|
| `GET …/production?from&to&granularity&timeZone` | `ProductionRead` | #100 |
| `GET …/production/comparison?from&to&granularity&timeZone` | `ProductionRead` | #101 |
| `GET …/production/export?from&to&granularity&timeZone&separator` | `ProductionRead` | #102 |

`ProductionRead` = všetky roly (ADR #207, riadok „Alerty, telemetria“: R pre všetkých, zákazník len vlastné).
Zákazník: `ICustomerScope`, cudzia inštalácia = 404 ešte pred dotazom/streamom.

## Dotaz (agregácia v SQL)

1. EF: id všetkých invertorov inštalácie (aj vymenených).
2. Raw SQL nad `telemetry_readings`: `device_id = ANY(@ids) AND ts BETWEEN from-1h AND to+1h` (rozsah PK).
   - energia = rozdiel čítača `total_wh` voči predchádzajúcemu meraniu zariadenia (`LAG`), záporné = 0;
   - meranie patrí do okna `ts - interval_sec` (ts je koniec okna);
   - najprv súčet zariadení za okno (výkon inštalácie), potom bucket: `date_bin('5 min')` alebo
     `date_trunc(unit, window_start, timeZone)`; špička = max súčtového výkonu.
3. Len buckety s dátami (bez dopĺňania medzier); prázdne obdobie = prázdne pole.

## Limity

JSON: 5 min ≤ 31 dní, hodina ≤ 366 dní, deň/mesiac ≤ 3660 dní. Export: 5 min ≤ 366 dní, ostatné ≤ 3660 dní.

## Predikcia (#101)

Predikcie zatiaľ neexistujú (#110/#111). Rozhranie `IProductionPredictionSource` s prázdnou implementáciou;
odpoveď vracia dvojice `actual`/`predicted` s `predicted = null`, `predictionAvailable = false`.
Granularita len hodina/deň/mesiac (predikcia je hodinová, ADR #196).

## CSV (#102)

`text/csv; charset=utf-8`, hlavička, `separator=semicolon|comma|tab` (predvolene `semicolon`, pri ňom desatinná
čiarka pre slovenský Excel), streamované cez `AsAsyncEnumerable`.

## Testy (TDD)

- Unit: validácia dotazu (formát dátumu, rozsah, granularita, časové pásmo, oddeľovač), formát CSV.
- Integračné: agregácia hodina/deň/mesiac s dvoma invertormi, lokálny deň, prázdne obdobie, 401, 403 (bez roly),
  zákazník vlastné 200 / cudzie 404, porovnanie s `null` predikciou aj s podvrhnutým zdrojom, CSV hlavička a oddeľovač.
