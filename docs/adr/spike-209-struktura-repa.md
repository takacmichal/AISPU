# ADR-209: Štruktúra monorepa, CI, tajomstvá, docker-compose

- Stav: prijaté (Sprint 0)
- Dátum: 2026-10-15
- Issue: #209

> **Zmena v Sprinte 1:** frontend sa zmenil z Angular + PrimeNG na **React + Vite + TypeScript** (zmena rozsahu). Backend je jeden projekt `src/SolarOps.Api` (vertical slices) a testy sú v `tests/`, nie `src/SolarOps.Api.Tests`. Workflow pre API sa volá `backend.yml`. Aktuálny stav popisuje koreňový `README.md`.

> Stav k 2026-10-15. Overené položky majú zdroj, neoverené sú označené **[NEOVERENÉ]**. Spike nemenil žiadne súbory projektu.

## Kontext
Repo `takacmichal/AISPU`. Komponenty: .NET 10 API, Angular frontend, Python ML služba, dokumentácia, infra. 3 vývojári, ~3 mesiace. Cieľ: jedno repo, nezávislé CI podľa zmenených ciest, bez tajomstiev v gite. (Existujúci obsah repa som nekontroloval, štruktúra je návrh.)

## Možnosti

| Možnosť | Plus | Mínus |
|---|---|---|
| A. Monorepo, path-filter CI (zvolené) | jeden PR pre zmenu API + UI, jednoduché issues a board | treba udržiavať filtre, širšie oprávnenia |
| B. Repo na komponent | izolované CI | 3 ľudia, režijné náklady na koordináciu, cross-repo zmeny kontraktu |
| C. Monorepo s Nx/Turborepo | cache, graf závislostí | zbytočná zložitosť pre .NET + Python |

## Rozhodnutie
Monorepo (A). Návrh stromu:

```
AISPU/
├── .github/
│   ├── workflows/
│   │   ├── api.yml
│   │   ├── frontend.yml
│   │   ├── ml.yml
│   │   ├── infra.yml
│   │   └── docs.yml
│   ├── CODEOWNERS
│   └── pull_request_template.md
├── src/                      # .NET 10 (požiadavka zadania; alternatíva backend/)
│   ├── SolarOps.sln
│   ├── SolarOps.Api/
│   ├── SolarOps.Domain/
│   ├── SolarOps.Infrastructure/   # EF Core, migrácie
│   └── SolarOps.Api.Tests/
├── frontend/                 # Angular + PrimeNG
├── ml/                       # Python ML služba (pyproject.toml, app/, tests/)
├── docs/                     # ADR, bpmn/, architektúra
│   ├── adr/
│   └── bpmn/
├── infra/                    # Bicep, azd (azure.yaml), skripty
├── docker-compose.yml        # lokálny vývoj
├── docker-compose.override.example.yml
├── .env.example              # bez hodnôt tajomstiev
├── .editorconfig
├── .gitignore
├── .gitattributes
├── global.json               # pin .NET SDK
└── README.md
```

**Pomenovanie:** .NET projekty `SolarOps.*` (PascalCase), adresáre `kebab-case`, Python `snake_case`, vetvy `feature/<issue>-popis`, `fix/<issue>-popis`, commit v rozkazovacom spôsobe po anglicky.

**CI: path filter na úrovni triggeru** (každý workflow má vlastné `paths`, vrátane svojho workflow súboru). Náčrt:

```yaml
# .github/workflows/api.yml
name: api
on:
  push:
    branches: [main]
    paths: ['src/**', 'global.json', '.editorconfig', '.github/workflows/api.yml']
  pull_request:
    paths: ['src/**', 'global.json', '.editorconfig', '.github/workflows/api.yml']
# jobs: restore, build, test (Testcontainers potrebuje Docker, na ubuntu-latest je dostupný)

# .github/workflows/frontend.yml  -> paths: ['frontend/**', '.github/workflows/frontend.yml']
# .github/workflows/ml.yml        -> paths: ['ml/**', '.github/workflows/ml.yml']
# .github/workflows/infra.yml     -> paths: ['infra/**', '.github/workflows/infra.yml']
```

Pozor: ak sú CI kontroly *required* v branch protection, workflow preskočený cez `paths` zostane vo "Pending" a blokuje merge. Riešenie: buď required checks nenastavovať na path-filtrované workflowy, alebo použiť jeden workflow s `dorny/paths-filter` a jobom `ci-gate` ako jediným required checkom. Pre 3-člennú skupinu stačia nerequired kontroly + dohoda; gate job je upgrade.

**Spoločné konvencie:** `.editorconfig` v koreni (UTF-8, LF, 4 medzery pre C#/Python, 2 pre TS/YAML/JSON), `.gitattributes` s `* text=auto eol=lf`, jeden koreňový `.gitignore` s sekciami (bin/obj, node_modules, `__pycache__`, `.venv`, `.env`, `*.user`), README s rýchlym štartom (`cp .env.example .env`, `docker compose up`), odkazom na `docs/` a tabuľkou komponentov.

**Tajomstvá:**
- Lokálne: .NET `dotnet user-secrets` (API), `.env` pre docker-compose a Python (git-ignorovaný), v gite iba `.env.example`.
- CI: GitHub Actions secrets/variables; prihlásenie do Azure cez OIDC (federated credential), nie cez dlhodobé kľúče.
- Azure: App settings / Container Apps secrets, odkazy na Key Vault cez managed identity. Pozn.: Key Vault nie je uvedený v zozname bezplatných služieb pre študentov, ale štandardná cena je veľmi nízka (platí z kreditu $100) [NEOVERENÉ cenové detaily].
- Zapnúť GitHub secret scanning a push protection, ak je dostupné pre repo.

**docker-compose (lokálny vývoj):** služby `db` (postgres:17-alpine alebo zvolená verzia podľa ADR-213, volume, healthcheck), `api` (build `src/`, `depends_on: db: condition: service_healthy`), `ml` (build `ml/`), `frontend` (dev server alebo nginx), voliteľne `pgadmin` cez profile `tools`. Konfigurácia výhradne cez `${VAR}` z `.env`.

## Dôsledky
- (+) Atomické zmeny kontraktu API a UI, jeden board/issues, jednoduché onboardovanie.
- (-) Path filtre treba udržiavať a testovať; zmena v `docs/` nespúšťa build (zámer).
- (-) Spoločné zmeny (`global.json`, `.editorconfig`) spúšťajú viac workflowov.
- Voľba názvu `src/` pre .NET je odchýlka od "backend/"; prijatá, lebo ju určilo zadanie.

## Zdroje
- Azure for Students (kredit $100, always-free služby, Container Apps/App Service): https://azure.microsoft.com/en-us/free/students
- Testcontainers for .NET (PostgreSQL modul): https://dotnet.testcontainers.org/modules/postgres/
- GitHub Actions `on.<push|pull_request>.paths` a poznámka o required checks: dokumentácia GitHub Docs (nepreverené cez fetch v tomto spike) [NEOVERENÉ URL, správanie je všeobecne známe].

## Nadväzujúce issues
- Založiť kostru adresárov, `.editorconfig`, `.gitignore`, `.gitattributes`, `global.json`, README.
- Workflowy `api.yml`, `frontend.yml`, `ml.yml` s path filtrami; rozhodnúť o gate jobe.
- `docker-compose.yml` + `.env.example` + healthchecky.
- Azure OIDC pre GitHub Actions + Key Vault/App settings (spolu s `infra/`).
- Zapnúť branch protection na `main` (PR + review).
