# ADR spike #218: Autentifikácia (ASP.NET Core Identity + JWT vs. Microsoft Entra ID)

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #218 (nadradené #9)
- **Súvisí:** #207 (roly), #197 (pôvodný spike auth, Sprint 0), #211 (hosting)

## Kontext

SolarOps má externých používateľov (zákazníci, ktorí si pozerajú svoju elektráreň) aj interných (obchod, projektant, technik, operátor, admin). Backend je .NET 10 minimal API + PostgreSQL, frontend **React + Vite + TypeScript** (checklist v issue hovorí o Angulari a MSAL; rozsah sa zmenil na React, rozdiel je nižšie). Tím má 3 ľudí, projekt je školský a musí byť spustiteľný lokálne (docker-compose) aj bez Azure tenantu.

## Zvažované možnosti

| Kritérium | A. ASP.NET Core Identity + vlastné JWT | B. Microsoft Entra External ID (zákazníci) + Entra ID (zamestnanci) | C. `MapIdentityApi` (vstavané Identity API endpointy) |
|---|---|---|---|
| Lokálny vývoj a testy | plne offline, Testcontainers stačí | vyžaduje tenant a app registrácie, ťažko v CI | offline |
| Samoregistrácia zákazníkov | vlastný endpoint + e-mail potvrdenie (Identity to vie) | user flow v External ID (hotové) | áno |
| Správa rolí | `RoleManager`, roly v DB, claim `role` v JWT | app roles v Entra + mapovanie na naše roly | áno (Identity) |
| Cena | 0 EUR | External ID: prvých 50 000 MAU zdarma [3]; školský tenant by sme museli riešiť (zákazníci sú externí) | 0 EUR |
| Frontend (React) | vlastný auth modul: access token v pamäti, hlavička cez `openapi-fetch` middleware, obnova pri 401 | `msal-react` (hotové PKCE, obnova tokenov) | ako A |
| Refresh token | vlastná implementácia (rotácia, reuse detection) | rieši Entra | vstavané tokeny sú proprietárne, **nie štandardné JWT** [2], čo nesedí s požiadavkou na JWT |
| Zložitosť pre MVP | stredná (asi 3 endpointy + služba na tokeny) | stredná až vysoká (tenant, consent, sync rolí) | nízka |
| Závislosť na cudzej službe | žiadna | Entra (dostupnosť, zmeny produktu) | žiadna |

## Rozhodnutie

**Možnosť A: ASP.NET Core Identity (EF Core stores v PostgreSQL) + JWT Bearer access token + rotujúci refresh token.**

