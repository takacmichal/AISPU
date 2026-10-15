# ADR spike #225: Generovaný TypeScript klient z OpenAPI

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #225 (nadradené #10)
- **Súvisí:** #224 (grafy), #218 (autentifikácia, JWT), rozhodnutie o .NET 10 OpenAPI

## Zmena rozsahu

Issue počíta s **Angularom** (kandidáti ng-openapi-gen, Angular `HttpClient`, signály). Frontend je **React + Vite + TypeScript**, takže Angular-špecifické generátory (ng-openapi-gen, `HttpClient` šablóny NSwag) odpadajú a kritérium "signály" sa nahrádza "dobrá integrácia s fetch a React Query".

## Kontext

Jeden zdroj pravdy (OpenAPI z .NET) má znížiť nesúlad FE/BE. Backend používa vstavané `Microsoft.AspNetCore.OpenApi` (dokument na `/openapi/v1.json`, Scalar UI v Development). Overené v dokumentácii Microsoftu: .NET 10 generuje **OpenAPI 3.1** (predvolene) s JSON Schema 2020-12 a podporuje generovanie dokumentu pri builde (MSBuild `OpenApiGenerateDocumentsOptions`, balík `Microsoft.Extensions.ApiDescription.Server`).

## Zvažované možnosti

| Nástroj (npm k 2026-10-15) | Čo generuje | Posúdenie |
|---|---|---|
| **openapi-typescript 7.13.0 + openapi-fetch 0.17.0** (MIT) | iba typy `paths`/`components` (`.d.ts`), klient je 6 kB typovaný `fetch` wrapper bez generovaného runtime kódu | **vybrané**: minimum generovaného kódu, presné typy, middleware pre auth, voliteľne `openapi-react-query` 0.5.4 |
| Orval 8.43.0 (MIT) | generuje funkcie/hooky (React Query, axios/fetch) | funkčné, ale veľa generovaného kódu a konfigurácie |
| `@hey-api/openapi-ts` 0.99.0 (MIT) | generovaný SDK + typy | verzia pred 1.0 (nestabilné API) |
| openapi-generator (Java) | veľa jazykov, TS šablóny | ťažká závislosť (Java), objemný generovaný kód, slabšie typy |
| NSwag | C# generátor aj TS klient | zbytočný, keď nepoužívame Swashbuckle/NSwag na strane servera; šablóny orientované na Angular/axios |
| ng-openapi-gen | Angular služby | **nepoužiteľné** (Angular) |

## Rozhodnutie

**openapi-typescript + openapi-fetch**, generované z `/openapi/v1.json` (OpenAPI 3.1 z .NET 10).

Workflow (návrh):

1. **Zdroj:** dokument z backendu. Lokálne zo spusteného API (`/openapi/v1.json`) alebo zo súboru vygenerovaného pri builde API (`Microsoft.Extensions.ApiDescription.Server`); v CI sa použije súbor z buildu, aby CI nepotrebovalo bežiaci server.
2. **Generovanie:** skript vo `frontend/package.json` (napr. `gen:api`) spúšťa `openapi-typescript <zdroj> -o src/api/schema.d.ts`.
3. **Commitnúť vygenerovaný `schema.d.ts`** (jeden súbor, čitateľný diff pri zmene API; FE build nezávisí od bežiaceho BE).
4. **CI kontrola:** workflow `frontend.yml` regeneruje schému a zlyhá, ak `git diff --exit-code src/api/schema.d.ts` nie je prázdny (chytá zabudnuté pregenerovanie po zmene BE).
5. **Klient:** `createClient<paths>({ baseUrl })` jeden zdieľaný modul; JWT access token a obnova cez refresh token (#218) cez **middleware** openapi-fetch (pridanie `Authorization: Bearer`, pri 401 jednorazový refresh a opakovanie).
6. **Chyby:** ProblemDetails (RFC 7807) typovať z OpenAPI odpovedí (400/404/409); .NET endpointy musia deklarovať `Produces`/`ProducesProblem`, inak chýbajú typy chýb.
7. **TanStack Query** nie je súčasťou tohto rozhodnutia; ak ho tím zvolí, `openapi-react-query` nadväzuje na openapi-fetch.

## Dôsledky

- Typová bezpečnosť volaní bez generovaného runtime kódu a bez Java závislosti.
- Backend musí mať v OpenAPI úplné metadata (`WithName`/operationId, `Produces`, enumy ako reťazce), inak sú typy nepresné; tieto pravidlá patria do konvencií backendu.
- Iba typy: **žiadna runtime validácia** odpovedí (neoveruje, že server naozaj vráti dokumentované tvary).
- Commitnutý `schema.d.ts` môže spôsobiť konflikty pri paralelných PR (pregenerovať po rebase).

## Riziká / neoverené

- **Neoverené:** funkčný proof-of-concept na vzorovom endpointe (generovanie a build na tomto repe sa v spiku nespustili); podpora OpenAPI 3.1 z .NET 10 v openapi-typescript 7.x je podľa dokumentácie projektu, nepotvrdená lokálnym behom (riziko: špecifické konštrukcie 3.1 ako `nullable` cez `type: [..., "null"]` a `format` pre `Guid`/`DateTimeOffset` -> `string`).
- Neoverené: presný názov a správanie MSBuild generovania dokumentu v tomto repe (balík/vlastnosti zo stránky Microsoftu, nie z lokálneho behu).
- Verzie z npm registry k 2026-10-15; openapi-typescript 7.13.0 má peer `typescript ^5.x`, overiť s verziou TS vo Vite scaffolde.

## Zdroje

- ASP.NET Core OpenAPI (3.1, build-time generovanie): https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi?view=aspnetcore-10.0
- openapi-typescript / openapi-fetch / openapi-react-query: https://openapi-ts.dev/ , https://openapi-ts.dev/openapi-fetch/
- Orval: https://orval.dev/ ; Hey API: https://heyapi.dev/
- npm: https://www.npmjs.com/package/openapi-typescript , https://www.npmjs.com/package/openapi-fetch

## Nadväzujúce issues

1. PoC: skript `gen:api`, vygenerovaný `schema.d.ts` pre jeden vzorový endpoint a typovaný klient (`frontend/src/api/`).
2. CI: krok kontroly aktuálnosti schémy vo `frontend.yml`; build-time generovanie OpenAPI v backende.
3. Auth middleware (Bearer + refresh) pre openapi-fetch po implementácii #218.
4. Backend konvencie OpenAPI (operationId, `ProducesProblem`, enumy ako reťazce).
