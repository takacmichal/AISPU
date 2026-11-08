# ADR #277: Revízia nasadenia v Azure (región, hosting frontendu, joby, admin)

- **Stav:** Prijaté
- **Dátum:** 2026-11-07, doplnené 2026-11-08 po prvom nasadení
- **Issue:** #277 (nadradené #51, #11)
- **Mení:** ADR #211 (hosting frontendu, región), ADR #215 (región a odhad nákladov)
- **Súvisí:** #157 (infra ako kód), #158 (PostgreSQL a migrácie), #159 (tajomstvá), #161 a #162 (CD), #168 (úlohy a health-checky), #169 (rozpočet), #278 a #279 (SPA routing, CORS), #380 (poplatok za prostredie)

## Kontext

ADR #211 počíta so Static Web Apps (SWA) pre React SPA a ADR #215 s cenami regiónu West Europe. Pri príprave nasadenia sa na predplatnom **Azure for Students** ukázalo:

- Politika „Allowed resource deployment regions“ povoľuje len `swedencentral`, `switzerlandnorth`, `polandcentral`, `uaenorth` a `italynorth`. **West Europe nie je povolený.**
- `Microsoft.Web/staticSites` je v Európe dostupné len vo West Europe. Validácia šablóny so SWA Free (`az deployment group validate`) skončila chybou `RequestDisallowedByAzure`. **SWA sa na tomto predplatnom nedá vytvoriť.**
- Container Apps, Key Vault a Application Insights sú dostupné vo všetkých povolených regiónoch. PostgreSQL Flexible Server síce zoznam SKU (`list-skus`) uvádza všade, pri vytvorení však v niektorých regiónoch zlyhá (pozri Doplnenie).
- Aplikácia pobeží v Azure len **4 až 5 dní** okolo obhajoby, potom sa resource group zmaže.

Ďalej treba vyriešiť veci, ktoré ADR #211 nechal otvorené: ako sa v Azure aplikujú EF migrácie (v kóde bežia len v Development), ako vznikne prvý administrátor a čo s úlohami na pozadí pri škálovaní na nulu.

## Možnosti (hosting frontendu)

| Možnosť | Výhody | Nevýhody |
|---|---|---|
| A. **nginx kontajner v Container Apps** | rovnaký model ako API a ML, HTTPS a škálovanie na nulu, plná kontrola nad hlavičkami a SPA fallbackom | ďalší obraz a kontajnerová aplikácia |
| B. SPA servírované z API (`wwwroot`) | jedna doména, netreba CORS | mení `Program.cs` a Dockerfile API, frontend a backend sa nasadzujú spolu |
| C. Storage static website | najlacnejšie | bez vlastných hlavičiek (CSP), fallback len cez chybový dokument s kódom 404 |
| D. SWA v inom regióne | pôvodný plán | na predplatnom zakázané |

## Rozhodnutie

### Región
Všetky zdroje idú do **Poland Central** (`polandcentral`), okrem databázy, ktorá je v **Italy North** (pozri Doplnenie).

### Frontend: možnosť A
- Samostatná aplikácia v tom istom prostredí Container Apps s verejným ingressom. Obraz obsahuje `vite build` a nginx (multi-stage, non-root).
- `frontend/nginx/default.conf.template` preberá pravidlá zo `staticwebapp.config.json`: fallback na `/index.html` okrem `/assets/*`, hlavičky CSP, `X-Content-Type-Options`, `X-Frame-Options` a `Referrer-Policy`. Súbory v `/assets/` majú dlhú cache, `index.html` sa vždy overuje. CSP `connect-src` obsahuje premennú `API_ORIGIN`, ktorú nginx dosadí pri štarte; v Azure je to presná adresa API.
- URL API ostáva zapečená pri builde (`VITE_API_URL`). Máme jedno prostredie, preto sa runtime `config.json` z #279 nerobí.
- CORS: `Cors__AllowedOrigins__0` je verejná adresa frontendovej aplikácie. Bez nastaveného `API_ORIGIN` povoľuje CSP `https://*.azurecontainerapps.io`.
- `frontend-deploy.yml` (SWA) sa nahradí deployom obrazu do Container Apps.

### Migrácie: Container Apps Job
- EF migrácie spúšťa **manuálne spúšťaný Container Apps Job**. CD pipeline ho spustí a počká na úspech pred nasadením novej revízie API. Pri chybe sa API nenasadí.
- Databáza ostáva zatvorená pre internet, GitHub Actions sa k nej nepripája.
- API v Azure migrácie pri štarte nespúšťa (ako doteraz mimo Development).

### Úlohy na pozadí: Container Apps Jobs
- Hodinová ML analýza a obnova predpovede počasia bežia ako **plánované Container Apps Jobs** (cron), nie ako hosted services v API. Pri `minReplicas = 0` by inak nebežali.
- V API sa v Azure vypnú cez existujúce prepínače `Ml__AnalysisEnabled=false` a `Weather__Enabled=false`.
- Spôsob spustenia (navrhované, upresní implementácia): rovnaký obraz API s argumentom príkazového riadka, ktorý vykoná jeden beh a skončí. Použije tak rovnakú DI a pripojenie k DB a nepotrebuje ďalšie prihlasovanie.

