# Autorizácia endpointov podľa rolí (#136)

Tento dokument je záväzná matica **politika → roly → endpointy**. Kód, z ktorého matica vzniká, je
`src/SolarOps.Api/Infrastructure/Identity/AuthorizationPolicies.cs`; testy
(`AuthorizationPolicyTests`, `AuthorizationEndpointsTests`) overujú, že dokumentácia a endpointy s ním sedia.

## Ako to funguje

- Každý endpoint, ktorý nie je verejný, používa **pomenovanú politiku** z `AuthorizationPolicies`.
  Moduly nesmú vypisovať roly priamo na endpointe — inak by sa matica rozišla s kódom.
- **Neprihlásený** používateľ dostane `401 Unauthorized`.
- **Prihlásený bez práva** dostane `403 Forbidden`.
- `AuthorizationPolicies.Configure` registruje politiky. Zámerne **nie je fallback politika**: tá by sa
  uplatnila aj na neexistujúce cesty a z `404` by spravila `401`. Namiesto toho test endpointov vyžaduje,
  aby každý `/api` endpoint mal práve jednu politiku (alebo bol v povolených anonymných skupinách).
- Neznáma cesta (napr. `/api/does-not-exist`) preto vždy vráti `404`, aj neprihlásenému používateľovi.
- Roly sú konštanty v `Infrastructure/Identity/Roles.cs`: `Admin`, `Sales` (Obchod), `Designer`
  (Projektant), `Technician` (Technik), `Operator` (Operátor), `Customer` (Zákazník).

### Verejné endpointy

`AllowAnonymous` majú iba tieto skupiny; test to vynucuje:

| Skupina | Endpointy | Dôvod |
|---|---|---|
| Prihlásenie | `/api/auth/register`, `/api/auth/login`, `/api/auth/refresh`, `/api/auth/logout`, `/api/auth/forgot-password`, `/api/auth/reset-password` | prístup pred prihlásením, obnova hesla |
| Info o API | `/api/system/info` | verzia a prostredie pre frontend |
| Prevádzka | `/health`, OpenAPI a Scalar (len v Development) | monitoring a dokumentácia API |

## Matica politík

Čítanie znamená `GET`; zápis je `POST`, `PUT` alebo `DELETE` podľa endpointu.

