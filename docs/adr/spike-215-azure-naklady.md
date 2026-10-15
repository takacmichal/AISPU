# ADR spike #215: Odhad nákladov v Azure a využitie free/student tier

- **Stav:** Navrhnuté
- **Dátum:** 2026-10-15
- **Issue:** #215 (nadradené #11)
- **Súvisí:** #211 (hosting), #213 (PostgreSQL), #169 (rozpočet a sledovanie nákladov)

## Kontext

SolarOps sa nasadzuje do Azure v Sprinte 4 na študentské predplatné. Architektúra (ADR #211): Container Apps (API + ML), Static Web Apps (React), PostgreSQL Flexible Server B1ms. Potrebujeme vedieť, čo sa zmestí do kreditu, kde sú free tiery a ako sa chrániť pred prekročením.

## Overené východiská

- **Azure for Students:** 100 USD kredit na 12 mesiacov, bez kreditnej karty, obnoviteľné každý rok, kým je osoba študent; k tomu mesačné bezplatné množstvá 20+ služieb na 12 mesiacov (len noví zákazníci Azure) a 65+ vždy bezplatných služieb [1].
- **Container Apps (Consumption):** mesačne na subscription zdarma 180 000 vCPU-s, 360 000 GiB-s a 2 mil. požiadaviek; pri scale-to-zero (0 replík) sa za výpočet neúčtuje [2][3].
- **Static Web Apps Free:** bez poplatku (limity: 250 MB aplikácia, 2 vlastné domény) [4]. Standard stojí 9 USD/mesiac (Retail Prices API) [5].
- **PostgreSQL Flexible Server:** stop/start je podporený ako nástroj úspory [6]. Tvrdenie o **bezplatnej ponuke PostgreSQL na 12 mesiacov (B1ms, 750 h, 32 GB) som nedokázal overiť** (stránky so službami sa vykresľujú dynamicky), preto je v odhade ako **neoverené, overiť na subscription**.

## Odhad nákladov (ceny z Azure Retail Prices API, región West Europe, 15. 10. 2026, USD, 730 h/mesiac; Pricing Calculator pre ostré číslo nebol použitý)

| Položka | Predpoklad | Mesačne (USD) |
|---|---|---|
| PostgreSQL Flexible B1ms, výpočet | 0,0199 USD/h, beží nepretržite | 14,53 |
| PostgreSQL úložisko | 32 GB x 0,1369 USD/GB (minimálna veľkosť **neoverená**), záloha v cene do veľkosti úložiska (**neoverené**) | 4,38 |
| Container Apps, scenár demo (scale-to-zero) | API 0,25 vCPU/0,5 GiB cca 2 h/deň, ML 0,5 vCPU/1 GiB cca 1 h/deň = 108 000 vCPU-s, 216 000 GiB-s | 0 (v bezplatnom limite) |
| Container Apps, API stále zapnuté (aktívna sadzba) | 0,25 vCPU/0,5 GiB, 24/7, sadzba 3,4e-05 USD/vCPU-s a 4e-06 USD/GiB-s po odpočítaní free limitu | cca 20 (horný odhad; v nečinnosti nižšie, idle sadzba 4e-06) |
| Static Web Apps | plán Free | 0 |
| Registry obrazov | GHCR (pre verejný/súkromný repozitár podľa podmienok GitHubu, **neoverené**) alebo ACR Basic 0,1666 USD/deň | 0 alebo cca 5,00 |
| Log Analytics (vytvára ho prostredie Container Apps) | príjem logov 2,99 USD/GB nad bezplatný limit (limit **neoverený**); očakávané < 1 GB/mesiac | cca 0 až 3 |
| **Spolu, demo (scale-to-zero, GHCR)** | PostgreSQL + logy | **cca 19 až 22** |
| **Spolu, API stále zapnuté, ACR Basic** | | **cca 45 až 50** |

S kreditom 100 USD vychádza **scenár demo na cca 4 až 5 mesiacov** prevádzky (Sprint 4 trvá kratšie), scenár so stále zapnutým API na cca 2 mesiace. Ak sa potvrdí bezplatná ponuka PostgreSQL, scenár demo klesne na cca 0 až 8 USD/mesiac.

## Rozhodnutie

- **Používame Azure for Students kredit**; nasadzujeme až v Sprinte 4 (nie skôr), aby kredit nebežal naprázdno.
- **Služby s free tierom/scale-to-zero:** Container Apps Consumption (`minReplicas = 0`), Static Web Apps Free; PostgreSQL B1ms s bezplatnou ponukou na 12 mesiacov **len ak ju overíme na subscription**; inak je PostgreSQL najväčšia položka.
- **Úspory:** zastaviť PostgreSQL server mimo vývoja a hodnotenia (stop/start [6]; správanie úložiska a automatický reštart po dlhšom čase **neoverené**), nepoužívať ACR (GHCR), neobmedzovať logy (nízka retencia, bez debug logov), pred obhajobou zapnúť `minReplicas = 1` len na deň demo, po odovzdaní zmazať resource group.
- **Rozpočtové upozornenia:** v Azure Cost Management vytvoriť budget na úrovni kreditu (100 USD) s upozorneniami e-mailom pri **50 %, 75 %, 90 % a 100 %** skutočných nákladov, a pri 100 % prognózy (forecast). Dostupnosť budgetov na predplatnom Azure for Students je **neoverená** (#169); náhradou je týždenná kontrola v Cost analysis.

## Dôsledky

- (+) Predvídateľné náklady v rámci kreditu pri scenári demo.
- (-) Cold start pri scale-to-zero (pozri ADR #211).
- (-) Odhad je z cenníka a predpokladov využitia, nie z faktúry; skutočné náklady treba sledovať od prvého týždňa nasadenia.
- Implementácia **musí** počítať s: Azure for Students kredit, PostgreSQL free 12 mesiacov overiť na subscription, ACA Consumption, SWA Free.

## Riziká a neoverené

- Bezplatná ponuka PostgreSQL (rozsah, trvanie, dostupnosť pre Students), minimálne úložisko a zálohy.
- Limity Azure for Students (povolené regióny, kvóty Container Apps, Azure Policy); región West Europe bol použitý len pre cenník.
- Free limit Log Analytics, ceny pre iné regióny, kurz USD/EUR.
- Pricing Calculator nebol použitý; čísla sú zo súboru Retail Prices API.

## Zdroje

1. Azure for Students: https://azure.microsoft.com/en-us/free/students
2. Container Apps, fakturácia (bezplatné množstvá): https://learn.microsoft.com/en-us/azure/container-apps/billing
3. Container Apps, škálovanie (minimum replík 0): https://learn.microsoft.com/en-us/azure/container-apps/scale-app
4. Static Web Apps, plány: https://learn.microsoft.com/en-us/azure/static-web-apps/plans
5. Azure Retail Prices API: https://prices.azure.com/api/retail/prices (filtre: `serviceName`, `armRegionName eq 'westeurope'`)
6. Azure Database for PostgreSQL Flexible Server, cenník (stop/start, burstable): https://azure.microsoft.com/en-us/pricing/details/postgresql/flexible-server/

## Nadväzujúce issues

- #169 Nastaviť rozpočet a sledovanie nákladov, #157 Vytvoriť Azure prostredie ako kód, #164 Nasadiť ML službu (Sprint 4, feature #47, #50).
- Zastarané (nevytvárané ani upravené): #158 Nasadiť Azure SQL a migrácie (databáza je PostgreSQL).
