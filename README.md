# SolarOps

Informačný systém firmy, ktorá predáva, inštaluje a servisuje fotovoltické elektrárne (FVE). Školský tímový projekt (AISPU).

Backlog: [Issues](../../issues) a [GitHub Project](https://github.com/users/takacmichal/projects/1). Architektonické rozhodnutia: [docs/adr](docs/adr/README.md).

## Stack

| Časť | Technológia | Priečinok |
|---|---|---|
| Backend | .NET 10, ASP.NET Core minimal API, EF Core 10 + Npgsql | `src/SolarOps.Api` |
| Testy backendu | xUnit, WebApplicationFactory, Testcontainers (PostgreSQL) | `tests/SolarOps.Api.Tests` |
| Frontend | **React** + Vite + TypeScript (pnpm) – od Sprintu 1 namiesto Angularu | `frontend/` |
| ML služba | Python 3.12 (uv), FastAPI, pytest, ruff | `ml/` |
| Databáza | PostgreSQL 16 | `docker-compose.yml` |
| Dokumentácia | ADR, BPMN | `docs/` |

## Lokálne spustenie

Potrebné: .NET SDK 10, Node 22 + pnpm, [uv](https://docs.astral.sh/uv/), Docker.

```bash
# 1. databáza
cp .env.example .env              # voliteľné; predvolené hodnoty zodpovedajú appsettings.Development.json
docker compose up -d postgres

# 2. API (http://localhost:5080, Scalar UI: /scalar/v1, OpenAPI: /openapi/v1.json)
dotnet run --project src/SolarOps.Api
#   v Development sa schéma vytvorí automaticky (EnsureCreated)
#   EnsureCreated nič nerobí, ak DB už tabuľky má: po pull-e PR s novými entitami
#   zmaž lokálnu DB: docker compose down -v && docker compose up -d postgres

# 3. frontend (http://localhost:5173)
cd frontend
cp .env.example .env.local        # VITE_API_URL=http://localhost:5080
pnpm install
pnpm dev

# 4. ML služba (http://localhost:8000/health)
cd ml
uv python install 3.12
uv run uvicorn solarops_ml.api:app --reload --port 8000
```

Celý stack v kontajneroch: `docker compose --profile app up --build`.

Connection string pre `dotnet run` je v `src/SolarOps.Api/appsettings.Development.json` (len lokálne vývojové heslo). Ak potrebuješ iný, použi `dotnet user-secrets` alebo premennú `ConnectionStrings__Postgres`, nie commit.

JWT podpisový kľúč (`Jwt:SigningKey`, min. 32 bajtov) je v gite iba ako lokálny vývojový kľúč v `appsettings.Development.json`. Mimo Development ho nastav cez premennú `Jwt__SigningKey` (napr. `openssl rand -base64 48`), inak API nenaštartuje.

## Testy

```bash
dotnet build                      # src/ musí byť bez warningov (TreatWarningsAsErrors)
dotnet test                       # integračné testy si spustia PostgreSQL cez Testcontainers (beží Docker)
cd frontend && pnpm lint && pnpm build && pnpm test
cd ml && uv run ruff check . && uv run pytest
```

CI (`.github/workflows/backend.yml`, `frontend.yml`, `ml.yml`) sa spúšťa podľa zmenených ciest na každom PR (aj na stacked PR) a na push do `main`.

## Backend: modulový vzor (pre feature PR)

API je jeden projekt rozdelený na vertikálne moduly v `src/SolarOps.Api/Features/<Modul>/`. **Feature PR nemení `Program.cs`, `AppDbContext` ani CI.** Vzorový modul: `Features/System/SystemModule.cs`.

1. Vytvor triedu implementujúcu `IModule` (verejný bezparametrický konštruktor). Pri štarte ju nájde reflexia:

   ```csharp
   public sealed class ProjectsModule : IModule
   {
       public void AddServices(IServiceCollection services, IConfiguration configuration)
       {
           services.AddScoped<IProjectService, ProjectService>();
       }

       public void MapEndpoints(IEndpointRouteBuilder endpoints)
       {
           var group = endpoints.MapGroup("/api/projects").WithTags("Projects");
           group.MapPost("/", CreateAsync).WithValidation<CreateProjectRequest>();
       }
   }
   ```

2. Entity a ich EF konfigurácia (`IEntityTypeConfiguration<T>`) patria do modulu. `AppDbContext` ich načíta cez `ApplyConfigurationsFromAssembly`; k dátam pristupuj cez `db.Set<T>()`. Tabuľky a stĺpce sú automaticky `snake_case`, kľúče `Guid` (`Guid.CreateVersion7()`), čas `DateTimeOffset` v UTC (`TimeProvider` je v DI).
3. Chyby: `DomainException` → 409, `NotFoundException` → 404, `RequestValidationException` → 400, `UnauthorizedAccessException` → 401, všetko ako ProblemDetails. Vstupné DTO validuj cez DataAnnotations a `.WithValidation<T>()`.
4. Aktuálny používateľ: `ICurrentUser` (`UserId`, `Roles`, `IsInRole`, `RequireUserId()`); dotazy na dáta používateľa vždy filtruj podľa neho. Názvy rolí sú v `Infrastructure/Identity/Roles`. Autentifikáciu (Identity + JWT Bearer, `/api/auth/*`) zabezpečuje modul `Features/Auth` (#132); v autorizačných politikách nenastavuj natvrdo schému, aby testy mohli použiť testovaciu.
5. Enumy sa serializujú ako reťazce. Stavové prechody sú metódy na entite, ktoré pri neplatnom prechode hodia `DomainException`.
6. Nový NuGet balík: verzia patrí do `Directory.Packages.props` (balíky Sprintu 1 sú tam už pripravené), do `.csproj` modulu pridaj len `<PackageReference Include="..." />` bez verzie.
7. Integračné testy: `[Collection(PostgresCollection.Name)]`, konštruktor s `PostgresFixture`, `new ApiFactory(postgres)` (každá factory má vlastnú databázu), `factory.CreateApiClient()` (anonymný) alebo `factory.CreateApiClient(userId, Roles.Sales)` (prihlásený cez testovaciu schému `TestAuthHandler`, funguje aj po pridaní JWT v #132), seed cez `factory.ExecuteDbAsync(db => ...)`. Unit testy patria do `tests/SolarOps.Api.Tests/Unit/<Modul>/`, integračné do `Integration/<Modul>/`.

**Žiadne EF migrácie vo feature PR.** V Sprinte 1 sa schéma vytvára cez `EnsureCreated` (dev aj testy). Iniciálna migrácia vznikne samostatným PR po merge Sprintu 1.

## Pravidlá PR a merge

- 1 issue = 1 PR, najprv ako **draft**. Vetva `sprint1/<NN>-<issue>-<slug>`, commit správy po anglicky v rozkazovacom spôsobe.
- **Stacked PR:** foundation PR (`sprint1/00-foundation`) sa merguje **prvý**, všetky stacky Sprintu 1 sú postavené na ňom. Každý PR v stacku má base = predchádzajúca vetva.
- Mergovať **merge commitom (nie squash)**, zdola nahor. Ak sa predsa použije squash, ďalší PR v stacku treba prebázovať: `git rebase --onto main <stará-base>`.
- Pred pushom musí na vetve prejsť `dotnet build` + `dotnet test` (resp. `pnpm build`, `uv run pytest`).
- Žiadne tajomstvá v gite: lokálne `dotnet user-secrets` / `.env` (ignorovaný), v gite len `.env.example`.
