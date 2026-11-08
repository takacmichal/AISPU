# Azure infraštruktúra ako kód (Bicep)

Bicep šablóny pre prostredie SolarOps v Azure. Vytvárajú resource group a všetky runtime zdroje tak, aby sa dali znovu vytvoriť bez ručného klikania.

> **Rozpor so zadaním #157:** issue hovorí o App Service pláne a Azure SQL. Záväzné ADR [#211](../docs/adr/spike-211-azure-hosting.md) a [#213](../docs/adr/spike-213-databaza.md) určujú **Container Apps** a **PostgreSQL Flexible Server**. Šablóny sa riadia ADR; App Service ani Azure SQL sa nevytvárajú.

## Čo sa vytvorí

| Zdroj | Účel | SKU / nastavenie |
|---|---|---|
| Resource group `rg-solarops-{env}` | Spoločný kontajner zdrojov | `westeurope` |
| Log Analytics workspace | Logy prostredia Container Apps | PerGB2018, retencia 30 dní |
| Container Apps environment | Spoločné prostredie pre API a ML | Consumption |
| Container App `ca-solarops-api-{env}` | .NET 10 API | verejný ingress, port 8080, 0,25 vCPU / 0,5 GiB, 0–3 (dev) / 0–5 (prod) replík |
| Container App `ca-solarops-ml-{env}` | FastAPI ML služba | **interný** ingress, port 8000, 0,5 vCPU / 1 GiB, 0–3 (dev) / 0–5 (prod) replík |
| PostgreSQL Flexible Server `psql-solarops-{env}-{suffix}` | Databáza | Burstable B1ms, PostgreSQL 16, 32 GB, bez HA |
| Databáza `solarops` | Aplikačná schéma | UTF-8 |
| Static Web App `swa-solarops-{env}-{suffix}` | React SPA | plán Free |

Všetky zdroje majú tagy `project`, `environment`, `managedBy`, `owner`, `workload` a `costCenter`.

## Štruktúra

```text
infra/
├── main.bicep             # subscription scope: resource group + volanie resources.bicep
├── resources.bicep        # všetky zdroje v resource group
├── main.dev.bicepparam    # parametre pre dev
├── main.prod.bicepparam   # parametre pre prod/demo
└── README.md
```

## Predpoklady

- Azure CLI 2.90+ a Bicep CLI (`az bicep install`)
- Azure for Students subscription s dostupným kreditom
- `POSTGRES_ADMIN_PASSWORD` (min. 16 znakov) a `JWT_SIGNING_KEY` (min. 32 bajtov) v prostredí, nikdy v gite
- Potvrdená subscription, resource group a región pred akýmkoľvek deployom

## Parametre a tajomstvá

Tajomstvá sa neukladajú do `.bicepparam`; načítajú sa z premenných prostredia:

```bash
export POSTGRES_ADMIN_PASSWORD="$(openssl rand -base64 24)"
export JWT_SIGNING_KEY="$(openssl rand -base64 48)"
```

Hodnoty si ulož do správcu hesiel. Pri ďalšom deployi ich musíš použiť znova, inak sa zmení administrátorské heslo PostgreSQL a JWT kľúč (čím sa zneplatnia prihlásenia). Heslo PostgreSQL nesmie obsahovať `;`, pretože sa vkladá do connection stringu; hodnota z `openssl rand -base64` je bezpečná.

Ďalšie parametre v `main.*.bicepparam`:

| Parameter | Význam |
|---|---|
| `environment` | `dev` alebo `prod`; používa sa v názvoch a tagoch |
| `location` | región, predvolene `westeurope` |
| `namePrefix` | predvolene `solarops` |
| `nameSuffix` | hash subscription + environment pre globálne unikátne názvy |
| `postgresAdminLogin` | administrátor PostgreSQL, predvolene `solaropsadmin` |
| `postgresBackupRetentionDays` | 7 dní v dev, 14 v prod |
| `apiImage`, `mlImage` | obrazy kontajnerov; predvolene placeholder do prvého nasadenia |
| `apiMinReplicas`, `apiMaxReplicas` | škálovanie API; 0 znamená scale-to-zero |
| `mlMinReplicas`, `mlMaxReplicas` | škálovanie ML služby |
| `logRetentionDays` | retencia logov, predvolene 30 dní |

## Validácia bez Azure

```bash
az bicep build --file infra/main.bicep --stdout > /dev/null

POSTGRES_ADMIN_PASSWORD=local-test-password-123 \
JWT_SIGNING_KEY=local-test-signing-key-0123456789abcdef \
az bicep build-params --file infra/main.dev.bicepparam --stdout > /dev/null

POSTGRES_ADMIN_PASSWORD=local-test-password-123 \
JWT_SIGNING_KEY=local-test-signing-key-0123456789abcdef \
az bicep build-params --file infra/main.prod.bicepparam --stdout > /dev/null
```

`build-params` vypíše aj hodnoty tajomstiev, preto nikdy nepresmeruj jeho výstup s reálnymi heslami do súboru.

## Náhľad a nasadenie