- **Access token:** JWT, platnosť **15 minút**, podpis symetrickým kľúčom z konfigurácie (`Jwt:Key` cez env/user-secrets, nikdy v gite). Claimy: `sub`, `email`, `role` (jeden alebo viac z `Admin`, `Sales`, `Designer`, `Technician`, `Operator`, `Customer`, pozri ADR #207). Overenie cez `Microsoft.AspNetCore.Authentication.JwtBearer`.
- **Refresh token:** náhodných 32+ bajtov, platnosť **7 dní**, rotuje sa pri každom použití. V DB sa ukladá **len SHA-256 hash** (nikdy samotný token), spolu s `UserId`, `ExpiresAt`, `RevokedAt`, `ReplacedByTokenHash`. Pri **odhlásení** sa token revokuje. Pri **reuse** už použitého (rotovaného) tokenu sa revokuje celá rodina tokenov používateľa (podozrenie na krádež).
- **Reset hesla:** Identity token (`GeneratePasswordResetTokenAsync`), e-mail cez rozhranie `IEmailSender`; vo vývoji implementácia, ktorá len zapíše do logu/konzoly.
- **Samoregistrácia:** len rola `Customer`; interné roly prideľuje `Admin`.
- Vstavané `MapIdentityApi` sa **nepoužije** (proprietárne tokeny, nie JWT); používame z Identity iba `UserManager`, `SignInManager` a `RoleManager`.
- **Entra ID zamietnuté:** zákazníci sú externí používatelia mimo školského tenantu, lokálny vývoj a CI by závisel od Azure, a rozdiel v cene to nerozhoduje (External ID je do 50 000 MAU zdarma), rozhoduje zložitosť a offline reprodukovateľnosť.

### Frontend (React namiesto Angulara z checklistu)

- Angular `MSAL` vs. interceptor sa prekladá na React: `msal-react` (možnosť B) vs. vlastný modul. Pri A: access token držať **len v pamäti**, hlavičku `Authorization: Bearer` pridať v middleware klienta `openapi-fetch` (ADR #225) a pri `401` jedenkrát zavolať refresh a zopakovať požiadavku.
- **Cookies vs. localStorage (návrh, finálne podľa implementácie #133):** HttpOnly cookie je pre refresh token bezpečnejšia voči XSS, ale frontend (Static Web Apps) a API (Container Apps) budú na rôznych doménach, čo je **cross-site** (`SameSite=None`, credentialed CORS; Safari blokuje cookies tretích strán). Preto pre MVP: refresh token v tele odpovede a v `sessionStorage`, access token v pamäti; prechod na HttpOnly cookie je možný, keď budú obe časti pod jednou registrovateľnou doménou (napr. `app.` a `api.` pod vlastnou doménou). Riziko XSS znižuje CSP a absencia `dangerouslySetInnerHTML`.

## Dôsledky

- (+) Plne lokálne a testovateľné (Testcontainers), bez externých závislostí; roly a používatelia v našej DB.
- (+) Jednoduché prepojenie na autorizáciu podľa rolí (#136) a obmedzenie zákazníka na vlastné dáta (#137).
- (-) Bezpečnostný kód (rotácia a reuse detection refresh tokenov) píšeme sami, treba ho pokryť testami (expirácia, reuse, revokácia).
- (-) Správa kľúča na podpis JWT (rotácia kľúča mimo rozsahu MVP). Neskoršia migrácia na Entra je možná, ak je `sub` a `role` kontrakt rovnaký.
- Implementácia **musí** dodržať: Identity + JwtBearer, 15 min / 7 dní, hash refresh tokenu, revokácia pri odhlásení a pri reuse, `IEmailSender`.

## Riziká a neoverené

- Presný formát transportu refresh tokenu (telo vs. cookie) nie je ešte implementovaný (**navrhované**).
- Názvy endpointov (`/api/auth/register`, `/login`, `/refresh`, `/logout`, `/forgot-password`, `/reset-password`) sú **navrhované**; implementácia v čase písania ADR neexistuje.
- Podmienky Entra External ID pre školský tenant a ich zmeny od októbra 2026 som neoveroval nad rámec FAQ [3] (**neoverené**).
- Nebol vytvorený prototyp autentifikácie; porovnanie je z dokumentácie, nie z merania. Timebox (6 h) nebol meraný.

## Zdroje

1. Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.13 na NuGet: https://www.nuget.org/packages/Microsoft.AspNetCore.Identity.EntityFrameworkCore
2. Identity API endpoints (tokeny "aren't standard JSON Web Tokens"): https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization
3. Microsoft Entra External ID FAQ (core ponuka zdarma pre prvých 50 000 MAU): https://learn.microsoft.com/en-us/entra/external-id/customers/faq-customers
4. Podpora .NET 10 (LTS do 14. 11. 2028): https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

## Nadväzujúce issues

- #132 Registrovať a prihlásiť používateľa, #133 Obnoviť token a odhlásiť sa, #134 Resetovať zabudnuté heslo, #135 Definovať roly a pridelenie rolí, #136 Chrániť endpointy podľa rolí, #137 Obmedziť zákazníka len na vlastné dáta (features #39, #40).
- Zastarané issues, ktoré treba upraviť (nevytvárané v tomto PR): #142 (Angular auth guard a JWT interceptor) a UI stories #227 až #229 (feature #226) po zmene rozsahu na React.
