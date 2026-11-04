# Checklist odovzdania (story #192, audit k 2026-10-28)

Audit je len na čítanie (bez zmien kódu). Metódy: `git grep` cez všetkých 127 commitov (všetky vetvy), kontrola aktuálnych súborov, `.gitignore`, workflow súborov, `gh` (issues, milestones, runs, secrets, deployments). Hodnoty tajomstiev sa nevypisujú, len miesto.

Súhrn pre AC #192: **(1) povinné výstupy: nesplnené** (Sprint 4 je takmer celý otvorený), **(2) deploy v Azure beží: nepotvrdené, podľa dôkazov nenasadené**, **(3) tajomstvá nie sú v repozitári: splnené pre reálne tajomstvá**, ostáva jedna hygienická výhrada (vývojový JWT kľúč, repo je verejné).

## 1. Povinné výstupy

Poznámka: formálne zadanie (text predmetu) nie je v repe. Zoznam vychádza z epicu #13/#57, otvorených issues Sprintu 4, README a poznámky Sprint 0 (tam je len „formát odovzdania" ako bod agendy a výstup BPMN `.bpmn` + PNG/PDF). Ak má predmet ďalšie požiadavky, treba ich doplniť.

Stav issues: 203 otvorených z 279; Sprint 0: 4 otvorené (26 zatvorených), Sprint 1: 24 otvorených, Sprint 2: 44, Sprint 3: 91, Sprint 4: 40 otvorených (1 zatvorené). Deadline Sprintu 0 je dnes (2026-10-28), Sprint 4 2027-01-27.

