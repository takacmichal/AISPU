# Scenár dema SolarOps

Issue #188. Scenár je postavený len na tom, čo API a frontend reálne vedia. Pôvodná verzia je zo 2026-10-28, krok 7 a medzery sú aktualizované k 2026-11-08 (pozri [Kontrola v Azure](#kontrola-v-azure-2026-11-08)). Čo chýba, je označené **„zatiaľ nie je implementované“** a v demo sa ukazuje cez plán B (slajd, simulátor, BPMN).

## Predpoklady a príprava

- Lokálny stack: `docker compose up -d postgres`, `dotnet run --project src/SolarOps.Api` (http://localhost:5080), `cd frontend && pnpm dev` (http://localhost:5173).
- Prihlásenie: `demo@solarops.local` / `DemoPass12345` (rola Admin, smie všetko). Technik: `technik@demo.solarops.local` / `DemoTech12345`.
- Obnova dát (pred každým prejdením): `python3 scripts/demo/reset-demo.py`. Skript zmaže len demo riadky (zákazníci `*@demo.solarops.local`, komponenty `DEMO-*`) a znova ich vytvorí cez API. Odmietne bežať, ak DB alebo API nie je na localhoste. Nastavenia cez env: `DEMO_API_URL`, `DEMO_DB_URL`, `DEMO_EMAIL`, `DEMO_PASSWORD`, `DEMO_TECH_PASSWORD`. Beh trvá cca 3 s.
- **Výroba** (súčasť resetu, vypína ju `--no-production`): elektráreň Nováka dostane 30 dní 5-minútovej telemetrie až po dnešnú polnoc UTC. Postup:
  - Simulátor z `ml/` ju vypočíta zo skutočného počasia tých dní (archív Open-Meteo).
  - ML predikcia sa počíta z rovnakého počasia, takže zdravé dni sedia s predikciou na cca 99 %.
  - Posledné 2 dni klesne výkon na 55 %. ML detekcia z toho urobí prebiehajúci alarm podvýroby a servisný tiket „Podvýroba: …“.

  Potrebné je `uv` (`ml/`), internet a bežiaca ML služba za API. Lokálne ju spustíš príkazom `cd ml && uv run uvicorn solarops_ml.api:app --port 8000`. Táto časť trvá cca 20 s. Údaje sú statické: nová výroba pribudne až ďalším resetom.
- **Azure** (nasadené prostredie, `infra/README.md`): `python3 scripts/demo/reset-demo.py --remote`. Heslá z repozitára sa tu nepoužijú, všetko ide z `infra/.env`:
  ```bash
  set -a; . infra/.env; set +a
  SRV=solarops-db-italynorth-<suffix>; MYIP=$(curl -s https://api.ipify.org)
  az postgres flexible-server firewall-rule create -g solarops-rg --server-name $SRV --name demo-reset-temp \
    --start-ip-address $MYIP --end-ip-address $MYIP          # len na čas resetu
  DEMO_API_URL=https://<api>.azurecontainerapps.io DEMO_EMAIL=admin@solarops.local \
  DEMO_PASSWORD="$SOLAROPS_ADMIN_PASSWORD" DEMO_ACCOUNT_PASSWORD="$SOLAROPS_DEMO_PASSWORD" \
  DEMO_TECH_PASSWORD="$SOLAROPS_DEMO_PASSWORD" \
  DEMO_DB_URL="Host=$SRV.postgres.database.azure.com;Database=solarops;Username=solarops;Password=$SOLAROPS_POSTGRES_PASSWORD" \
  python3 scripts/demo/reset-demo.py --remote
  az postgres flexible-server firewall-rule delete -g solarops-rg --server-name $SRV --name demo-reset-temp --yes
  ```
  Bez `DEMO_DB_URL` (a bez firewall pravidla) skript len nahrá dáta do prázdnej databázy; ak demo zákazníci existujú, odmietne bežať. SQL ide cez `psql` alebo `docker run postgres:16-alpine` s TLS. Simulátor distribučky je v Azure zapnutý (`ConnectionRequest__Simulator__Enabled=true`). Účty: admin `admin@solarops.local` (`SOLAROPS_ADMIN_PASSWORD`), ostatné roly `*@demo.solarops.local` (`SOLAROPS_DEMO_PASSWORD`).
- Ukazovanie API: Swagger/OpenAPI `http://localhost:5080/openapi/v1.json`, alebo `.http` súbory v `src/SolarOps.Api/Features/*/`.

## Pripravené dáta

| Zákazník | Stav po resete | Použitie |
|---|---|---|
| Ján Novák (`novak@…`) | návrh s verziou, prijatá ponuka, podpísaná zmluva so zálohou, inštalácia `Commissioned`, 1 striedač + 2 panely, žiadosť o pripojenie `Draft`, 30 dní výroby s predikciou, alarm podvýroby a tiket z neho | „hotový“ zákazník: pripojenie, zariadenia, monitoring a detekcia |
| Mária Kováčová (`kovacova@…`) | len zákazník, odberné miesto a žiadosť o návrh (`Requested`) | živý začiatok: obhliadka, návrh, ponuka, zmluva |
| Peter Horváth (`horvath@…`) | podpísaná zmluva, inštalácia `Planned` | záložný zákazník pre plán B |

Katalóg: `DEMO-PAN-450` (panel 450 W, 145 €), `DEMO-INV-10K` (striedač 10 kW, 1 450 €), `DEMO-LAB-INST` (práca, 55 €/h).

## Scenár krok po kroku (cca 12 min)

Hodnoty `<…>` sú id vypísané skriptom `reset-demo.py` na konci behu.

| # | Čas | Kto | Čo urobí | Očakávaný výsledok | Stav |
|---|---|---|---|---|---|
| 0 | 1 min | Moderátor | Úvod: firma predáva a servisuje FVE, ukáže prihlásenie vo frontende. | Prihlásenie cez `/login`, dashboard. | UI |
| 1 | 1 min | Obchod | Frontend `/customers`: otvorí Máriu Kováčovú, ukáže odberné miesto. | Detail zákazníka s odberným miestom (EIC `24ZDEMO00000002B`). | UI |
| 2 | 1 min | Projektant | `POST /api/designs/<kovacova_design>/site-survey` s poznámkou z obhliadky. | 200, stav `SiteSurveyDone`. | API |
| 3 | 1 min | Projektant | `PUT /api/designs/<id>/parameters` (poloha, spotreba 4 800 kWh, strešná rovina). | 200, stav `InDesign`. | API |
| 4 | 2 min | Projektant | `POST …/configuration/preview` s 20 panelmi a striedačom, potom `POST …/versions`. | Kompatibilita OK (DC 9 kWp, AC 10 kW, pomer 0,9), odhad výroby cca 8 690 kWh/rok, verzia 1. | API |
| 5 | 2 min | Obchod | **Ponuka:** `POST /api/quotes` (20× panel, 1× striedač, 24 h práce, DPH 23 %), `…/send`, `…/accept`. | Stavy `Draft`, `Sent` (platnosť 30 dní), `Accepted`. | API |
| 6 | 1 min | Obchod | `POST /api/contracts` z prijatej ponuky, `…/sign` s odberným miestom, `…/deposit/pay`. | Zmluva `ZM-2026-…` `Signed`, záloha `Paid`, vznikne inštalácia `Planned`. | API |
| 7 | 2 min | Obchod, Projektant, Operátor | **Pripojenie:** `POST /api/connection-requests` (EIC, 10 kW, schéma a súhlas vlastníka ako prílohy), Projektant `POST …/submit`, Operátor zahrá distribučku cez simulátor `POST …/decision` s `{"decision": "Approve"}` ([simulátor](../simulator-distribucky.md)). | 201 `Draft`, potom `Submitted` (beží lehota 10 pracovných dní), potom `ContractOffered`. Ďalšie kroky až po `Concluded` sú v [simulator-distribucky.md](../simulator-distribucky.md). | API |
| 8 | 1 min | Technik | Na hotovom zákazníkovi Novák ukáže `GET /api/installations/<novak_installation>` a `…/devices`. | Stav `Commissioned`, história stavov, striedač a 2 panely so sériovými číslami. | API |
| 9 | 1 min | Operátor | **Monitoring:** frontend `/monitoring`, elektráreň Nováka, obdobie 30 dní. | Graf skutočnej výroby oproti ML predikcii. Zdravé dni sa takmer prekrývajú, posledné 2 dni sú výrazne pod predikciou. | UI |
| 10 | 1 min | Operátor | **Detekcia anomálií:** sekcia alarmov na tej istej stránke. | Prebiehajúci alarm podvýroby: medián cca 55 % predikcie, odhad straty v kWh, závažnosť Medium. | UI |
| 11 | 1 min | Operátor | **Tiket:** frontend `/tickets`, tiket „Podvýroba: FVE 9,9 kWp Novak (demo)“. | Tiket s pôvodom `AnomalyDetected`, popis s dôkazmi po hodinách. Nadväzuje BPMN proces #68. | UI |

## Plán B

- **API padne alebo DB nie je dostupná:** `docker compose up -d postgres`, reštart API, `python3 scripts/demo/reset-demo.py`. Ak ani to, ukázať `.http` súbory a pripravené snímky obrazovky.
- **Nejaký krok vráti 409/400:** dáta sú rozbité po neúspešnom pokuse, spustiť reset a zopakovať od kroku 2. Záložný zákazník Horváth má zmluvu a inštaláciu už hotové (kroky 2 až 6 preskočiť).
- **Frontend nefunguje:** všetko okrem kroku 1 ide cez API, takže stačí Swagger alebo `curl`.
- **Monitoring a detekcia (kroky 9 a 10), keď reset výroby zlyhá** (chýba internet alebo ML služba): spustiť reset s `--no-production` a ukázať samotný simulátor (offline, bez siete, trvá cca 2 s):
  ```bash
  cd ml && uv run python -m solarops_ml.simulator --location alice_springs \
    --start 2024-06-20 --end 2024-06-21 --seed 42 \
    --fault power_drop,2024-06-20T02:00,2024-06-20T05:00,0.5 \
    --out data/sim/alice.json --labels-out data/sim/alice_labels.csv
  ```
  Výstup je JSON s dávkami po 5 minútach (kontrakt ADR-185) a CSV s labelmi poruchy.

## Čo scenár nepokrýva (zhrnutie medzier)

1. Živá výroba: telemetria pribúda len pri resete, nie priebežne. Alarm je „prebiehajúci“ voči dňu resetu, preto reset spustiť deň pred obhajobou alebo v ten deň.
2. Kroky 2 až 7 sú v tabuľke ako volania API. Frontend má od Sprintu 3 obrazovky aj pre návrhy, ponuky, zmluvy, žiadosti o pripojenie, inštalácie, tikety a kalendár výjazdov, ale prejdenie týchto krokov klikaním zatiaľ nebolo overené.

## Skúška nanečisto

Dátum: **2026-10-28**, lokálny stack (API `dotnet run`, PostgreSQL 16 v dockeri), skript `reset-demo.py` + volania `curl` pod používateľom Admin. Kroky 2 a 3 boli spustené priamo, 4 až 7 v jednom behu.

| # | Krok | Skutočný výsledok |
|---|---|---|
| 0 | Prihlásenie | OK: `POST /api/auth/login` 200, token. UI prihlásenie nebolo klikané (len API). |
| 1 | Zákazník a odberné miesto | OK cez API (`GET /api/customers`, `…/supply-points`). Vykreslenie vo frontende nebolo overené. |
| 2 | Obhliadka | OK: 200, `SiteSurveyDone`. |
| 3 | Parametre návrhu | OK: 200, `InDesign`. |
| 4 | Preview a verzia | OK: preview 200 (DC 9 kWp, AC 10 kW, 8 690,88 kWh/rok, zdroj `simple-v1`), verzia 201 (číslo 1). |
| 5 | Ponuka | OK: vytvorenie 201 `Draft`, `send` 200 `Sent` (platnosť do +30 dní), `accept` 200 `Accepted`. |
| 6 | Zmluva | OK: vytvorenie 201 (`ZM-2026-000006`), `sign` 200 `Signed` s `installationId`, záloha `pay` 200. Audit má záznamy `Created` a `Signed`. |
| 7 | Žiadosť o pripojenie | OK čiastočne: vytvorenie 201 (`Draft`). `POST …/submit` vracia 404, odoslanie chýba. |
| 8 | Inštalácia a zariadenia (Novák) | OK: pripravuje reset (prechody až po `Commissioned`, technik, harmonogram, 3 zariadenia); `GET /api/installations/{id}` 200. |
| 9 | Monitoring | CHÝBA: `GET /api/telemetry`, `/api/monitoring`, `/api/ingest`, `/api/installations/x/telemetry` vracajú 404. Simulátor funguje: vygeneroval 226 kB JSON (seed 42, porucha `power_drop`). |
| 10 | Detekcia | CHÝBA: `/api/anomalies`, `/api/alerts` vracajú 404. |
| 11 | Tiket | CHÝBA: `/api/tickets` vracia 404. |

Idempotencia skriptu: `reset-demo.py` spustený dvakrát po sebe dal rovnaký stav (3 zákazníci, 3 komponenty, 3 zariadenia, 2 zmluvy, 2 inštalácie, rovnaké stavy entít). Líši sa len poradové číslo zmluvy (`ZM-2026-000002` → `000004`), lebo číslovanie zmlúv je sekvencia, ktorú reset nevracia. Technický účet `technik@demo.solarops.local` sa nemaže, len sa znova použije. Beh s `DEMO_DB_URL` na cudzí host skončil chybou „refusing to run“ (kód 1).

Poznámky z nanečisto: pred prvým úspešným behom skript zlyhal na prechode do `Mounting`, ktorý vyžaduje harmonogram a technika. Skript teraz zakladá technika a harmonogram sám. Na živom začiatku (Kováčová) preto po skúške treba pred ostrým demom znova spustiť reset, lebo skúška ju presunula do hotovej zmluvy.

## Kontrola v Azure (2026-11-08)

Kontrola len na čítanie (bez zmien dát) na nasadenom API po resete s výrobou (#454). Overené volaniami API, nie klikaním v UI.

| Čo | Výsledok |
|---|---|
| Prihlásenie rolí Admin, Obchod, Projektant, Technik, Operátor, Zákazník (Novák) | OK, všetky 200 |
| Zákazníci | OK, 11 demo zákazníkov vrátane Kováčovej a Horvátha |
| Alarm podvýroby (Operátor) | OK, 1 prebiehajúci, závažnosť Medium, medián 55 % predikcie |
| Tikety (Operátor) | OK, 5 tiketov, medzi nimi „Podvýroba: FVE 9,9 kWp Novak (demo)“ v stave `New` s pôvodom `AnomalyDetected` |
| Tikety v portáli zákazníka (Novák) | OK, vidí len svoje (2) |
| Inštalácie (Technik) | OK, 8 |
| Studený štart | API aj web majú `minReplicas 0`: prvá požiadavka po nečinnosti trvá 20 až 30 s. Pred obhajobou nastaviť 1 repliku (`infra/README.md`). |

Celé prejdenie scenára klikaním vo frontende (kroky 0 až 11) treba urobiť pred obhajobou ručne. Kroky 2 až 7 menia dáta Kováčovej, po skúške preto znova spustiť reset.
