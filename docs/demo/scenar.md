# Scenár dema SolarOps

Issue #188. Scenár je postavený len na tom, čo API a frontend reálne vedia (stav k 2026-10-28). Čo chýba, je označené **„zatiaľ nie je implementované“** a v demo sa ukazuje cez plán B (slajd, simulátor, BPMN).

## Predpoklady a príprava

- Lokálny stack: `docker compose up -d postgres`, `dotnet run --project src/SolarOps.Api` (http://localhost:5080), `cd frontend && pnpm dev` (http://localhost:5173).
- Prihlásenie: `demo@solarops.local` / `DemoPass12345` (rola Admin, smie všetko). Technik: `technik@demo.solarops.local` / `DemoTech12345`.
- Obnova dát (pred každým prejdením): `python3 scripts/demo/reset-demo.py`. Skript zmaže len demo riadky (zákazníci `*@demo.solarops.local`, komponenty `DEMO-*`) a znova ich vytvorí cez API. Odmietne bežať, ak DB alebo API nie je na localhoste. Nastavenia cez env: `DEMO_API_URL`, `DEMO_DB_URL`, `DEMO_EMAIL`, `DEMO_PASSWORD`, `DEMO_TECH_PASSWORD`. Beh trvá cca 3 s.
- Ukazovanie API: Swagger/OpenAPI `http://localhost:5080/openapi/v1.json`, alebo `.http` súbory v `src/SolarOps.Api/Features/*/`.

## Pripravené dáta

| Zákazník | Stav po resete | Použitie |
|---|---|---|
| Ján Novák (`novak@…`) | návrh s verziou, prijatá ponuka, podpísaná zmluva so zálohou, inštalácia `Commissioned`, 1 striedač + 2 panely, žiadosť o pripojenie `Draft` | „hotový“ zákazník: pripojenie, zariadenia, monitoring (plán B) |
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
| 7 | 1 min | Obchod | **Pripojenie:** `POST /api/connection-requests` (EIC, 10 kW, schéma a súhlas vlastníka ako prílohy). | 201, žiadosť v stave `Draft`. Odoslanie distribučke a ďalšie stavy: **zatiaľ nie je implementované**. | API (čiastočne) |
| 8 | 1 min | Technik | Na hotovom zákazníkovi Novák ukáže `GET /api/installations/<novak_installation>` a `…/devices`. | Stav `Commissioned`, história stavov, striedač a 2 panely so sériovými číslami. | API |
| 9 | 1 min | Moderátor | **Monitoring:** telemetria zo striedača. | **Zatiaľ nie je implementované** (API nemá príjem ani zobrazenie telemetrie). Plán B: simulátor `ml/` vygeneruje dávky podľa ADR-185. | chýba |
| 10 | 1 min | Moderátor | **Detekcia anomálií:** pokles výkonu. | **Zatiaľ nie je implementované** (`ml/` má len `/health`). Plán B: simulátor s injekciou poruchy, viď nižšie. | chýba |
| 11 | 1 min | Operátor | **Tiket:** vytvorenie servisného tiketu z alertu. | **Zatiaľ nie je implementované** (žiadny endpoint ani entita). Plán B: BPMN proces servisného tiketu v `docs/bpmn/`. | chýba |

## Plán B

- **API padne alebo DB nie je dostupná:** `docker compose up -d postgres`, reštart API, `python3 scripts/demo/reset-demo.py`. Ak ani to, ukázať `.http` súbory a pripravené snímky obrazovky.
- **Nejaký krok vráti 409/400:** dáta sú rozbité po neúspešnom pokuse, spustiť reset a zopakovať od kroku 2. Záložný zákazník Horváth má zmluvu a inštaláciu už hotové (kroky 2 až 6 preskočiť).
- **Frontend nefunguje:** všetko okrem kroku 1 ide cez API, takže stačí Swagger alebo `curl`.
- **Monitoring a detekcia (kroky 9 a 10):** ukážka simulátora (offline, bez siete, trvá cca 2 s):
  ```bash
  cd ml && uv run python -m solarops_ml.simulator --location alice_springs \
    --start 2024-06-20 --end 2024-06-21 --seed 42 \
    --fault power_drop,2024-06-20T02:00,2024-06-20T05:00,0.5 \
    --out data/sim/alice.json --labels-out data/sim/alice_labels.csv
  ```
  Výstup je JSON s dávkami po 5 minútach (kontrakt ADR-185) a CSV s labelmi poruchy. Do API sa zatiaľ nahrať nedá.
- **Tiket (krok 11):** ukázať proces v `docs/bpmn/` a povedať, že implementácia je v backlogu.

## Čo scenár nepokrýva (zhrnutie medzier)

1. Príjem a zobrazenie telemetrie (ingest endpoint, ukladanie, grafy).
2. Detekcia anomálií nad telemetriou (ML služba má len health check).
3. Tikety a alerty (entity, endpointy, UI).
4. Odoslanie žiadosti o pripojenie a jej stavy za `Draft` (`Submitted` až `Concluded` existujú v enume, ale chýba endpoint na prechod).
5. Frontend: hotová je len správa zákazníkov s odbernými miestami; ponuky, zmluvy a inštalácie sú zástupné stránky.

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