### Prvý administrátor
- E-mail administrátora je konštanta v kóde. Heslo sa číta z `Admin__Password`: v Azure z tajomstva Container Apps alebo Key Vault (#159), lokálne z user-secrets.
- Bez nastaveného hesla sa admin nevytvorí a API to zapíše do logu. Repozitár je verejný, preto heslo nikdy nie je v gite.

### Obrazy, náklady a rozpočet
- Obrazy v GHCR (ADR #215), nie v ACR.
- Rozpočet `solarops-budget` je vytvorený (2026-11-07): 30 USD mesačne na celé predplatné, upozornenia pri 50, 75, 90 a 100 % skutočných nákladov a pri 100 % prognózy, adresát je rola Owner. Tým je overené, že budgety na Azure for Students fungujú (bod z ADR #215).

Odhad na 5 dní v Poland Central (Azure Retail Prices API, 2026-11-07, USD):

| Položka | Cena | 5 dní |
|---|---|---|
| Environment Management Hour (ak sa účtuje, #380) | 0,10 USD/h | 12,00 |
| PostgreSQL Flexible B1ms, výpočet (Italy North, rovnaká cena) | 0,0199 USD/h | 2,39 |
| PostgreSQL úložisko 32 GB | podľa cenníka | < 1 |
| Container Apps (API, ML, frontend, joby) | vCPU 0,000024 USD/s aktívne, 0,000003 USD/s nečinné; mesačná bezplatná kvóta | 0 až 3 |
| **Spolu (horší prípad)** | | **približne 15 až 20** |

### Doplnenie 2026-11-08: prvé nasadenie
Prvý beh `infra/deploy.sh` (#157) odhalil dve veci, ktoré validácia šablóny nezachytí:

- **Prostredie Container Apps v režime Express.** Ak šablóna neurčí režim, Azure nové prostredie vytvorí ako *Express* (`environmentMode: Express`). Express nepodporuje joby, referencie na Key Vault, interné mená aplikácií (`http://solarops-ml`) ani nešifrované HTTP medzi aplikáciami, teda všetko, na čom stojí toto ADR. Šablóna preto nastavuje `environmentMode: 'WorkloadProfiles'` s jediným profilom Consumption (API `2026-07-01`). Platí sa ďalej len za použitie a škálovanie na nulu ostáva. Existujúce prostredie sa dá prepnúť na mieste, trvá to okolo 20 minút.
- **PostgreSQL mimo Poland Central.** Burstable `Standard_B1ms` aj `Standard_B2s` vracia predplatnému Azure for Students v Poland Central (vo všetkých troch zónach) aj v Sweden Central chybu `SkuNotAvailable`. V **Italy North** sa B1ms vytvoril. Databáza je preto v Italy North (`postgresLocation` v `main.bicepparam`), ostatné zdroje ostávajú v Poland Central. Názov servera obsahuje región, lebo neúspešný názov ostáva v pôvodnom regióne istý čas rezervovaný.

Dôsledky doplnenia: dotazy z API do databázy idú cez dva regióny (rádovo +20 ms na dotaz). Cena B1ms je v oboch regiónoch rovnaká. Firewall pravidlo „Allow Azure services“ funguje aj medzi regiónmi.

## Dôsledky

- (+) Nasadenie je na predplatnom vôbec možné; všetky časti bežia v jednom prostredí Container Apps.
- (+) Migrácie a úlohy na pozadí nezávisia od toho, či API práve beží.
- (-) Pribúda obraz frontendu a jeho deploy; `staticwebapp.config.json` a jeho test nahradila nginx šablóna a jej test.
- (-) Pribúdajú tri joby (migrácie, ML analýza, počasie), ktoré treba zahrnúť do #157.
- Treba upraviť issues #51, #277, #278, #279 (SWA na Container Apps) a #158 (PostgreSQL namiesto Azure SQL).
- ADR #211 a #215 platia ďalej pre API, ML, databázu a úspory. Hosting frontendu a región sa riadia týmto ADR.

## Riziká a neoverené

- **Environment Management Hour (#380):** cenník ho od 2026-11-01 vedie ako samostatnú položku prostredia vedľa súkromných endpointov a plánovanej údržby. Či ho platí aj čisté Consumption prostredie, potvrdí až Cost Analysis po prvom dni.
- Rate limiting prihlásenia za proxy Container Apps: adresa proxy nie je v `ForwardedHeaders:KnownProxies`, takže všetci klienti môžu zdieľať jeden limit. Zatiaľ sa nerieši, treba sledovať pri deme.
- Cold start frontendu, API a ML pri `minReplicas = 0`. Pred obhajobou nastaviť 1.
- Požiadavky zadania (frontend volá oba backendy, dve SQL databázy) môžu zmeniť ingress ML služby a pridať druhú databázu. Rieši sa samostatne.

## Zdroje

1. Politika regiónov predplatného: `az policy assignment list` (Allowed resource deployment regions), 2026-11-07.
2. Dostupnosť služieb podľa regiónu: `az provider show -n <provider> --query "resourceTypes[?resourceType=='<typ>'].locations"`, 2026-11-07.
3. Validácia SWA vo West Europe: `az deployment group validate`, chyba `RequestDisallowedByAzure`, 2026-11-07.
4. Azure Retail Prices API, región `polandcentral`: https://prices.azure.com/api/retail/prices
5. Container Apps Jobs: https://learn.microsoft.com/en-us/azure/container-apps/jobs
6. Container Apps: fakturácia: https://learn.microsoft.com/en-us/azure/container-apps/billing
7. Container Apps express: https://learn.microsoft.com/en-us/azure/container-apps/express-overview
8. `Microsoft.App/managedEnvironments`, vlastnosť `environmentMode`: https://learn.microsoft.com/en-us/azure/templates/microsoft.app/managedenvironments
9. PostgreSQL Flexible Server, chyby kapacity a SKU: https://learn.microsoft.com/azure/postgresql/flexible-server/how-to-resolve-capacity-errors
