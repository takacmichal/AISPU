# ADR spike #222: Obnovovanie dashboardu (polling vs. SignalR vs. SSE)

- **Stav:** Navrhnuté (výstup spiku #222, nadradené #10)
- **Dátum:** 2026-11-04
- **Súvisí:** ADR #185 (5-min telemetria), ADR #194 (detekcia podvýroby), ADR #211 (hosting), ADR #215 (náklady), #251/#252 (alerty), #271 (notifikačné centrum), #376 (napojenie API na ML)

## Kontext

`Prehľad` dnes načíta KPI a poslednú aktivitu raz pri otvorení a potom sa už sám neobnovuje; `Monitoring výroby` načíta dáta pri zmene inštalácie alebo obdobia. Používateľ má pri chybe iba manuálne „Skúsiť znova“. Dashboard tak môže zobrazovať zastarané dáta, kým ho používateľ sám neobnoví.

Otázka spiku: ako dostať nové dáta a alarmy na dashboard, keď telemetria prichádza po minútach a real-time nie je kritický? Má sa použiť polling, SignalR alebo SSE?

Východiská z repozitára:

- **Telemetria** je na 5-minútovej UTC mriežke; ingest prijíma dávky až 288 meraní a je idempotentný (ADR #185). Výroba sa agreguje na 5 min, hodiny, dni a mesiace (ADR #220).
- **Detekcia podvýroby** beží na hodinových dátach a udalosť vzniká až po **2 po sebe idúcich podvýrobných hodinách** (ADR #194). Alarm teda fyzicky nemôže byť k dispozícii skôr ako približne 2 hodiny po začiatku poruchy.
- **Alarmy ešte nie sú v API.** ML služba má endpoint `POST /v1/detections/underproduction`, ale API ju zatiaľ nevolá a nemá tabuľku ani endpoint alarmov; integráciu rieši #376. Frontend má pripravený placeholder (`anomaliesApi.ts`, `AnomalySection.tsx`).
- **Stack:** React 19 + Vite, bearer JWT cez `authFetch`, priame CORS volanie API z Azure Static Web Apps Free na Azure Container Apps (ADR #211). V projekte nie je žiadna závislosť na SignalR ani SSE.
- **Notifikačné centrum** už má v akceptačných kritériách polling a SSE/SignalR výslovne vylučuje (#271).

## Požiadavky na aktuálnosť

Zadanie neuvádza čísla, preto ich odvodzujeme z fyzikálnych limitov dát a z požadovanej odozvy UI. Cieľom je MVP, nie real-time produkt. Spike bol robený v rámci 4-hodinového timeboxu; záťažový test a meranie reálnej latencie neboli jeho súčasťou.

| Dátový tok | Zdroj / frekvencia | Cieľová aktuálnosť v MVP | Poznámka |
|---|---|---|---|
| Výroba v monitoringu | 5-min telemetria, agregáty | do 5 min | Novší údaj ako 5 min neexistuje; kratší interval nemá zmysel. |
| KPI a posledná aktivita | zmeny pri akciách používateľov | do 5 min | Po návrate na kartu sa obnoví hneď, nie až po intervale. |
| Alarmy podvýroby | hodinová detekcia, >= 2 h | do 60 s od uloženia alarmu | Celková latencia je daná detekciou (>= 2 h), nie dopravou do UI. |
| Stav API v pätičke | health/info | do 5 min | Nekritické, stačí pri otvorení a po návrate na kartu. |
| Historické obdobie | nemenné dáta | bez automatického obnovovania | Obnoviť len pri zmene filtra alebo manuálne. |

**Záver požiadaviek:** sub-sekundová latencia nemá oporu v dátach. Pre dáta stačí 5 min, pre alarmy 60 s. To je presne oblasť, kde je polling jednoduchší a lacnejší než trvalé spojenie.

## Zvažované možnosti

| Kritérium | A. Polling REST (odporúčaný) | B. SignalR + Azure SignalR Service | C. SignalR na vlastnom hostingu | D. SSE |
|---|---|---|---|---|
| Model | klient sa pýta v intervale | server push cez WebSocket / fallback | server push cez WebSocket | jednosmerný server push cez HTTP |
| Latencia | interval (5 min dáta, 60 s alarmy) | < 1 s | < 1 s | < 1 s |
| Nová infraštruktúra | žiadna, existujúce REST endpointy | Azure SignalR Service, hub, negotiate | Redis backplane, sticky sessions, min. 1 replika | streaming endpoint, fan-out, reconnect |
| Azure Container Apps | funguje so scale-to-zero | WebSocket podporovaný; služba drží spojenia, aplikácia musí bežať pri odosielaní | persistentné spojenia držia repliku; session affinity len v single revision mode a HTTP ingress | HTTP ingress má timeout 240 s, SSE treba počítať s reconnectom |
| Náklady | 0 USD navyše, requesty v rámci free grantu | Free: 20 spojení a 20 000 správ/deň; Standard 1,61 USD/jednotka/deň (~49 USD/mes.), prvý 1M správ v cene, potom 1 USD/milión | min. 1 replika API ~20 USD/mes. (ADR #215) + Redis (cena neoverená) | 0 USD navyše, ale spotrebúva spojenia a compute |
| Autentifikácia | existujúci `Authorization: Bearer` header | token cez `accessTokenFactory`, nutná konfigurácia hubu | to isté | `EventSource` nepodporuje vlastné hlavičky, token by musel ísť do cookie alebo URL |
| Zložitosť | nízka: hook, interval, abort, backoff | vysoká: hub, reconnect, skupiny, škálovanie, testy | vysoká: hub + backplane + sticky sessions | stredná: streaming, reconnect, auth obchádzka |
| Testovateľnosť | fake timers, jednoduché mocky | integračné testy hubu a spojenia | hub + Redis + sticky sessions | testovanie streamu a reconnectu |
| Vhodnosť pre 5-min dáta a 2-h detekciu | **áno** | zbytočne silné | zbytočne silné | zbytočne silné |

### A. Polling REST

- Využíva existujúce endpointy a `authFetch`; žiadny nový balík, služba ani secret.
- Dashboard sa obnoví v intervale, po návrate na kartu (`visibilitychange`, `focus`) a po obnovení spojenia (`online`).
- Pri skrytej karte sa polling pozastaví; pri chybe sa interval predlžuje (backoff) a po úspechu sa vráti na základnú hodnotu.
- Požiadavky sa nesmú prekrývať: pred ďalším cyklom sa zruší predchádzajúci `AbortController`.
- Voliteľne sa doplní `ETag`/`If-None-Match`: zníži sa prenášaný objem (304), ale počet requestov zostane. Pre MVP to nie je podmienka.

### B. SignalR + Azure SignalR Service

- Manažovaná služba rieši backplane a spojenia; podľa dokumentácie nie sú potrebné sticky sessions, klienti sa presmerujú na SignalR Service.
- Free tier je 20 súbežných spojení a 20 000 správ/deň, čo je pre demo použiteľné, ale bez SLA a s limitom jednej jednotky. Standard stojí 1,61 USD/jednotka/deň (Retail Prices API, West Europe, 4. 11. 2026) a prvý milión správ je v cene.
- Prináša hub, negotiate endpoint, reconnect, autorizáciu, skupiny a testovanie spojenia. To je neúmerné požiadavke „dáta do 5 min, alarmy do 60 s“.
- Pri scale-to-zero sa aplikácia musí prebudiť, aby správu odoslala; samotná služba teda neruší cold start pri doručení alarmu.

### C. SignalR na vlastnom hostingu

- Vyžaduje sticky sessions a backplane (Redis), inak sa správa nedostane ku klientom na inej replike. Container Apps podporuje session affinity len v single revision mode a pri HTTP ingress.
- Persistentné spojenia držia repliku API zapnutú, takže prakticky rušia scale-to-zero a zvyšujú baseline náklady.
- Oproti Azure SignalR Service je to viac prevádzkovej práce za rovnaký výsledok. Pre tento projekt nemá opodstatnenie.

### D. SSE

- Jednosmerný kanál by na alarmy stačil a nevyžaduje Azure službu.
- `EventSource` neumožňuje nastaviť `Authorization` header; token by musel ísť do cookie alebo query stringu, čo zhoršuje bezpečnosť a logovanie.
- Container Apps HTTP ingress má timeout 240 s; SSE je dlhý HTTP response, preto treba počítať s pravidelným reconnectom (overiť pri konkrétnej implementácii).
- Pri viacerých replikách treba fan-out/backplane rovnako ako pri SignalR. Zložitosť sa teda nepribližuje pollingu.

## Odhad nákladov a requestov

Predpoklady: 5 endpointov na jeden refresh dashboardu (info + 4 zoznamy), 2 endpointy na monitoring (výroba + porovnanie), 1 ľahký endpoint na alarmy. Free grant Container Apps je 2 milióny HTTP requestov na subscription za kalendárny mesiac; health probes sa neúčtujú (ADR #215 a oficiálna dokumentácia). Mesiac uvažujeme ako 30,5 dňa.

| Scenár | Requesty/deň | Requesty/mesiac | Podiel z 2M grantu |
|---|---:|---:|---:|
| Dashboard 5 min, 5 endpointov, 1 používateľ | 1 440 | 43 920 | 2,2 % |
| Dashboard 5 min, 5 endpointov, 10 používateľov | 14 400 | 439 200 | 22 % |
| Dashboard 60 s, 5 endpointov, 10 používateľov | 72 000 | 2 196 000 | 110 % |
| Monitoring 5 min, 2 endpointy, 10 používateľov | 5 760 | 175 680 | 8,8 % |
| Alarmy 60 s, 1 endpoint, 10 používateľov | 14 400 | 439 200 | 22 % |
| **Návrh spolu** (dashboard 5 min + monitoring 5 min + alarmy 60 s, 10 používateľov) | **34 560** | **1 054 080** | **53 %** |

Ide o hornú hranicu „10 používateľov s otvoreným dashboardom 24/7“. Reálna prevádzka počas demo/hodnotenia je výrazne nižšia. Ak by sa 60 s použilo aj na celý dashboard pre 10 súbežných používateľov, free grant sa prekročí; preto je 60 s vyhradené len pre ľahký alarmový endpoint.

SignalR v porovnaní: Free tier 0 USD, ale limit 20 spojení a 20 000 správ/deň; Standard ~49 USD/mesiac. Vlastný SignalR pridá min. 1 repliku API (~20 USD/mes.) a Redis. Polling je teda najlacnejšia varianta, ktorá spĺňa požiadavky.

## Rozhodnutie

1. **Pre MVP používame polling, nie SignalR ani SSE.** Dashboard a monitoring sa obnovujú cez existujúce REST endpointy; žiadna nová infraštruktúra, závislosť ani secret.
2. **Intervaly:**
   - KPI a posledná aktivita: **5 min** počas viditeľnej karty; okamžité obnovenie pri návrate na kartu, `focus` a `online`.
   - Monitoring výroby: **5 min** zosynchronizované s 5-min telemetriou; len ak obdobie obsahuje aktuálny deň. Historické obdobie sa automaticky neobnovuje.
   - Alarmy (keď pribudne API z #376): **60 s** cez ľahký endpoint s parametrom `since` alebo `ETag`; pri 304/prázdnom výsledku sa nič neprekresľuje.
   - Stav API: 5 min alebo pri návrate na kartu.
3. **Implementačné pravidlá:** jeden spoločný hook (napr. `usePolling`/rozšírenie `useLoad`), žiadne prekrývajúce sa requesty, `AbortController`, pozastavenie pri skrytej karte, exponenciálny backoff s jitterom, zachovanie posledných dát počas obnovy a diskrétny indikátor „aktualizované pred X“.
4. **SignalR/SSE teraz nezavádzame.** Prehodnotiť až keď nastane aspoň jedna z týchto podmienok:
   - produkt bude vyžadovať doručenie alarmu do niekoľkých sekúnd (nielen minút),
   - pribudne real-time spolupráca alebo live kurzor, ktorý sa nedá riešiť pollingom,
   - počet súbežných dashboardov alebo objem requestov sa dostane nad únosnú hranicu free grantu a ETag/interval nestačia.
   V takom prípade použiť **Azure SignalR Service** (manažovaný backplane, bez sticky sessions), nie vlastný SignalR s Redisom.
5. **SSE nepoužívať**, kým nebude jasná potreba jednosmerného pushu a vyriešená autentifikácia bez tokenu v URL.
6. **ETag/If-None-Match je voliteľná optimalizácia**, nie súčasť MVP. Zaviesť ju, keď budú odpovede veľké alebo keď meranie ukáže, že sa blížime k free grantu.

## Dôsledky

- (+) Žiadna nová Azure služba, konfigurácia ani tajomstvo; funguje s ADR #211 aj scale-to-zero.
- (+) Autentifikácia zostáva v `Authorization` hlavičke, token nejde do URL ani do cookie.
- (+) Jednoduché testovanie cez fake timers a existujúce mocky; malá zmena vo frontende.
- (+) Náklady zostávajú v rámci free grantu pri navrhnutých intervaloch.
- (-) Dáta môžu byť zastarané až 5 min, alarmy až 60 s; to je však v rámci fyzikálnych limitov.
- (-) Polling vytvára opakované requesty aj vtedy, keď sa nič nezmenilo; rieši sa viditeľnosťou karty, backoffom a neskôr ETagom.
- (-) Ak sa zmení požiadavka na real-time, bude treba samostatnú zmenu a nové ADR; to je zámerné.

## Riziká a neoverené

- **Odhad requestov nie je záťažový test.** Vychádza z počtu endpointov a intervalu; reálne počty závisia od rolí, dĺžky sedenia a správania používateľov. Pred nasadením treba zmerať.
- **Alarmové API neexistuje** (#376). Cieľ „60 s od uloženia“ je návrh, nie meranie; po integrácii treba overiť, ako často sa alarmy ukladajú a aký je objem odpovede.
- **Detekcia má latenciu >= 2 h** (ADR #194), takže polling 60 s nezrýchli samotnú detekciu. Ak by produkt žiadal rýchlejšie alarmy, treba meniť detekčné pravidlo, nie dopravu do UI.
- **Prehliadače obmedzujú timery na skrytých kartách.** Preto sa spoliehame na `visibilitychange`/`focus`, nie na presný interval.
- **SignalR/SSE detaily sú overené len z dokumentácie.** Pri budúcom prechode treba overiť cold start, timeout WebSocketu a doručenie pri scale-to-zero.
- **Ceny sú z 4. 11. 2026, región West Europe.** Skutočné ceny a free grant overiť na subscription (#215).
- **SSE autentifikácia:** `EventSource` nepodporuje `Authorization` hlavičku; token v query stringu môže unikať do logov. Bez vyriešenia túto variantu neodporúčame.

## Zdroje

1. Interné: [ADR #185 – telemetria](spike-185-telemetria.md), [ADR #194 – podvýroba](spike-194-definicia-podvyroby.md), [ADR #211 – hosting](spike-211-azure-hosting.md), [ADR #215 – náklady](spike-215-azure-naklady.md), [ADR #220 – časové rady](spike-220-casove-rady.md), #271 (notifikačné centrum – polling), #376 (napojenie API na ML).
2. Azure Container Apps – ingress (HTTP/1.1, HTTP/2, WebSocket, timeout 240 s): https://learn.microsoft.com/en-us/azure/container-apps/ingress-overview
3. Azure Container Apps – session affinity: https://learn.microsoft.com/en-us/azure/container-apps/sticky-sessions
4. Azure Container Apps – fakturácia a free grant 2 mil. requestov: https://learn.microsoft.com/en-us/azure/container-apps/billing
5. ASP.NET Core SignalR – hosting a škálovanie: https://learn.microsoft.com/en-us/aspnet/core/signalr/scale?view=aspnetcore-10.0
6. Azure SignalR Service – cenník (Free 20 spojení / 20 000 správ/deň): https://azure.microsoft.com/en-us/pricing/details/signalr-service/
7. Azure Retail Prices API – Standard Unit 1,61 USD/deň, West Europe, 4. 11. 2026: https://prices.azure.com/api/retail/prices
8. MDN – `EventSource` (iba `withCredentials`, bez vlastných hlavičiek): https://developer.mozilla.org/en-US/docs/Web/API/EventSource/EventSource

## Nadväzujúce issues

- **#384 [STORY] Automatické obnovovanie dashboardu pollingom** (vytvorené z tohto spiku): hook, intervaly 5 min/60 s, viditeľnosť karty, abort, backoff a testy.
- **#376 Napojiť API na ML službu:** pri návrhu alarmového endpointu počítať s ľahkou odpoveďou pre polling (`since` alebo `ETag`), aby dashboard neťahal celý zoznam pri každom cykle.
- **#248 Monitoring výroby a alerty (UI):** napojiť alarmovú sekciu na 60 s polling, keď bude API dostupné; produkčný graf držať na 5 min.
- **#271 Notifikačné centrum:** už rozhodnuté pre polling; nemeniť.
