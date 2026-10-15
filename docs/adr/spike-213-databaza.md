# ADR-213: Databáza - Azure SQL vs PostgreSQL

- Stav: prijaté (Sprint 0)
- Dátum: 2026-10-15
- Issue: #213

> Stav k 2026-10-15. Overené položky majú zdroj, neoverené sú označené **[NEOVERENÉ]**. Spike nemenil žiadne súbory projektu.

## Kontext
Kurz používal Azure SQL, tím pozná aj PostgreSQL. Dáta: transakčné (zákazky, projekty, používatelia) a časový rad telemetrie z invertorov každých 5-15 min. Python ML služba musí čítať dáta. Rozpočet: Azure for Students ($100 kredit na 12 mesiacov) + free ponuky. Projekt trvá ~3 mesiace.

Odhad objemu: 100 invertorov x 12 meraní/hod (5 min) x 24 h = ~29 tis. riadkov/deň na jedno meranie na invertor; aj pri 10 kanáloch stovky tisíc riadkov/deň, teda desiatky miliónov za rok. Pre projekt je to malý až stredný objem (zvládne obe DB bez špeciálnych nástrojov).

## Možnosti

| Kritérium | Azure SQL Database (free offer) | Azure Database for PostgreSQL Flexible Server |
|---|---|---|
| EF Core provider | `Microsoft.EntityFrameworkCore.SqlServer`, EF Core 8, 9, 10 (oficiálny, Microsoft) | `Npgsql.EntityFrameworkCore.PostgreSQL`; NuGet má 10.0.x (10.0.0 až 10.0.3). Stránka providerov na Learn uvádza len EF 8, 9 (pravdepodobne neaktuálna) |
| Migrácie | `dotnet ef migrations` | rovnaké `dotnet ef migrations` |
| Free / študentská ponuka | 100 000 vCore s/mes, 32 GB; **stále zdarma**; po vyčerpaní auto-pauza do konca mesiaca (alebo platba za presah) | 750 h B1ms, 32 GB úložisko + 32 GB záloha; **12 mesiacov** (pre študentov uvedené na stránke Azure for Students) |
| Cena po free | serverless GP | B1ms približne 12-15 USD/mes compute podľa regiónu (tretia strana, jún-júl 2026) + úložisko |
| Always-on pri ingestii každých 5-15 min | problém: free limit 100 000 vCore s = len ~28 h behu pri 1 vCore; časté pripojenia bránia auto-pause a vyčerpajú limit; po vyčerpaní je DB nedostupná | 750 h = celý mesiac nepretržite, bez pauzovania |
| Časové rady | natívne partitioning (v Azure SQL len v PRIMARY filegroup), columnstore; bez specializovanej extenzie | declarative partitioning, BRIN indexy; **TimescaleDB** dostupný ako extension (allowlist `azure.extensions`, `shared_preload_libraries`, rola `azure_pg_admin`) |
| TimescaleDB na Azure | n/a | iba **Apache-2 edícia**; Toolkit nedostupný (info z 2023). Kompresia a continuous aggregates sú v Community edícii, teda zrejme nedostupné [NEOVERENÉ] |
| Lokálny vývoj | Docker image `mssql` (ťažší, ~1.5 GB RAM, na Apple Silicon beží cez emuláciu) | `postgres` alpine image, ľahký, natívny ARM |
| Testcontainers (.NET) | modul pre MsSql existuje (detaily nepreverené) | `Testcontainers.PostgreSql` (overené v dokumentácii) |
| Prístup z Pythonu | `pyodbc`/`mssql-python` + ODBC driver (zložitejšia inštalácia v Docker image) | `psycopg` / SQLAlchemy / pandas, bez natívnych ovládačov navyše |
| Rizikové miesta | tím už pozná z kurzu | rozdiely v SQL dialekte oproti kurzu; `timestamptz` a UTC dôsledne |

## Rozhodnutie
**PostgreSQL (Azure Database for PostgreSQL Flexible Server, Burstable B1ms) s EF Core + Npgsql.**

Odôvodnenie:
1. Free ponuka (750 h = nepretržitý beh) pokrýva celé trvanie projektu (3 mesiace << 12 mesiacov). Azure SQL free (100 000 vCore s) nestačí na always-on DB s pravidelnou ingestiou a po vyčerpaní sa pozastaví do konca mesiaca, čo by zhodilo demo.
2. Jednoduchší lokálny vývoj (ľahký kontajner, natívny ARM), jednoduchšie Testcontainers a jednoduchší prístup z Pythonu (`psycopg`/pandas).
3. Pre časové rady máme dobrú cestu eskalácie: najprv natívne partitioning po mesiacoch + BRIN/B-tree index `(inverter_id, ts DESC)`; TimescaleDB (Apache edícia) pridať, až ak to meranie ukáže ako potrebné.
4. EF Core migrácie a ostatné vzory fungujú rovnako.

