---
name: solarops-azure
description: Use for any Azure, deployment, CI/CD, Docker or hosting work in SolarOps (Sprint 4 issues #157–#169, #277–#280). Encodes the architecture decided in ADR-211/213/215/218 — Container Apps for API and ML, Static Web Apps for React, PostgreSQL Flexible Server — and the cost and secret rules.
---

# Azure nasadenie SolarOps

Záväzné rozhodnutia sú v ADR: `docs/adr/spike-211-azure-hosting.md` (hosting), `spike-213-databaza.md`, `spike-215-azure-naklady.md` (náklady), `spike-218-autentifikacia.md` (cross-site auth) a `spike-196-ml-integracia.md` (ML služba). Pred zmenou si ich prečítaj; ak ticket odporuje ADR, ADR vyhráva a rozpor zapíš do PR.

## Cieľová architektúra

| Časť | Azure služba | Poznámky |
|---|---|---|
| `src/SolarOps.Api` (.NET 10) | Container Apps (Consumption), verejný ingress | `minReplicas = 0`; pred obhajobou 1 kvôli cold startu |
| `ml/` (FastAPI) | Container Apps, **interný** ingress | volá ho len API, timeout 15 s (#196) |
| `frontend/` (React + Vite) | Static Web Apps, plán Free | bez linked backendu, SPA volá API priamo cez `VITE_API_URL` |
| Databáza | PostgreSQL Flexible Server B1ms | NIE Azure SQL (issue #158 je zastaraný) |
| Obrazy | GHCR alebo ACR Basic | zatiaľ nerozhodnuté |
| Logy a monitoring | Log Analytics / Application Insights | #167, #168 |

Frontend je **React**, nie Angular (zastarané issues #165 a #277).

## Pravidlá

- **Infra ako kód** (#157), cez Bicep alebo `azd`; žiadne ručné klikanie, ktoré by sa nedalo zopakovať. Nič nenasadzuj a nevytváraj v Azure bez výslovného pokynu a bez potvrdenia subscription, resource group a regiónu.
- **Náklady:** študentský kredit. Preferuj scale-to-zero a najmenšie SKU. Pred vytvorením zdroja uveď odhad ceny a over limity Azure for Students (regióny, kvóty Container Apps; v ADR označené ako neoverené). Rozpočet a upozornenia sú #169.
- **Tajomstvá:** v Key Vault (#159) alebo ako secrets Container Apps. V gite len `.env.example`. Mimo Development musí byť nastavené `Jwt__SigningKey` (min. 32 bajtov) a `ConnectionStrings__Postgres`, inak API nenaštartuje.
- **CORS:** API musí povoliť origin Static Web Apps (#279). Auth je bearer token, nie cookies (cross-site, ADR-218).
- **SPA routing:** `staticwebapp.config.json` s `navigationFallback` na `/index.html` a bezpečnostnými hlavičkami (#278).
- **Schéma DB:** v Sprinte 1 je `EnsureCreated`; pred nasadením do Azure treba iniciálnu EF migráciu (samostatný PR, #158 prepísať na PostgreSQL). Nepúšťaj `EnsureCreated` proti zdieľanej produkčnej DB.
- **CI/CD:** GitHub Actions v `.github/workflows/` (`backend.yml`, `frontend.yml`, `ml.yml` už existujú, spúšťajú sa podľa `paths`). Deploy workflow (#161, #162, #280) pridávaj ako samostatné súbory; v nich používaj OIDC prihlásenie do Azure, nie dlhodobé kľúče v secrets. Zmeny CI a Docker konfigurácie len na výslovné zadanie.
- **Kontajnery:** viacstupňové Dockerfiles (`src/SolarOps.Api/Dockerfile`, `ml/Dockerfile`), non-root používateľ, bez tajomstiev v obraze. Lokálne celý stack: `docker compose --profile app up --build`.

## Postup pri úlohe

1. Prečítaj issue a príslušné ADR; zisti, či zdroj už existuje (`az`/`azd` len na čítanie).
2. Navrhni zmenu a odhad nákladov, počkaj na potvrdenie.
3. Over lokálne (`docker compose`, `dotnet build`, `pnpm build`, `uv run pytest`) pred akýmkoľvek nasadením.
4. Zaznamenaj otvorené otázky (kvóty, regióny, cold start, náklady) do tela PR sekcie „Na zváženie“.
