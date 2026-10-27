---
name: solarops-dotnet-api
description: Use when writing or reviewing .NET code in SolarOps beyond the module skeleton — minimal API endpoint metadata and OpenAPI, ProblemDetails errors, EF Core 10 queries (paging, projections, N+1, concurrency), time handling and C# style. Complements solarops-feature-module.
---

# .NET 10 API a EF Core v SolarOps

Kostra modulu je v skille `solarops-feature-module`. Tu sú detaily kódu. Vzor hľadaj v `Features/Customers`, `Features/Quotes`, `Features/Installations`.

## Endpointy (minimal API)

- Skupina: `endpoints.MapGroup("/api/<kolekcia>").WithTags("<Modul>")`. Kolekcie v množnom čísle, kebab-case (`/api/connection-requests`).
- Každý endpoint má `.RequireAuthorization(...)` s rolami z `Roles.*`, vstupné DTO s `.WithValidation<T>()` a typované odpovede (`Results.Created(location, dto)`, `Results.Ok`, `Results.NoContent`). Použi `.Produces<T>()` / `.ProducesProblem(...)` pre 400/401/403/404/409, aby OpenAPI (`/openapi/v1.json`, Scalar na `/scalar/v1`) zodpovedalo skutočnosti; z neho sa generuje TypeScript klient (ADR-225).
- Chyby: hoď `DomainException` (409), `NotFoundException` (404), `RequestValidationException` (400); mapovanie na ProblemDetails je centrálne, nerob vlastné `try/catch` v endpointoch.
- DTO sú `record` s DataAnnotations; entity nikdy nevracaj z API. Enumy sa serializujú ako reťazce.
- Každá nová trasa dostane request v `.http` súbore modulu.

## EF Core

- Čítania: `AsNoTracking()` a projekcia do DTO cez `Select`; žiadne `ToList()` pred `Where`/`OrderBy`/`Skip`/`Take`.
- Zoznamy vždy stránkované (predvolene strana 1, veľkosť 20, max 100; vyšší limit = 400). Pri vyhľadávaní porovnávaj bez ohľadu na veľkosť písmen na strane DB.
- N+1: používaj `Include` alebo projekciu, nie dopyty v cykle. Skontroluj aj pri pridávaní kolekcií do odpovedí.
- Zápisy: jeden `SaveChangesAsync` na jednu operáciu; viac krokov, ktoré musia prejsť spolu (výmena zariadenia, odobratie roly), v jednej transakcii. Pri pravidlách typu „posledný admin“ použi `Serializable`.
- Súbežnosť: unikátne pravidlá (sériové číslo, číslo verzie) vynúť unikátnym indexom a `DbUpdateException` s kódom 23505 mapuj na 409.
- Čas: `TimeProvider` z DI, hodnoty `DateTimeOffset` v UTC; nikdy `DateTime.Now`/`UtcNow` priamo.
- Kľúče `Guid.CreateVersion7()`. Žiadne EF migrácie vo feature PR (`EnsureCreated`).
- Časové rady telemetrie navrhuj podľa ADR-220, nie ako bežné entity.

## Štýl C#

- Nullable reference types sú zapnuté, `TreatWarningsAsErrors` platí pre `src/`: žiadne warningy ani potlačenia bez dôvodu. `.editorconfig`: 4 medzery pre `.cs`, LF.
- XML dokumentačné komentáre (`/// <summary>`) na verejných typoch modulov v štýle susedných súborov; chybný `<param>` pri zapnutých warningoch zhodí build.
- Preferuj `sealed` triedy, primárne konštruktory a `IReadOnlyList<T>` vo verejných kontraktoch podľa okolitého kódu.
- Žiadne tajomstvá v kóde; konfigurácia cez `IOptions<T>` s predvolenými hodnotami v kóde alebo `appsettings.json` (nemeň ho bez zadania).

## Pred dokončením

`dotnet build` bez warningov a `dotnet test` (Docker beží) so zobrazeným výstupom.
