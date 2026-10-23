# ADR spike #220: Úložisko časových rád telemetrie (PostgreSQL vs. TimescaleDB vs. Azure Data Explorer)

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #220 (nadradené #5)
- **Súvisí:** kontrakt telemetrie (spike #185), #199 (formát a simulácia), #196 (ML číta dáta), #211 (hosting DB)

## Kontext

Invertory posielajú každých 5 minút JSON `readings[]` (kľúč `(deviceId, timestamp)`, idempotentný ingest). Odhad objemu: **100 inštalácií x 288 vzoriek/deň = 28 800 záznamov/deň, 10,5 mil. riadkov/rok**. Dotazy: (1) graf jedného zariadenia za deň alebo mesiac, (2) agregácie hodina/deň pre prehľady a export, (3) posledná hodnota každého zariadenia (stav online/offline), (4) dávkové čítanie pre ML (tréning, rezíduum voči predikcii). Databáza je už PostgreSQL 16 (Azure Database for PostgreSQL Flexible Server), spoločná s doménovými dátami.

## Zvažované možnosti

| Kritérium | A. Tabuľka v PostgreSQL (+ neskôr partitioning, BRIN) | B. TimescaleDB | C. Azure Data Explorer (ADX) |
|---|---|---|---|
| Objem 10,5 mil. riadkov/rok | zvláda (meranie nižšie) | zvláda | predimenzované |
| Ďalšia služba / jazyk | nie | rozšírenie PostgreSQL | **áno**: ďalšia služba, KQL, samostatný ingest a spojenie s relačnými dátami cez export alebo externú tabuľku |
| Podpora v Azure PostgreSQL Flexible | natívna | rozšírenie `timescaledb` je dostupné (v. 2.24.0 pre PG 15 až 18), **ale len Apache-2 edícia** [1] | n/a |
| Funkcie | natívne partície, BRIN, `INSERT ... ON CONFLICT` | hypertable, retencia; kompresia a continuous aggregates patria podľa upstreamu do Timescale License (TSL), ktorú Azure neponúka [1][2] (**neoverené**, nepreverené v tomto spiku) | výborná kompresia a agregácie, retencia |
| Cena a limity | v rámci existujúcej DB | v rámci existujúcej DB | platený cluster (cena **neoverená**, vypočítať v Azure Pricing Calculator); **free cluster**: zdarma na 1 rok, ~100 GB nekomprimovane, max 10 DB a 100 stĺpcov na tabuľku [3], SLA a podmienky pre školský projekt **neoverené** |
| Testovateľnosť lokálne | Testcontainers.PostgreSql | vlastný image (nie čistý `postgres:16`) | emulátor neexistuje (**neoverené**) |
| Riziko pre tím | nízke | stredné | vysoké |

## Rozhodnutie

**PostgreSQL tabuľka `telemetry_readings` v hlavnej databáze.** Neskôr (keď sa dáta začnú hromadiť, nie v Sprinte 1) **mesačný natívny partitioning** podľa `ts` a **BRIN** index na `ts`; **TimescaleDB** len ak to ukážu merania; **Azure Data Explorer zamietnutý** (ďalšia služba a jazyk, cena platenej varianty a limity free clustra, objem to nevyžaduje).

### Návrh schémy (navrhované; EF entita v module telemetrie, snake_case, bez migrácií v Sprinte 1)

```sql
-- surové merania, 1 riadok = 1 vzorka 5 min
telemetry_readings(
  device_id uuid not null, ts timestamptz not null,   -- UTC
  message_id uuid not null, status smallint not null,
  ac_power_w real, dc_power_w real, voltage_v real, current_a real,
  frequency_hz real, temperature_c real,
  day_wh double precision, total_wh double precision,
  primary key (device_id, ts)                          -- dedup podľa kontraktu #185
);
create index telemetry_readings_ts_brin on telemetry_readings using brin (ts);

-- agregácie (plnené jobom, upsert posledných 48 h kvôli oneskoreným dátam)
telemetry_hourly(device_id uuid, hour_start timestamptz, avg_power_w real, max_power_w real,
                 energy_wh double precision, sample_count smallint, primary key (device_id, hour_start));
telemetry_daily(device_id uuid, day date, energy_wh double precision, peak_power_w real,
                sample_count int, primary key (device_id, day));   -- deň v UTC; lokálny deň rieši API
```

Pozn.: primárny kľúč obsahuje `ts`, takže je kompatibilný s neskorším partitioningom podľa `ts`. Výnimka z konvencie "Guid kľúče": surové merania sú identifikované `(device_id, ts)`.

### Retencia (návrh)

- Surové merania: **13 mesiacov** (po zapnutí partitioningu zrušenie partície: `DROP`/`DETACH` mesačnej partície, bez `DELETE`).
- `telemetry_hourly` a `telemetry_daily`: bez expirácie (v meraní 98 MB/rok pre 100 zariadení).
- Ingest: dávkový `INSERT ... ON CONFLICT (device_id, ts) DO NOTHING`, vracia 202; duplicity sa ignorujú.

### Meranie na simulovaných dátach (spustené)

PostgreSQL 16 (Docker `postgres:16-alpine`, Apple M2, lokálne), 100 zariadení (uuid) x 365 dní x 288 = **10 512 000 riadkov**, vložené v časovom poradí (ako reálny ingest), skript `docs/adr/assets/spike-220-benchmark.sql`:

| Údaj | Výsledok |
|---|---|
| Veľkosť tabuľky | 1 903 MB celkom (heap 1 173 MB, PK 729 MB, BRIN 176 kB) |
| Graf 1 zariadenia za deň (288 riadkov) | 2,5 ms (bitmap scan podľa PK) |
| 1 zariadenie, 1 mesiac, hodinový priemer z surových dát (8 640 riadkov) | 319 ms za studena, 93 ms, 16 ms pri opakovaní |
| To isté z `telemetry_hourly` (720 riadkov) | 0,2 ms |
| Denná agregácia všetkých zariadení za mesiac (864 000 riadkov, BRIN bitmap scan) | 360 ms |
| Posledná hodnota každého zariadenia (`LATERAL ... ORDER BY ts DESC LIMIT 1` nad 100 zariadeniami) | 1,0 ms |
| Vloženie dávky 100 riadkov (jeden 5-minútový tik) s `ON CONFLICT DO NOTHING` | 1,7 ms |
| `telemetry_hourly` (10,5 mil. riadkov agregovaných na 876 000) | 98 MB |

Závery: surové dáta za rok sú rádovo 2 GB, čo je bezproblémové; dotazy na grafy za dlhšie obdobie čítajú **agregačné tabuľky**, nie surové merania; náklady na uuid kľúč sú citeľné (PK je 729 MB), preto je partitioning a retencia užitočná až neskôr. Meranie je jednorazové, lokálne a bez súbežnej záťaže (**neoverené** pre Azure B1ms, kde je menej RAM a IOPS).

## Dôsledky

- (+) Jedna databáza, žiadna nová služba; testy cez Testcontainers; ML číta tie isté tabuľky (alebo cez API).
- (+) Cesta na zväčšenie je zdokumentovaná (partitioning, BRIN, prípadne TimescaleDB).
- (-) Agregácie a retenciu treba riešiť vlastným jobom (Timescale continuous aggregates nie sú na Azure Flexible k dispozícii, ak je tvrdenie [2] správne).
- (-) Pri desaťnásobnom raste (1 000 inštalácií) je treba znova zmerať a zapnúť partitioning.
- Implementácia **musí** použiť tabuľku `telemetry_readings` v PostgreSQL s kľúčom `(device_id, ts)`; bez TimescaleDB a ADX.

## Riziká a neoverené

- Výkon na cieľovej B1ms inštancii, súbežné čítanie a zápis (len lokálne jednorazové meranie).
- Funkcie TimescaleDB mimo Apache-2 edície a ich nedostupnosť v Azure (iba z dokumentácie [1], upstream licencia [2]).
- Cena platenej ADX a podmienky free clustra pre projekt; podpora lokálneho vývoja pre ADX.
- Presná definícia dňa pre `telemetry_daily` (UTC vs. lokálny čas inštalácie).

## Zdroje

1. Azure Database for PostgreSQL, `timescaledb` (Apache-2 edícia): https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-considerations a https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-versions
2. TimescaleDB licencia (Apache 2.0 a Timescale License pre adresár `tsl`): https://github.com/timescale/timescaledb/blob/main/LICENSE
3. Azure Data Explorer, free cluster (limity): https://learn.microsoft.com/en-us/azure/data-explorer/start-for-free
4. Kontrakt telemetrie: spike #185.

## Nadväzujúce issues

- #94 simulátor, #95 ingest endpoint, #96 autentifikácia zariadenia (Sprint 1, feature #26).
- #97 Uložiť merania do databázy, #98 Agregovať výrobu na hodiny a dni, #99 Ošetriť chýbajúce a chybné merania, #100 Zobraziť výrobu inštalácie za obdobie (Sprint 2, features #27, #28).