Zámerne **nepoužívame** TimescaleDB od začiatku: Apache edícia nemá všetky funkcie, pridáva prevádzkovú zložitosť (preload knižnice, obmedzenia pri restore a pri major upgrade) a pri našom objeme nie je nutná.

Konkrétne usmernenia:
- Telemetria: tabuľka `telemetry_reading(inverter_id, ts timestamptz, ...)`, PK `(inverter_id, ts)`, partitioning RANGE podľa `ts` (mesačne), vytváranie partícií migráciou/jobom. EF Core nemá natívnu podporu partícií, partície a BRIN riešiť cez raw SQL v migrácii.
- Hromadný zápis cez `COPY`/`EFCore.BulkExtensions` alebo `ExecuteSql`, nie po jednom `SaveChanges`.
- ML služba: samostatný read-only DB používateľ, čítanie cez SQLAlchemy/psycopg.
- Stopnúť/zmazať server mimo vývoja: pozor, aj pri zastavenom serveri sa účtuje úložisko a záloha (Q&A jún 2025).

## Dôsledky
- (+) Beh v rámci free ponuky; konzistentné lokálne, CI aj produkčné prostredie (PostgreSQL všade).
- (+) Python bez natívnych ovládačov.
- (-) Free ponuka je časovo obmedzená (12 mes.); po jej skončení ~12-15 USD/mes + úložisko. Pre projekt nerelevantné.
- (-) Treba overiť, že účet Azure for Students má PostgreSQL Flexible Server dostupný v zvolenom regióne (niektoré študentské predplatné majú obmedzené regióny a SKU). Overiť skutočným vytvorením servera v prvom týždni.
- (-) Tím sa musí naučiť niektoré rozdiely oproti SQL Serveru (`timestamptz`, sekvencie/identity, case sensitivity názvov).
- Rozhodnutie sa dá zvrátiť: EF Core abstrahuje väčšinu, ale raw SQL (partície, BRIN) by sa prepisovalo.

## Zdroje
- Azure for Students (PostgreSQL 12 mes., Azure SQL always-free): https://azure.microsoft.com/en-us/free/students
- Azure SQL free offer: https://learn.microsoft.com/azure/azure-sql/database/free-offer
- Azure SQL free offer FAQ: https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq
- Flexible Server free offer (oznámenie): https://azure.microsoft.com/updates/azure-database-for-postgresql-flexible-server-azure-free-offer-coming-soon
- Poplatky pri zastavenom serveri: https://learn.microsoft.com/en-sg/answers/questions/2287609/i-am-being-charged-for-postgressql-db
- Extensions considerations (TimescaleDB Apache-2 edícia, allowlist, preload): https://learn.microsoft.com/en-us/azure/postgresql/extensions/concepts-extensions-considerations
- Podporované verzie PostgreSQL (11-18) a vytvorenie servera: https://learn.microsoft.com/en-us/azure/postgresql/configure-maintain/quickstart-create-server
- TimescaleDB Toolkit nedostupný (Q&A 2023): https://learn.microsoft.com/en-us/answers/questions/1180513/supporting-timescaledb-toolkit-extension-on-azure
- EF Core providery: https://learn.microsoft.com/en-us/ef/core/providers/
- Npgsql EF Core na NuGet (10.0.x): https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL
- Testcontainers PostgreSQL: https://dotnet.testcontainers.org/modules/postgres/
- Azure SQL partitioning (PRIMARY filegroup): https://www.mssqltips.com/sqlservertip/3494/azure-sql-database--table-partitioning/
- Ceny B1ms (tretia strana, orientačné): https://www.bytebase.com/dbcost/azure-flexible/instance/B1ms/
- Azure PostgreSQL pricing (oficiálne, ceny sa nevykreslili): https://azure.microsoft.com/pricing/details/postgresql/

## Nadväzujúce issues
- Vytvoriť Flexible Server B1ms v študentskom predplatnom a overiť dostupnosť regiónu a free ponuky (prvý týždeň).
- Kostra EF Core: `Npgsql` provider, prvá migrácia, naming convention (snake_case).
- Návrh schémy telemetrie + partitioning + indexy (`db-expert`), test výkonu na syntetických dátach (napr. 50 mil. riadkov).
- Testcontainers integračné testy (`Testcontainers.PostgreSql`) v CI.
- ML: read-only používateľ, pripojenie cez `psycopg`/SQLAlchemy, spoločný `.env` v compose.
- Rozhodnutie o TimescaleDB až po meraní (spike, nízka priorita).
- Rozpočtové upozornenia (Cost Management alert) na kredit $100.