| Politika | Povolené roly | Endpointy |
|---|---|---|
| `AdminOnly` | Admin | `GET /api/roles`, `GET /api/users`, `GET /api/users/{id}/roles`, `PUT /api/users/{id}/roles` |
| `CustomersRead` | Admin, Sales, Designer, Technician, Operator | `GET /api/customers`, `GET /api/customers/{id}`, `GET /api/customers/{customerId}/supply-points`, `GET /api/customers/{customerId}/supply-points/{id}` |
| `CustomersWrite` | Admin, Sales | `POST /api/customers`, `PUT /api/customers/{id}`, `POST /api/customers/{id}/archive`, `POST /api/customers/{id}/reactivate`, `POST/PUT/DELETE /api/customers/{customerId}/supply-points/…` |
| `CatalogRead` | Admin, Sales, Designer, Operator | `GET /api/components`, `GET /api/components/{id}` |
| `CatalogWrite` | Admin, Sales | `POST /api/components`, `PUT /api/components/{id}`, `POST /api/components/{id}/activate`, `POST /api/components/{id}/deactivate` |
| `QuotesRead` | Admin, Sales, Designer, Operator | `GET /api/quotes`, `GET /api/quotes/{id}`, `GET /api/quotes/{id}/history` |
| `QuotesWrite` | Admin, Sales | `POST /api/quotes`, `POST /api/quotes/{id}/send`, `POST /api/quotes/{id}/accept`, `POST /api/quotes/{id}/reject` |
| `ContractsRead` | Admin, Sales, Designer, Operator | `GET /api/contracts`, `GET /api/contracts/{id}`, `GET /api/contracts/{id}/audit` |
| `ContractsWrite` | Admin, Sales | `POST /api/contracts`, `POST /api/contracts/{id}/sign`, `POST /api/contracts/{id}/deposit/pay` |
| `DesignsRead` | Admin, Sales, Designer | `GET /api/designs`, `GET /api/designs/{id}`, `GET /api/designs/{id}/versions`, `GET /api/designs/{id}/versions/{number}`, `POST /api/designs` (založenie návrhu patrí obchodnému toku) |
| `DesignsWrite` | Admin, Designer | `POST /api/designs/{id}/site-survey`, `POST /api/designs/{id}/mark-infeasible`, `PUT /api/designs/{id}/parameters`, `POST /api/designs/{id}/configuration/preview`, `POST /api/designs/{id}/versions` |
| `InstallationsRead` | Admin, Sales, Designer, Technician, Operator, Customer | `GET /api/installations`, `GET /api/installations/{id}`, `GET /api/installations/{installationId}/devices…` (všetky GET vetvy) |
| `InstallationsPlan` | Admin, Sales, Designer, Operator | `POST /api/installations`, `PUT /api/installations/{id}`, `PUT /api/installations/{id}/schedule`, `PUT /api/installations/{id}/technicians` |
| `InstallationsField` | Admin, Designer, Technician, Operator | `POST /api/installations/{id}/transitions`, `POST /api/installations/{installationId}/devices`, `POST /api/installations/{installationId}/devices/{deviceId}/replace` |
| `ConnectionRequestsRead` | Admin, Sales, Designer, Technician, Operator, Customer | `GET /api/connection-requests`, `GET /api/connection-requests/{id}`, `GET /api/connection-requests/{id}/attachments/{attachmentId}` |
| `ConnectionRequestsWrite` | Admin, Sales, Operator | `POST /api/connection-requests`, `PUT /api/connection-requests/{id}` |

`Customer` je v `InstallationsRead` a `ConnectionRequestsRead`, ale vidí **len vlastné záznamy**:
obmedzenie rieši `ICustomerScope` (#137) vo vnútri dotazu, nie autorizačná politika. Politika rozhoduje,
či rola vôbec smie na endpoint; scope rozhoduje, ktoré riadky vráti.

## Ako pridať alebo zmeniť endpoint

1. Ak existuje vhodná politika, použi ju: `.RequireAuthorization(AuthorizationPolicies.QuotesRead)`.
2. Ak je potrebná nová politika, pridaj konštantu a záznam do `AuthorizationPolicies.Matrix`, sem do
   matice a do testov. Politika bez záznamu v matici alebo bez endpointu test neprejde.
3. Verejný endpoint musí mať `.AllowAnonymous()`; testy povoľujú anonymne len skupiny z tabuľky vyššie.
4. Nová rola patrí do `Roles.All` a musí mať pokrytie v matici.

## Testy

| Test | Čo overuje |
|---|---|
| `Unit/Identity/AuthorizationPolicyTests` | každá politika je zaregistrovaná s rolami z matice, konštanty sedia s maticou, dokumentácia obsahuje každú politiku, fallback nie je nastavený (kvôli `404`) |
| `Integration/Security/AuthorizationEndpointsTests` | každý `/api` endpoint je anonymný (len povolené skupiny) alebo má práve jednu politiku z matice; každá politika je použitá; pre každú politiku `401` bez tokenu, zakázaná rola `403`, povolené roly nie `401/403` |

Integračné testy potrebujú Docker (Testcontainers, PostgreSQL 16); v CI ich spúšťa `backend` workflow.

## Súvisiace dokumenty

- [ADR spike #207: Roly a workflow](../adr/spike-207-roly-a-workflow.md) — pôvodný návrh rolí a ich oprávnení.
- [ADR spike #218: Autentifikácia](../adr/spike-218-autentifikacia.md) — JWT, refresh token a cross-site obmedzenia.