Najprv over subscription a región. Nič sa nevytvorí, kým nespustíš `create`.

```bash
az login
az account show
az account set --subscription "<subscription-id>"

# Dev
az deployment sub validate \
  --name solarops-dev \
  --location westeurope \
  --parameters infra/main.dev.bicepparam

az deployment sub what-if \
  --name solarops-dev \
  --location westeurope \
  --parameters infra/main.dev.bicepparam

az deployment sub create \
  --name solarops-dev \
  --location westeurope \
  --parameters infra/main.dev.bicepparam

# Prod/demo
az deployment sub validate \
  --name solarops-prod \
  --location westeurope \
  --parameters infra/main.prod.bicepparam

az deployment sub what-if \
  --name solarops-prod \
  --location westeurope \
  --parameters infra/main.prod.bicepparam

az deployment sub create \
  --name solarops-prod \
  --location westeurope \
  --parameters infra/main.prod.bicepparam
```

Výstupy:

```bash
az deployment sub show --name solarops-dev --query properties.outputs -o json
```

Kľúčové výstupy: `apiUrl`, `staticWebAppHostname`, `staticWebAppName`, `postgresFqdn`, `containerAppsEnvironmentName`, `logAnalyticsWorkspaceId`.

## Po nasadení

1. **Obrazy:** Bicep použije placeholder. Reálne obrazy doplní deploy workflow API (#161) a ML (#164); vtedy nastav `apiImage` a `mlImage` na publikované tagy.
2. **Frontend:** do GitHub Secrets pridaj `AZURE_STATIC_WEB_APPS_API_TOKEN` a do Variables `VITE_API_URL` (#280). Token získaš cez:
   ```bash
   az staticwebapp secrets list \
     --name <static-web-app-name> \
     --resource-group rg-solarops-dev \
     --query properties.apiKey -o tsv
   ```
3. **Migrácie:** pred prvým štartom API aplikuj EF migrácie na PostgreSQL (#158 v pôvodnom znení je zastarané).
4. **SPA routing:** `staticwebapp.config.json` s `navigationFallback` a bezpečnostnými hlavičkami patrí do #278.
5. **Ďalšie vrstvy:** Key Vault (#159), Application Insights (#167), budget a alerty (#169), custom doména (#166).

## Odhad nákladov

Ceny sú z ADR [#215](../docs/adr/spike-215-azure-naklady.md) (West Europe, október 2026) a nie sú overené na študentskom predplatnom.

| Položka | Mesačne (USD) |
|---|---:|
| PostgreSQL B1ms, výpočet + 32 GB | cca 18,91 |
| Container Apps, scale-to-zero (API aj ML) | 0 v rámci free grantu |
| Static Web Apps Free | 0 |
| Log Analytics (< 1 GB/mesiac) | 0–3 |
| **Spolu, demo** | **cca 19–22** |
| API stále zapnuté (`apiMinReplicas = 1`) | cca +20, teda 44–47 |

Kredit Azure for Students je 100 USD. Pred obhajobou sa dá `apiMinReplicas` nastaviť na 1, po nej vrátiť na 0. Ak sa potvrdí 12-mesačná free ponuka PostgreSQL, náklady klesnú takmer na nulu.

## Obmedzenia a riziká

- **Databáza je verejne dostupná** cez firewall pravidlo `AllowAzureServices` (Container Apps nemá stabilnú odchádzajúcu IP). Pre produkciu treba private endpoint alebo VNet integráciu; pre študentské demo je to vedomý kompromis.
- **Tajomstvá** sú v Container Apps secrets, nie v Key Vault. To je dočasné do #159.
- **X-Forwarded-For:** API dôveruje privátnej sieti `10.0.0.0/8`, z ktorej Container Apps ingress pridáva hlavičku. Bez toho by rate limit podľa reálnej IP (#379) videl jednu spoločnú adresu. Pri prechode na VNet rozsah zúžte.
- **Súkromné obrazy:** šablóna nekonfiguruje `registries`. Placeholder z MCR je verejný; ak sa použije súkromné GHCR, doplňte registry credentials do Container App (súčasť #161/#164).
- **Placeholder obrazy** po prvom deployi neposkytujú reálne API ani ML; zdroje ale vzniknú a dajú sa aktualizovať bez zmeny infraštruktúry.
- **Región a kvóty** Container Apps a free ponuka PostgreSQL na Azure for Students nie sú overené. Pred deployom skontroluj dostupnosť v subscription.
- **Žiadny ACR** — počíta sa s GHCR; ak sa tím rozhodne pre ACR Basic, pribudne cca 5 USD/mesiac a parameter pre registry.
- **Cold start** pri `minReplicas = 0`; pred demo nastav API na 1.
- **Vlastná doména** a CORS pre ňu nie sú súčasťou šablón; CORS je nastavený na predvolený hostname Static Web App.

## Zmazanie prostredia

```bash
az group delete --name rg-solarops-dev --yes --no-wait
```

Tým sa zmažú všetky zdroje vrátane databázy. Pred zmazaním skontroluj, že zálohy už nepotrebuješ.
