# AISPU

ASP.NET Core Web API (.NET 10) postavené na princípoch Clean Architecture.

## Štruktúra projektu

```
AISPU/
├── AISPU.sln
├── src/
│   ├── AISPU.Api/              # Prezentačná vrstva – ASP.NET Core Web API (controllers, Program.cs)
│   ├── AISPU.Application/      # Aplikačná logika – use cases, rozhrania, DTO
│   ├── AISPU.Domain/           # Doménové entity a pravidlá (bez závislostí)
│   └── AISPU.Infrastructure/   # Implementácie – databáza, externé služby
└── tests/
    └── AISPU.UnitTests/        # xUnit testy pre Domain a Application
```

### Závislosti medzi vrstvami

```
Api ──► Application ──► Domain
 └────► Infrastructure ──► Application
```

Domain nezávisí od ničoho, Application iba od Domain. Infrastructure implementuje rozhrania z Application.

## Spustenie

```bash
dotnet build
dotnet run --project src/AISPU.Api
dotnet test
```
