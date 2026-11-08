# Azure infraštruktúra (Bicep)

Celé prostredie SolarOps v Azure je opísané v [`main.bicep`](main.bicep) (#157). Rozhodnutia sú v ADR #211, #213, #215 a #277; ADR #277 mení región a hosting frontendu.

## Čo sa vytvorí

Všetko je v resource group `solarops-rg` v regióne **Poland Central** (predplatné Azure for Students povoľuje len niekoľko regiónov).

| Zdroj | Názov | Poznámka |
|---|---|---|
| Container Apps prostredie | `solarops-env` | Consumption, logy do Log Analytics |
| API | `solarops-api` | verejné, port 8080, 0 až 1 replika |
| ML služba | `solarops-ml` | len interná, API ju volá na `http://solarops-ml` |
| Frontend | `solarops-web` | nginx, verejný, port 8080 |
| Job `solarops-migrate` | ručne | EF migrácie (`--job migrate`), spúšťa ho `deploy.sh` |
| Job `solarops-ml-analysis` | každú hodinu (`5 * * * *`, UTC) | analýza inštalácií, alarmy a tikety |
| Job `solarops-weather` | denne o 3:30 UTC | predpoveď počasia |
| PostgreSQL Flexible Server | `solarops-pg-<suffix>` | 16, B1ms, 32 GB, zálohy 7 dní |
| Key Vault | `solarops-kv-<suffix>` | tajomstvá, prístup cez RBAC |
| Managed identity | `solarops-identity` | aplikácie a joby ňou čítajú tajomstvá z Key Vault |
| Log Analytics | `solarops-logs` | 30 dní, limit 1 GB denne |

Joby používajú obraz API. Plán ML analýzy a počasia je v API vypnutý (`Ml__AnalysisEnabled=false`, `Weather__Enabled=false`), robia ho joby.

## Tajomstvá

V gite nie je žiadne tajomstvo. `deploy.sh` pri prvom behu vygeneruje do `infra/.env` (ignorovaný, práva 600):

- `SOLAROPS_POSTGRES_PASSWORD` – heslo správcu PostgreSQL,
- `SOLAROPS_JWT_SIGNING_KEY` – podpisový kľúč JWT,
- `SOLAROPS_ADMIN_PASSWORD` – heslo prvého admina `admin@solarops.local`.

Bicep ich zapíše do Key Vault (`postgres-connection`, `jwt-signing-key`, `admin-password`). Container Apps ich čítajú cez managed identity ako referencie na Key Vault, nie ako hodnoty. `infra/.env` si odlož; bez neho ďalší beh vygeneruje nové heslá a zmení ich v Azure.

## Nasadenie

Predpoklady: `az login` do predplatného Azure for Students, zaregistrovaní provideri `Microsoft.App`, `Microsoft.DBforPostgreSQL` a `Microsoft.KeyVault`, obrazy API a ML v registri (zo CD #161 a #162, napr. `ghcr.io/takacmichal/aispu-api:<tag>`).

```bash
SOLAROPS_API_IMAGE=ghcr.io/takacmichal/aispu-api:<tag> \
SOLAROPS_ML_IMAGE=ghcr.io/takacmichal/aispu-ml:<tag> \
infra/deploy.sh
```

Skript:

1. vytvorí resource group a nasadí `main.bicep` s parametrami z `main.bicepparam`,
2. spustí job `solarops-migrate` a počká na jeho koniec,
3. reštartuje API, aby na novej schéme vytvorilo roly a prvého admina,
4. vypíše adresy frontendu a API.

Ďalšie behy môžu obrazy vynechať, ostanú nasadené. Frontend pri prvom nasadení beží ako prázdny nginx (`nginx-unprivileged`), lebo jeho build potrebuje adresu API (`VITE_API_URL`), ktorá vznikne až s prostredím. Skutočný obraz frontendu nasadí CD (#162).

Súkromné obrazy: nastav `SOLAROPS_REGISTRY_USERNAME` a `SOLAROPS_REGISTRY_PASSWORD` (napr. GHCR token s `read:packages`).

Overenie šablóny bez vytvárania zdrojov:

```bash
az bicep build --file infra/main.bicep
az deployment group validate -g <existujúca resource group> --parameters infra/main.bicepparam   # premenné SOLAROPS_* nastavené
```

## Prevádzka

```bash
# pred obhajobou bez studeného štartu (~cena: API a frontend bežia stále)
az containerapp update -g solarops-rg -n solarops-api --min-replicas 1
az containerapp update -g solarops-rg -n solarops-web --min-replicas 1

# logy aplikácie a jobu
az containerapp logs show -g solarops-rg -n solarops-api --tail 100
az containerapp job execution list -g solarops-rg -n solarops-migrate -o table

# ručné spustenie analýzy
az containerapp job start -g solarops-rg -n solarops-ml-analysis

# PostgreSQL mimo hodnotenia zastaviť (platí sa len úložisko)
az postgres flexible-server stop -g solarops-rg -n <solarops-pg-...>

# po obhajobe všetko zmazať
az group delete -n solarops-rg
```

Náklady sú v ADR #277 (približne 15 až 20 USD za 5 dní v horšom prípade). Rozpočet `solarops-budget` (30 USD) posiela upozornenia vlastníkovi predplatného.

## Známe obmedzenia

- **Firewall databázy** povoľuje služby Azure (pravidlo `0.0.0.0`), nie len naše prostredie. Container Apps (Consumption) nemajú pevnú odchádzajúcu IP; uzavretie by vyžadovalo VNet.
- **Prvé nasadenie** môže zlyhať, ak sa priradenie roly pre Key Vault ešte nerozšírilo (aplikácia nevie načítať tajomstvo). Stačí `deploy.sh` spustiť znova.
- **Rate limiting za proxy:** API nemá nastavené `ForwardedHeaders__KnownNetworks__0` pre sieť Container Apps, všetci klienti preto môžu zdieľať jeden limit prihlásení (README, Časté chyby).
- **ML model:** obraz neobsahuje natrénovaný model, služba počíta nekalibrovaným fyzikálnym modelom (#378).
- Application Insights a upozornenia ešte nie sú (#167, #168).
