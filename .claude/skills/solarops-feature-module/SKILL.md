---
name: solarops-feature-module
description: Use when adding or extending a backend feature in SolarOps (new endpoint, entity, service or module under src/SolarOps.Api/Features). Walks through the module pattern, DI, EF configuration, authorization, validation and tests used in this repo.
---

# Nový backend modul v SolarOps

Postup pre jeden issue = jeden PR. Pravidlá a príkazy sú v `CLAUDE.md` a `README.md` (sekcia „Backend: modulový vzor“).

## 1. Príprava

1. Prečítaj issue (`gh issue view <N>`), rodičovský feature a ADR, ktorý sa témy týka (`docs/adr/`, `docs/bpmn/mapovanie-entit.md`).
2. Otvor 1–2 existujúce moduly podobného druhu: `Features/Customers` (CRUD so zoznamom a stránkovaním), `Features/Quotes` (stavový workflow), `Features/Installations` (životný cyklus, plánovanie, zákaznícky scope).
3. Vetva: `sprint1/<NN>-<issue>-<slug>` z `main`.

## 2. Súbory modulu

V `src/SolarOps.Api/Features/<Modul>/`:

- `<Modul>Module.cs`: trieda `IModule` s verejným bezparametrickým konštruktorom. `AddServices` registruje služby, `MapEndpoints` namapuje skupinu `/api/<kolekcia>` s `.WithTags(...)`, `.RequireAuthorization(...)` a `.WithValidation<T>()`.
- `<Entita>.cs`: entita so súkromnými setrami; stavové prechody ako metódy, neplatný prechod hodí `DomainException`.
- `<Entita>Configuration.cs`: `IEntityTypeConfiguration<T>`; `AppDbContext` ju nájde cez `ApplyConfigurationsFromAssembly`. Unikátne indexy dávaj do DB, nie len do kontroly v kóde.
- `<Modul>Contracts.cs` (alebo `*Dtos.cs`): vstupné a výstupné DTO s DataAnnotations.
- `<Modul>Service.cs`: logika, `db.Set<T>()`, `TimeProvider`, `ICurrentUser`.
- `<Modul>.http`: ukážkové requesty.

Nemeň `Program.cs`, `AppDbContext` ani CI. Nerob EF migráciu.

## 3. Autorizácia a vlastníctvo dát

- Roly iba cez konštanty `Roles.*`. Zápis zvyčajne Admin plus rola, ktorá agendu vlastní (Sales, Designer, Technician, Operator); čítanie podľa ADR #207.
- Zákazník vidí iba svoje záznamy (`CustomerScope`); cudzí záznam = 404.
- Konkurenčné pravidlá (unikátnosť, „posledný admin“, dvojitá výmena) rieš unikátnym indexom alebo transakciou a duplicitu mapuj na 409.

## 4. Testy (TDD)

1. Najprv unit testy domény v `tests/SolarOps.Api.Tests/Unit/<Modul>/` a spusti ich: musia zlyhať.
2. Integračné testy v `Integration/<Modul>/` podľa README bodu 7. Pokry: úspech (201/200), validáciu (400), neexistujúci záznam (404), konflikt (409), 401, 403 a zákazníka s cudzími dátami.
3. `dotnet build` (0 warningov) a `dotnet test`; Docker musí bežať.

## 5. Dokončenie

- Rozpory medzi ticketom a ADR a otvorené otázky zapíš do tela PR.
- Draft PR s `Closes #N`. Postup commitov a PR je v `CLAUDE.md`.