| Položka | Stav | Dôkaz |
|---|---|---|
| README s návodom na spustenie (#179) | Čiastočne: README existuje (stack, lokálne spustenie, testy), issue #179 je otvorené | `README.md`; chýba produkčný/Azure návod |
| BPMN modely `.bpmn` + PNG/PDF (#205, #186) | Čiastočne: v `main` (HEAD 5b40bd7) sú PNG, `.drawio`, PDF a README; samotné `.bpmn` súbory v `main` NIE SÚ. Boli len v lokálnom nesledovanom adresári `docs/bpmn/bpmn/`, ktorý počas auditu zmizol z pracovného stromu. #186 otvorené | `docs/bpmn/png/`, `docs/bpmn/solarops-bpmn.drawio`, `docs/bpmn/solarops-bpmn.drawio.pdf`, `docs/bpmn/README.md`; `git ls-files docs/bpmn` neobsahuje `*.bpmn` |
| Mapovanie BPMN na entity (#69) | Splnené (súbor existuje, issue zatvorené) | `docs/bpmn/mapovanie-entit.md` |
| ADR zo spikov | Splnené | `docs/adr/` (24 ADR vrátane `spike-211-azure-hosting.md`); v poznámke Sprint 0 je úloha „presunúť ADR do docs/adr" nezaškrtnutá, ale ADR v repe sú |
| ERD / dátový model (#60, #213) | Neoverené: #60 zatvorené, ale súbor ERD som v `docs/` nenašiel podľa názvu | `docs/adr/spike-213-databaza.md`; overiť, kde je diagram |
| Slovník pojmov domény (#59) | Nesplnené | issue #59 otvorené (Sprint 0) |
| Analýza požiadaviek a doménový model (#14) | Nesplnené | issue #14 otvorené |
| Spike #185 (reálne vs. simulované dáta) | Nesplnené v trackeri (ADR `spike-185-telemetria.md` existuje) | issue #185 otvorené, treba zatvoriť/odôvodniť |
| Architektúra Clean Architecture (#181) | Nesplnené | issue #181 otvorené; v repe nie je samostatný dokument |
| API dokumentácia (#182) | Čiastočne: OpenAPI/Scalar sa generuje, publikovanie otvorené | `frontend/openapi/openapi.json`, README; #182 otvorené |
| Dokumentácia ML modelov a metrík (#184) | Nesplnené | #184 otvorené; `ml/README.md` existuje, ADR `spike-191`, `spike-193` |
| Azure infraštruktúra ako kód (#157) | Nesplnené | v repe nie je `infra/`, `*.bicep`, `azure.yaml`; #157, #158, #159 otvorené |
| CI pipeline (#160) | Splnené v praxi (build/test), issue otvorené | `.github/workflows/backend.yml`, `frontend.yml`, `ml.yml`; posledné runy `main` úspešné |
| CD pipeline API a ML (#161, #162, #163, #164) | Nesplnené | žiadny workflow nenasadzuje API ani ML; issues otvorené |
| Deploy frontendu (#277, #278, #279, #165, #166) | Nesplnené | workflow `frontend-deploy.yml` existuje, ale nasadzovanie sa preskakuje (viď sekcia Deploy); chýba `frontend/staticwebapp.config.json` (#278); #277 až #279 otvorené |
| Monitoring, health-checky, rozpočet (#167, #168, #169) | Nesplnené | issues otvorené |
| Akceptačné a E2E testy (#54, #175, #176, #177, #275, #276) | Nesplnené | issues otvorené |
| Technická dokumentácia (#55), dokumentácia ML a BPMN (#56) | Nesplnené | features otvorené |
| Demo scenár a dáta (#188) | Nesplnené | issue otvorené |
| Prezentácia max. 15 snímok (#190) | Nesplnené | issue otvorené, v repe nie je |
| Backlog Sprintu 1 až 3 (funkcionalita aplikácie) | Nesplnené | Sprint 1: 24, Sprint 2: 44, Sprint 3: 91 otvorených issues (`gh issue list --state open`) |
| Tajomstvá nie sú v repe (AC #192) | Splnené (s výhradou) | sekcia 2 |
| Deploy v Azure beží (AC #192) | Nepotvrdené, pravdepodobne nesplnené | sekcia 3 |

## 2. Tajomstvá

Rozsah: všetky commity všetkých vetiev (`git rev-list --all`, 127 commitov), vzory: PEM súkromné kľúče, AWS/GitHub/Slack/OpenAI tokeny, JWT tokeny, Azure Storage/SAS/Service Bus kľúče, `Password=`/`Pwd=` v connection stringoch, JSON kľúče `*Key/*Secret/*Password/*Token`, a zoznam všetkých kedy pridaných súborov (`.env`, `.pem`, `.pfx`, `.key`, `.tfstate`, dumpy). Vylúčené `pnpm-lock.yaml`, `docs/bpmn`, OpenAPI.

Repozitár `takacmichal/AISPU` je **verejný**. GitHub secret scanning aj push protection sú zapnuté, otvorených alertov je 0.

| Závažnosť | Nález | Miesto | Odporúčanie |
|---|---|---|---|
| Nízka | Lokálne dev heslo databázy (`solarops`) v connection stringu, rovnaké ako docker-compose predvolené. Je to zámerná lokálna hodnota, nie reálne tajomstvo. | `src/SolarOps.Api/appsettings.Development.json:10`, `.env.example:5`, `docker-compose.yml` (predvolené `${POSTGRES_PASSWORD:-solarops}`) | Žiadna akcia; v Azure nesmie byť použité. Heslo Azure PostgreSQL generovať nové. |
| Nízka | Vývojový JWT podpisový kľúč (označený „local-development-only") v gite. Pri verejnom repe by ktokoľvek vedel podpísať tokeny, ak by API v Azure omylom bežalo s `ASPNETCORE_ENVIRONMENT=Development` alebo bez prepísania. | `src/SolarOps.Api/appsettings.Development.json:16` | Pri nasadení vždy nastaviť `ASPNETCORE_ENVIRONMENT=Production` a `Jwt__SigningKey` zo secret store (Key Vault/secret Container Apps). Zvážiť presun dev kľúča do `dotnet user-secrets` alebo generovanie pri štarte. `docker-compose.yml` spúšťa API s `ASPNETCORE_ENVIRONMENT: Development`, preto ho nepoužívať ako Azure predlohu. |
| Info | `appsettings.json` má `Jwt:SigningKey` a `ConnectionStrings:Postgres` prázdne; podľa README API mimo Development bez nich nenaštartuje (fail-fast). | `src/SolarOps.Api/appsettings.json` | V poriadku. |
| Info | Žiadne PEM kľúče, cloudové/GitHub tokeny, SAS, Azure kľúče, `.env`, `.tfstate`, certifikáty ani dumpy v aktuálnom strome ani v histórii. | celá história | V poriadku. |
| Info | `.gitignore` ignoruje `.env`, `.env.*` (okrem `.env.example`), `*.local`, `bin/`, `obj/`, `.claude/settings.local.json`, `ml/data/`. Skompilované `bin/.../appsettings.*.json` nie sú sledované (ignorované). | `.gitignore` | V poriadku. |
| Info | Workflow `frontend-deploy.yml` používa `secrets.AZURE_STATIC_WEB_APPS_API_TOKEN` a `secrets.GITHUB_TOKEN`, žiadne zapísané hodnoty. `permissions` sú minimálne (`contents: read`, `pull-requests: write`), PR z forkov sa nenasadzujú. `backend.yml`, `frontend.yml`, `ml.yml` secrets nepoužívajú. | `.github/workflows/` | V poriadku. Pri pridaní CD pre API/ML použiť OIDC (podľa `.claude/skills/solarops-azure/SKILL.md`), nie dlhodobé kľúče. |
| Info | Nastavené repository secrets/variables: `gh secret list` a `gh variable list` vrátili prázdny výsledok (žiadny `AZURE_STATIC_WEB_APPS_API_TOKEN`, žiadny `VITE_API_URL`). Prostredia `production` a `preview-pr-*` existujú. | GitHub nastavenia | Súvisí s nasadením, viď sekcia 3. |
| Info | Dependabot security updates sú vypnuté; `npm audit`/`dotnet list package --vulnerable` v tomto audite neboli spustené. | GitHub nastavenia | Zapnúť, spustiť audity pred odovzdaním. |

Záver: reálne tajomstvá sa v repe ani v histórii nenašli. Prepísanie histórie nie je potrebné. Heuristické vzory nemusia zachytiť všetko (napr. nezvyčajne formátované heslá), preto odporúčam pred odovzdaním pustiť aj gitleaks/trufflehog.

## 3. Deploy v Azure

Zistenia:

- Cieľ podľa ADR `docs/adr/spike-211-azure-hosting.md` (stav „Navrhnuté"): Container Apps (API + ML), Static Web Apps Free (React), PostgreSQL Flexible Server B1ms. Verejná URL nie je nikde v repe uvedená (hľadané `azurestaticapps`, `azurecontainerapps`, `azurewebsites`), preto som nemal čo overiť cez `curl`.
- V repe nie je infraštruktúra ako kód (`infra/`, Bicep, `azd`) ani workflow na nasadenie API a ML. Existuje len `.github/workflows/frontend-deploy.yml`.
- Posledné `frontend-deploy` runy na `main`: 1 z posledných 8 skončil `success` (run 40664652057, 2026-10-28), ostatné `cancelled` (2 až 3 s). **Úspešný „production" run nasadenie reálne preskočil**: log hlási „deployment_token was not provided … Build will not be uploaded", pretože workflow nastavuje `SKIP_DEPLOY_ON_MISSING_SECRETS: true` a secret `AZURE_STATIC_WEB_APPS_API_TOKEN` nie je nastavený. Zelený run teda nie je dôkaz nasadenia. GitHub tiež eviduje „deployments" pre `production` a `preview-pr-*`, ale ide o záznamy prostredí workflow, nie o dôkaz bežiacej aplikácie.
- `frontend/staticwebapp.config.json` (SPA fallback a hlavičky, #278) neexistuje, `VITE_API_URL` variable nie je nastavená.
- Prístup do Azure (subscription, `az`) som nepoužil, preto **nie je overené**, či nejaké Azure zdroje existujú.

Záver: AC „Deploy v Azure beží" **nie je splnené / nedá sa potvrdiť**. Nič nenasvedčuje tomu, že API, ML alebo SPA v Azure bežia.

Čo treba na splnenie: vytvoriť Azure zdroje (#157, #158), nastaviť secret `AZURE_STATIC_WEB_APPS_API_TOKEN` a variable `VITE_API_URL`, pridať CD pre API a ML (#161 až #164), CORS (#279), potom overiť `curl https://<url>/health` a otvoriť SPA v prehliadači.

## 4. Blokery odovzdania

1. **Azure nasadenie neexistuje (kritický)**: žiadne IaC, žiadne CD pre API/ML, SPA workflow nasadenie preskakuje (chýba secret). AC #192 bod 2 nesplnený. Issues #157 až #169, #277 až #280.
2. **Sprint 4 je takmer celý otvorený (kritický)**: 40 otvorených (E2E testy #175, #176, #275, #276, #177; dokumentácia #179, #181, #182, #184, #186; demo #188; prezentácia #190). #192 sa nedá uzavrieť pred nimi.
3. **Rozsiahly otvorený backlog funkcionality**: Sprint 1 až 3 majú spolu 159 otvorených issues; nie je jasné, ktoré sú povinné pre odovzdanie, treba to potvrdiť s tímom a zadaním.
4. **Sprint 0 nezavretý (deadline dnes)**: #2, #14, #59, #185 otvorené.
5. **Chýbajúce formálne zadanie v repe**: zoznam „povinných výstupov" je odvodený; treba ho potvrdiť podľa zadania predmetu.
6. **BPMN `.bpmn` súbory nie sú v gite**: výstup #205 (`.bpmn` + PNG/PDF) je v `main` len ako PNG/drawio/PDF; `.bpmn` (L1, L2-61 až L2-68) boli počas auditu len nesledované lokálne a následne zmizli z pracovného stromu. Obnoviť a commitnúť (alebo vedome ostať pri drawio). Pracovný strom sa počas auditu menil (iný proces), stav je k 2026-10-28. Tajomstvá: bez blokerov, len odporúčania zo sekcie 2.

## Čo nebolo skontrolované

Skutočný stav Azure (žiadny prístup), obsah GitHub UI mimo `gh`, `npm audit`/`dotnet list package --vulnerable`/`pip-audit`, špecializované skenery (gitleaks, trufflehog), binárne súbory v histórii (`-I` ich vynechal), obsah zatvorených PR/komentárov, formálne znenie zadania predmetu.
