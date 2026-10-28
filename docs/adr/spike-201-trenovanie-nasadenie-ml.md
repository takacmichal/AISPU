# ADR #201: Trénovanie a nasadenie ML do Azure

- Stav: Navrhnuté (výstup spiku #201, nadradené #11)
- Dátum: 2026-10-28
- Súvisí s: [ADR #198](spike-198-verzovanie-ml-modelu.md) (verzovanie a metadáta modelu)
- Ceny: Azure Retail Prices API (`prices.azure.com/api/retail/prices`), región `westeurope`, dopytované 2026-10-28; mená/jednotky podľa API. Marketingové cenníky na azure.microsoft.com sa nedali použiť (ceny sa načítavajú skriptom, vo fetchi boli prázdne). Ceny sú bez DPH, pay-as-you-go, nezahŕňajú študentské kredity.
- Doplnkové zdroje: Microsoft Learn, [Billing in Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/billing) (stav 2026-03-25), [Revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions) (2026-02-26), [Jobs](https://learn.microsoft.com/en-us/azure/container-apps/jobs) (2026-09-16), [Azure ML pricing](https://azure.microsoft.com/en-us/pricing/details/machine-learning/).

## Kontext

Potrebujeme (1) miesto, kde sa model trénuje, a (2) hosting ML služby (predikcia výroby z počasia, volaná z .NET API), s ohľadom na študentský rozpočet. Model je malý tabuľkový (scikit-learn), dáta v rádoch MB až stoviek MB, pretrénovanie zriedkavé (týždne až mesiace), záťaž nízka (školský projekt, demo). Backend API beží na App Service, ostatné podľa epicu #11.

## Alternatívy

### Tréning

| | T1: Lokálny tréning + upload do Blob | T2: Azure ML (compute) | T3: Container Apps Job |
|---|---|---|---|
| Postup | `train.py` lokálne, upload `model.joblib` + `metadata.json` do Blobu | workspace, compute, tracking v Azure | job s kontajnerom s `train.py`, spúšťaný ručne |
| Cena | 0 | Azure ML: podľa stránky cenníka bez príplatku za samotnú službu, platí sa compute (za sekundu) a súvisiace služby (storage, Key Vault, registry, monitoring); presná suma neoverená | spotreba v rámci mesačných bezplatných grantov Consumption plánu (viď nižšie) |
| Zložitosť | nízka | vysoká pre jeden malý model | stredná (Docker image pre tréning, dáta v Blobe) |
| Reprodukovateľnosť | skript + lock + metadáta (ADR #198) | silná, ale viaže na Azure ML | rovnaká ako T1, prostredie fixované imageom |

### Hosting ML služby

| | H1: Container Apps (Consumption) | H2: App Service for Containers (Linux) |
|---|---|---|
| Model ceny | za sekundu vCPU/GiB + požiadavky, scale-to-zero | fixne za plán (hodinová sadzba) bežiaci stále |
| Scale-to-zero | áno (bez nákladov pri 0 replikách) | nie (plán beží a platí sa) |
| Bezplatný grant | 180 000 vCPU-s, 360 000 GiB-s, 2 M požiadaviek mesačne na subscription (Learn, Billing) | Free F1 existuje (1 GB RAM, 60 CPU min/deň, bez SLA, "nie pre produkciu"); podpora kontajnerov a Always On na F1 neoverená |
| Nasadenie novej verzie | nová revízia, zero-downtime v single mode, rollback cez revíziu | slot swap (potrebuje vyššiu úroveň plánu, neoverené) alebo redeploy image |
| Cold start | pri scale-to-zero áno (neoverená dĺžka) | pri Always On žiadny |
| Zdieľanie s API | samostatné prostredie | môže bežať na tom istom pláne ako API (marginálne náklady ~0, ale delia sa o 1,75 GB RAM na B1) |

## Odhad nákladov (mesačne, West Europe)

Predpoklad: ML služba 0,5 vCPU a 1 GiB, 730 h/mesiac. Sadzby z API: ACA vCPU active 0,000034 USD/s, vCPU idle 0,000004 USD/s, memory active/idle 0,000004 USD/GiB-s, requests 0,56 USD/1 M (USD, lebo v EUR API zaokrúhľuje na 0). Ostatné v EUR.

| Variant | Výpočet | Odhad / mesiac |
|---|---|---|
| ACA, scale-to-zero, ~2 h aktívne denne (60 h) | 108 000 vCPU-s a 216 000 GiB-s, pod bezplatným grantom | 0 USD |
| ACA, min. 1 replika, väčšinou idle | (1 314 000 - 180 000) × 0,000004 + (2 628 000 - 360 000) × 0,000004 | ~14 USD |
| ACA, min. 1 replika, stále aktívna (horný odhad) | 1 134 000 × 0,000034 + 2 268 000 × 0,000004 | ~48 USD |
| App Service Linux B1 (1 vCPU, 1,75 GB) | 0,0158 EUR/h × 730 | ~11,5 EUR |
| App Service Linux B2 (2 vCPU, 3,5 GB) | 0,0317 EUR/h × 730 | ~23,1 EUR |
| App Service Linux P0v3 (1 vCPU, 4 GB) | 0,0744 až 0,0783 EUR/h × 730 (API vrátilo dve sadzby) | ~54 až 57 EUR |
| Azure Container Registry Basic (10 GB v cene) | 0,1466 EUR/deň | ~4,5 EUR |
| Blob Storage Hot LRS (model + dáta, ~1 GB) | 0,0172 EUR/GB | < 0,05 EUR |
| Tréning lokálne | | 0 |
| Tréning ako ACA Job (2 vCPU, 4 GiB, 10 min/mesiac) | 1 200 vCPU-s, 2 400 GiB-s, pod grantom (grant je zdieľaný s ML službou) | 0 USD |
| Azure ML | bez príplatku za službu (podľa cenníka), platí sa compute + súvisiace zdroje | neoverené, nad 0 |

Poznámky: bezplatný grant je mesačný na celú subscription, takže ho zdieľa ML služba aj job. Pri variante min. 1 replika platí rozpätie 14 až 48 USD podľa toho, koľko času replika nie je idle (idle = pod 0,01 vCPU a pod 1 000 B/s siete, Learn Billing). Image môže bežať aj z iného registra než ACR (pozri Neoverené).

## Rozhodnutie

1. **Tréning: T1, lokálny tréning + upload do Blob Storage** s metadátami podľa ADR #198. Azure ML sa nepoužije (pre jeden malý model je to réžia, kým lokálny tréning je zadarmo a reprodukovateľný skriptom). T3 (Container Apps Job) je zálohou, ak by tréning prerástol laptop alebo mal bežať automaticky.
2. **Hosting: H1, Azure Container Apps (Consumption plán)** pre ML službu. Dôvod: scale-to-zero a bezplatný grant dávajú pri nízkej záťaži cenu ~0 USD, kým App Service platí fixne aj pri nečinnosti (B1 ~11,5 EUR). Revízie dávajú zero-downtime nasadenie a jednoduchý rollback.
3. **Image obsahuje len kód a závislosti, model nie.** Služba pri štarte stiahne `models/pv-forecast/v${MODEL_VERSION}/model.joblib` z Blobu (managed identity s rolou Storage Blob Data Reader, bez kľúčov v kóde), overí `artifact_sha256` a odmietne štart pri nezhode. Nová verzia modelu tak nevyžaduje build image.
4. **Cold start:** predvolene `minReplicas = 0`. .NET API volá ML službu s timeoutom a retry (prvé volanie po nečinnosti môže trvať dlhšie). Na demo/obhajobu sa nastaví `minReplicas = 1` (~14 až 48 USD za mesiac, alebo len dočasne na dni demo).
5. **Image:** multi-stage Dockerfile, `python:3.12-slim`, bez dev závislostí; cieľ do ~500 MB (odhad, treba zmerať).

### Postup nasadenia novej verzie modelu

1. Lokálne `python ml/train.py` (čistý git, tag `ml-vX.Y.Z`), kontrola metrík proti baseline v `metadata.json`.
2. Upload: `az storage blob upload-batch -d models --destination-path pv-forecast/vX.Y.Z -s out/` (verzie sa neprepisujú).
3. Nasadenie: `az containerapp update -n <ml-app> -g <rg> --set-env-vars MODEL_VERSION=X.Y.Z`. Vytvorí sa nová revízia. V single revision mode ostane starý traffic na starej revízii, kým nová nie je pripravená (provisioned, škálovaná, prešli startup a readiness probes); readiness probe na `/health` musí vrátiť 200 až po úspešnom načítaní modelu, takže chybná verzia sa do prevádzky nedostane.
4. Overenie: `GET /health` vracia `model_version` a `artifact_sha256`, smoke test jednej predikcie.
5. Zmena kódu služby: nový image tag (`ml-service:<git sha>`) cez GitHub Actions, rovnaký `az containerapp update --image ...`.
6. Rollback: `az containerapp update ... --set-env-vars MODEL_VERSION=<predošlá>` (krok 3 naopak). Alternatívne návrat na predošlú revíziu cez traffic/aktiváciu revízií (Learn: "You can roll back to a previous revision"; presný príkaz a podmienky pre single mode treba vyskúšať).

## Dôsledky

- Mesačná cena ML časti je typicky 0 USD až ~14 USD, s horným odhadom ~48 USD pri stále aktívnej replike; k tomu ACR ~4,5 EUR, ak sa použije.
- Verzia modelu je oddelená od verzie kódu služby, rollback modelu je zmena jednej premennej.
- Cold start po nečinnosti je cena za scale-to-zero; klient (API) musí byť odolný voči pomalej prvej odpovedi.
- Služba závisí od dostupnosti Blobu pri štarte. Zmiernenie: retry pri štarte; pri pretrvávajúcom probléme ostáva predošlá revízia.
- Tréning nie je v cloude, takže je viazaný na stroj autora; reprodukovateľnosť zaisťujú lock súbor a metadáta (ADR #198).
- Nadväzujúce issues: Dockerfile ML služby + `/health`, Bicep/azd pre Container Apps prostredie + managed identity + Blob, GitHub Actions pre build a deploy, zmeranie veľkosti image a cold startu, rozhodnutie o registri (ACR vs GHCR).

## Neoverené

- **Cold start a veľkosť image:** nemerané, hodnoty (~500 MB, dĺžka štartu) sú odhady. Treba zmerať po prvom nasadení (čas od požiadavky po 200 z `/health`).
- **Poplatok "Environment Management Hour"** (0,143 USD/h = ~104 USD/mesiac, platný od 2026-09-01, v API pod ACA): Learn Billing tvrdí, že poplatok za správu sa účtuje pri súkromných endpointoch, plánovanej údržbe alebo Dedicated profile. Že čisté Consumption prostredie bez týchto funkcií ho neplatí, som nepotvrdil z cenníka ani z faktúry. **Pred nasadením overiť cez Azure Pricing Calculator alebo Cost Analysis**, inak môže byť odhad H1 nesprávny.
- App Service: či Free F1 podporuje kontajnery a Always On, a podmienky slot swapu; API vrátilo pre P0v3 dve rôzne sadzby (rôzne dátumy účinnosti).
- Presná cena Azure ML a jeho závislostí, a či rozdiel medzi USD/EUR sadzbami zodpovedá aktuálnemu kurzu (použité hodnoty priamo z API).
- Región: použitý West Europe; región projektu nie je potvrdený. Študentské kredity/Azure for Students neboli zohľadnené.
- GHCR ako bezplatná alternatíva k ACR (limity a súkromné image) nebolo overované.
- Ceny sa menia; hodnoty platia k 2026-10-28.
