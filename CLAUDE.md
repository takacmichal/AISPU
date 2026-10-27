# SolarOps (AISPU)

Informačný systém firmy, ktorá predáva, inštaluje a servisuje fotovoltické elektrárne (FVE). Školský tímový projekt. Podrobnosti sú v `README.md`, rozhodnutia v `docs/adr/`, procesy v `docs/bpmn/`.

## Jazyk

- Kód, identifikátory, commity a názvy PR: po anglicky.
- Telo PR, ADR, README, BPMN a texty pre používateľov: po slovensky.
- Doménové pojmy (Obchod, Projektant, Technik, Zákazník, odberné miesto, distribučka) sú v kóde anglicky, napr. `Roles.Sales`, `SupplyPoint`. Slovník pojmov je v `docs/bpmn/mapovanie-entit.md`.

## Štruktúra

| Časť | Priečinok | Technológia |
|---|---|---|
| Backend | `src/SolarOps.Api` | .NET 10, ASP.NET Core minimal API, EF Core 10 + Npgsql, PostgreSQL 16 |
| Testy backendu | `tests/SolarOps.Api.Tests` | xUnit, WebApplicationFactory, Testcontainers (treba bežiaci Docker) |
| Frontend | `frontend/` | React 19 + Vite + TypeScript, pnpm, Vitest + Testing Library. **Nie Angular** (staré issues ho ešte spomínajú) |
| ML a simulátor | `ml/` | Python 3.12, uv, FastAPI, pytest, ruff |

## Príkazy

```bash
dotnet build                                   # src/ musí byť bez warningov (TreatWarningsAsErrors)
dotnet test                                    # integračné testy si spustia PostgreSQL cez Testcontainers
cd frontend && pnpm lint && pnpm build && pnpm test
cd ml && uv run ruff check . && uv run ruff format --check . && uv run pytest
docker compose up -d postgres                  # lokálna DB; po pull-e s novými entitami: docker compose down -v
```

## Backend: pravidlá modulov

API je jeden projekt rozdelený na vertikálne moduly v `src/SolarOps.Api/Features/<Modul>/`. Vzorový modul je `Features/System/SystemModule.cs`, postup je v skille `solarops-feature-module`.

- Feature PR **nemení** `Program.cs`, `AppDbContext` ani CI. Modul sa nájde reflexiou cez `IModule`.
- **Žiadne EF migrácie vo feature PR.** Schéma sa vytvára cez `EnsureCreated`; iniciálna migrácia príde samostatným PR po Sprinte 1.
- Kľúče `Guid` (`Guid.CreateVersion7()`), čas `DateTimeOffset` v UTC cez `TimeProvider`, tabuľky a stĺpce `snake_case`, enumy ako reťazce.
- Stavové prechody sú metódy na entite, neplatný prechod hodí `DomainException` (409). `NotFoundException` je 404, `RequestValidationException` 400, výsledok je ProblemDetails.
- Vstup validuj DataAnnotations a `.WithValidation<T>()`.
- Dáta používateľa vždy filtruj cez `ICurrentUser`; zákazník smie vidieť len svoje (`CustomerScope`), cudzí záznam vracia 404 rovnako ako neexistujúci.
- Role sú konštanty v `Infrastructure/Identity/Roles` (Admin, Sales, Designer, Technician, Operator, Customer). V politikách nenastavuj natvrdo autentifikačnú schému.
- Nový NuGet balík: verzia patrí do `Directory.Packages.props`, `.csproj` má `PackageReference` bez verzie. Bez opýtania nepridávaj závislosti.
- Moduly majú `.http` súbor s ukážkovými requestmi ako susedné moduly.

## Testy

- TDD: najprv zlyhávajúci test, potom kód.
- Unit testy: `tests/SolarOps.Api.Tests/Unit/<Modul>/`. Integračné: `Integration/<Modul>/` s `[Collection(PostgresCollection.Name)]`, `PostgresFixture`, `new ApiFactory(postgres)`, `factory.CreateApiClient(userId, Roles.Sales)` a seedom cez `factory.ExecuteDbAsync(...)`.
- Pri každom endpointe testuj aj 401 (bez prihlásenia), 403 (zlá rola) a prístup zákazníka k cudzím dátam.
- Testy sú deterministické: žiadna sieť, pevné hodiny a seed.

## Git a PR

- 1 issue = 1 PR, najprv **draft**. Vetva `sprint1/<NN>-<issue>-<slug>`, base `main`.
- Commit a názov PR po anglicky v rozkazovacom spôsobe s číslom issue, napr. `Add panel and inverter registry with serial numbers (#83)`.
- Telo PR po slovensky v sekciách **Zmeny / Na zváženie / Testy** a riadok `Closes #N`.
- Merge commitom (nie squash). Pred pushom prejde `dotnet build` + `dotnet test` (resp. `pnpm build`, `uv run pytest`).
- Necommituj ani nepushuj bez výslovnej požiadavky. Nikdy force push na `main`.
- Žiadne tajomstvá v gite: lokálne `dotnet user-secrets` alebo `.env` (ignorovaný), v gite len `.env.example`.

## ML a simulátor

- Dataset a počasie: ADR-178 (DKASC Alice Springs) a ADR-180 (Open-Meteo). Telemetria: ADR-185, injekcia porúch: ADR-187.
- Simulátor patrí do `ml/src/solarops_ml/simulator/` a generuje JSON podľa kontraktu z ADR-185 (`schemaVersion "1.0"`, dávky po najviac 288 meraní po 5 minút).
- Reprodukovateľnosť: každý generátor a model berie `seed`.

## Pri práci

- Pred novým kódom si prečítaj susedný modul a ADR, ktorý sa témy týka. Drž sa jeho štýlu.
- Malé, cielené zmeny. Refaktoring a premenovanie mimo zadania nerob.
- Ak ticket odporuje ADR, ADR vyhráva; rozpor zapíš do sekcie „Na zváženie“ v PR.
