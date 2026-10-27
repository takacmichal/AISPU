# Mapovanie BPMN procesov na entity a stavy systému (#69)

Tento dokument prepája úlohy z BPMN procesov L2 (#61 až #68) s entitami, stavmi a endpointmi systému SolarOps (.NET 10 API, Angular, Python ML, PostgreSQL). Cieľom je, aby každá úloha mala buď priradený stav entity, alebo endpoint, alebo odôvodnenie, prečo v systéme nemá efekt.

**Upozornenie:** všetky entity, stavy a endpointy v tomto dokumente sú **navrhované** (nie sú implementované). Slúžia ako vstup pre Sprint 1 až 3 a môžu sa pri implementácii spresniť. Stavy sú modelované ako enumy (prístup "stavy namiesto workflow enginu", pozri #207). Názvy stavov sú kódové identifikátory v angličtine. Kolónka Story odkazuje na existujúce stories v backlogu.

## Entity a stavy

Všetko navrhované.

| Entita (navrhovaná) | Stavy (enum) | Prechody (stručne) |
|---|---|---|
| `Customer` + `SupplyPoint` | `Customer`: `Active`, `Archived` | Zákazník a odberné miesto vznikajú pri dopyte (#70, #72). |
| `Quote` | `Draft`, `Sent`, `RevisionRequested`, `Accepted`, `Rejected`, `Expired` | `Draft → Sent → Accepted/Rejected`; `Sent → RevisionRequested → Draft` (#74, #75). |
| `FveDesign` | `Requested`, `SiteSurveyDone`, `Infeasible`, `InDesign`, `Rejected`, `Approved` | `Requested → SiteSurveyDone → InDesign → Approved`; `InDesign → InDesign` (prepracovanie); `SiteSurveyDone → Infeasible` (#76 až #78). |
| `Contract` | `Draft`, `PdfGenerated`, `Sent`, `Signed`, `Expired`, `Cancelled` | `Draft → PdfGenerated → Sent → Signed`; `Sent → Expired` (lehota na podpis) (#79 až #81). |
| `DepositPayment` (súčasť `Contract`) | `NotInvoiced`, `Invoiced`, `Paid`, `Overdue` | `NotInvoiced → Invoiced → Paid`; `Invoiced → Overdue` (#81). |
| `ConnectionRequest` | `Ready`, `Submitted`, `InfoRequested`, `Rejected`, `ContractOffered`, `ContractSigned`, `ContractSubmitted`, `Concluded` | `Ready → Submitted` až `Concluded`; povinné prílohy schéma a súhlas vlastníka (#85). Typ žiadosti: `SmallSource` (≤ 10,8 kW) alebo `LargeSource` (posúdenie, štúdia). Lehoty 10 pracovných dní / 30 dní (#85 až #87). |
| `Installation` | `Planned`, `ComponentsOrdered`, `ComponentsReceived`, `Mounting`, `MountingDone`, `Inspection`, `InspectionFailed`, `InspectionPassed`, `Commissioned`, `HandedOver` | Pozri tabuľky procesov #65 a #66 (#82, #88 až #93). |
| `Component` (panel, invertor) | `Ordered`, `Received`, `Installed`, `Faulty`, `Replaced` | Sériové čísla (#83, #84, #128). |
| `ConnectionMilestone` (súčasť `Installation`) | `FunctionalTestDone`, `MeterInstalled`, `ConnectionNotified` | Len pre väčší zdroj a podľa distribučky (#92). |
| `HandoverProtocol` | `Issued`, `Signed` | Preberací protokol (#93). |
| `ServiceTicket` | `New`, `Triaged`, `RemoteDiagnosis`, `VisitScheduled`, `OnSite`, `Verified`, `Resolved`, `Closed` | Pôvod: `CustomerReport`, `Manual`, `AnomalyDetected`. Pozri #121, #115, #120. |
| `ServiceVisit` | `Planned`, `InProgress`, `Done`, `Cancelled` | Výjazd technika, servisný protokol, použitý materiál (#125, #127, #128). |
| `Alert` (anomália) | `Open`, `UnderReview`, `Confirmed`, `FalsePositive`, `Closed` | Upozornenie ML na podvýrobu; `Confirmed` vytvorí `ServiceTicket`; `FalsePositive` je label pre ML (#113, #114, #119). |
| `Notification` | `Pending`, `Sent`, `Failed` | E-mail zákazníkovi / technikovi (#130, #131, #271). |
| `AppUser` (roly) | roly `Sales`, `Designer`, `Technician`, `Operator`, `Customer`, `Admin` | Mapovanie BPMN lanes na roly (#135, #207). |

Poznámka k lanes v tabuľkách: **O** = Obchod/Operátor (back office), **P** = Projektant, **T** = Technik, **Z** = Zákazník, **D** = Distribučka, **M** = ML služba. Lanes Z, D, M sú pooly (externé strany), v tabuľkách sa objavujú len ako správy.

## #61 Dopyt a cenová ponuka

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Zaznamenať zákazníka a odberné miesto | O | `Customer` → `Active`; `SupplyPoint` vznikne | `POST /api/customers`, `POST /api/customers/{id}/supply-points` | #70, #72 |
| Zostaviť cenovú ponuku z komponentov | O | `Quote` → `Draft` | `POST /api/quotes`, `PUT /api/quotes/{id}/items`, `GET /api/components` | #73, #74 |
| Odoslať ponuku zákazníkovi | O | `Quote` → `Sent` | `POST /api/quotes/{id}/send` | #75 |
| Odpoveď zákazníka (catch) a brána "Ponuka schválená?" | O | `Quote` → `Accepted` / `Rejected` / `RevisionRequested` (→ `Draft`) | `POST /api/quotes/{id}/accept`, `POST /api/quotes/{id}/reject`, `POST /api/quotes/{id}/request-revision` | #75 |

## #62 Návrh FVE projektantom

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Odovzdať podklady projektantovi | O | `FveDesign` → `Requested` | `POST /api/quotes/{id}/designs` | #76, #78 |
| Vykonať obhliadku objektu | P | `FveDesign` → `SiteSurveyDone` (poznámky, fotky) | `POST /api/designs/{id}/site-survey` | #76 |
| Brána "Realizovateľné?" (nie) | P | `FveDesign` → `Infeasible` | `POST /api/designs/{id}/mark-infeasible` | #76 |
| Zadať parametre strechy a spotreby | P | `FveDesign` → `InDesign` | `PUT /api/designs/{id}/parameters` | #76 |
| Navrhnúť konfiguráciu FVE a odhad výroby | P | `FveDesign` (`InDesign`), vypočítaný odhad výroby | `POST /api/designs/{id}/calculate` | #77 |
| Vypracovať jednopólovú schému | P | `FveDesign` (`InDesign`), príloha schémy | `POST /api/designs/{id}/attachments` | #77 |
| Schváliť návrh (brána "Návrh vyhovuje?" áno) | O | `FveDesign` → `Approved`; nie → `InDesign` (prepracovať) | `POST /api/designs/{id}/approve`, `POST /api/designs/{id}/rework` | #78 |
| Previazať návrh s ponukou | O | `FveDesign.QuoteId` ↔ `Quote` (`Accepted`) | `PUT /api/designs/{id}/quote` | #78 |

## #63 Uzatvorenie zmluvy a platba zálohy

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Vytvoriť zmluvu z prijatej ponuky | O | `Contract` → `Draft` | `POST /api/contracts` (z `quoteId`) | #79 |
| Vygenerovať PDF zmluvy | O | `Contract` → `PdfGenerated` | `POST /api/contracts/{id}/pdf`, `GET /api/contracts/{id}/pdf` | #80, #219 |
| Odoslať zmluvu zákazníkovi | O | `Contract` → `Sent` | `POST /api/contracts/{id}/send` | #79, #130 |
| Čakať na podpis (brána) + timer "Lehota na podpis vypršala" | O | `Contract` → `Expired` (plánovaná úloha) | `POST /api/contracts/{id}/expire` (volá background job) | #81 |
| Zaznamenať podpis | O | `Contract` → `Signed` | `POST /api/contracts/{id}/sign` | #81 |
| Vystaviť zálohovú faktúru | O | `DepositPayment` → `Invoiced` | `POST /api/contracts/{id}/deposit-invoice` | #81 |
| Čakať na úhradu (brána) + timer "Splatnosť vypršala" | O | `DepositPayment` → `Overdue` (job) | `POST /api/contracts/{id}/deposit/mark-overdue` | #81 |
| Zaznamenať úhradu zálohy | O | `DepositPayment` → `Paid` | `POST /api/contracts/{id}/deposit/pay` | #81 |

## #64 Žiadosť o pripojenie k distribučke

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Pripraviť podklady pre pripojenie | O | `ConnectionRequest` → `Ready` (povinné prílohy) | `POST /api/connection-requests` | #85, #217 |
| Brána "Výkon ≤ 10,8 kW?" | P | `ConnectionRequest.Type` = `SmallSource` / `LargeSource` | (súčasť `POST /api/connection-requests`, pole `type` odvodené z výkonu) | #85 |
| Pripraviť žiadosť pre malý zdroj (online) | P | `ConnectionRequest` (`Ready`, typ `SmallSource`) | `PUT /api/connection-requests/{id}` | #85 |
| Pripraviť žiadosť pre väčší zdroj (posúdenie, štúdia) | P | `ConnectionRequest` (`Ready`, typ `LargeSource`) | `PUT /api/connection-requests/{id}` | #85 |
| Podať žiadosť distribučke | P | `ConnectionRequest` → `Submitted` (začnú plynúť lehoty) | `POST /api/connection-requests/{id}/submit` | #85, #86 |
| Rozhodnutie distribučky (catch) + brána "Obsah rozhodnutia?" | P | `ConnectionRequest` → `InfoRequested` / `Rejected` / `ContractOffered` | `POST /api/connection-requests/{id}/decision` (simulácia distribučky) | #86, #87 |
| Doplniť a opraviť žiadosť | O | `ConnectionRequest` → `Submitted` (z `InfoRequested`) | `POST /api/connection-requests/{id}/resubmit` | #86 |
| Skontrolovať technické podmienky | P | `ConnectionRequest` (`ContractOffered`), kontrola podmienok | `POST /api/connection-requests/{id}/review-conditions` | #86 |
| Odoslať zmluvu o pripojení zákazníkovi | O | `ConnectionRequest` (`ContractOffered`) + notifikácia | `POST /api/connection-requests/{id}/send-contract` | #86, #130 |
| Podpísaná zmluva od zákazníka (catch) | O | `ConnectionRequest` → `ContractSigned` | `POST /api/connection-requests/{id}/customer-signed` | #86 |
| Zaslať podpísanú zmluvu distribučke | P | `ConnectionRequest` → `ContractSubmitted`, potom `Concluded` | `POST /api/connection-requests/{id}/submit-signed-contract`, `POST /api/connection-requests/{id}/conclude` | #86, #87 |

## #65 Inštalácia a revízia

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Naplánovať inštaláciu a termín | O | `Installation` → `Planned` (termín, technici) | `POST /api/installations`, `PUT /api/installations/{id}/schedule`, `PUT /api/installations/{id}/technicians` | #82, #88, #89 |
| Objednať komponenty | O | `Installation` → `ComponentsOrdered`; `Component` → `Ordered` | `POST /api/installations/{id}/components/order` | #83 |
| Prevziať panely a invertory (sériové čísla) | T | `Installation` → `ComponentsReceived`; `Component` → `Received` | `POST /api/installations/{id}/components/receive` | #83 |
| Montáž FVE (podproces) | T | `Installation` → `Mounting` → `MountingDone`; `Component` → `Installed`; checklist | `PUT /api/installations/{id}/checklist`, `POST /api/installations/{id}/strings` | #84, #90 |
| Chýba alebo je chybný komponent (error) | T | `Component` → `Faulty` | `POST /api/components/{id}/report-fault` | #83, #128 |
| Doobjednať alebo vymeniť komponent | T | `Component` → `Replaced` / `Ordered` | `POST /api/components/{id}/replace` | #128 |
| Revízia (podproces) | T | `Installation` → `Inspection` → `InspectionPassed` | `POST /api/installations/{id}/inspection`, `POST /api/installations/{id}/inspection-report` | #91 |
| Revízia neúspešná (error) | T | `Installation` → `InspectionFailed` | `POST /api/installations/{id}/inspection` (výsledok `Failed`) | #91 |
| Odstrániť nedostatky | T | `Installation` → `Mounting` (opakovanie) | `POST /api/installations/{id}/rework` | #90, #91 |

## #66 Uvedenie do prevádzky a odovzdanie

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Vykonať funkčnú skúšku s distribučkou | T | `ConnectionMilestone` → `FunctionalTestDone` (len väčší zdroj) | `POST /api/installations/{id}/connection-milestones/functional-test` | #92 |
| Pripraviť podklady k zmluve o dodávke | O | `Installation` (dokumenty), priložené podklady | `POST /api/installations/{id}/documents` | #93 |
| Elektromer namontovaný (catch) | T | `ConnectionMilestone` → `MeterInstalled` | `POST /api/installations/{id}/connection-milestones/meter-installed` | #92 |
| Oznámenie o pripojení (catch) | T | `ConnectionMilestone` → `ConnectionNotified` | `POST /api/installations/{id}/connection-milestones/connection-notified` | #92 |
| Spustiť FVE a overiť výrobu | T | `Installation` → `Commissioned`; prvé merania v telemetrii | `POST /api/installations/{id}/commission` | #92, #95 |
| Odovzdať FVE a preberací protokol zákazníkovi | T | `HandoverProtocol` → `Issued` | `POST /api/installations/{id}/handover-protocol` | #93 |
| Podpísaný preberací protokol (catch) | O | `HandoverProtocol` → `Signed`; `Installation` → `HandedOver` | `POST /api/installations/{id}/handover-protocol/sign` | #93 |
| FVE v prevádzke: aktivovať monitoring (koncová správa) | O | `Installation.MonitoringEnabled` = `true` | `PUT /api/installations/{id}/monitoring` | #92, #26 |

## #67 Servisný proces z hlásenia zákazníka

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Prijať hlásenie a vytvoriť tiket | O | `ServiceTicket` → `New` (pôvod `CustomerReport`) | `POST /api/tickets` | #120, #121 |
| Roztriediť tiket (typ, priorita) | O | `ServiceTicket` → `Triaged` | `PUT /api/tickets/{id}/triage` | #121, #124 |
| Diagnostikovať poruchu na diaľku | O | `ServiceTicket` → `RemoteDiagnosis` (komentár) | `POST /api/tickets/{id}/comments`, `POST /api/tickets/{id}/transition` | #121, #123 |
| Naplánovať výjazd technika | T | `ServiceVisit` → `Planned`; `ServiceTicket` → `VisitScheduled` | `POST /api/tickets/{id}/visits` | #124, #125, #131 |
| Opraviť alebo vymeniť komponent na mieste | T | `ServiceVisit` → `InProgress`; `ServiceTicket` → `OnSite`; `Component` → `Replaced` | `PUT /api/visits/{id}/materials` | #127, #128 |
| Zapísať servisný zásah | T | `ServiceVisit` → `Done` | `POST /api/visits/{id}/report` | #127, #156 |
| Overiť obnovenie výroby | T | `ServiceTicket` → `Verified` | `POST /api/tickets/{id}/verify` | #127 |
| Uzavrieť tiket a informovať zákazníka | O | `ServiceTicket` → `Resolved` → `Closed`; `Notification` → `Sent` | `POST /api/tickets/{id}/close` | #129, #130 |

Poznámka: úloha "Vyriešené na diaľku?" je brána, ktorá nie je úloha; jej vetva "áno" používa `POST /api/tickets/{id}/close` priamo z `RemoteDiagnosis`.

## #68 Automatický servis z detekcie podvýroby

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Upozornenie na podvýrobu (štart, správa z ML) | O | `Alert` → `Open` | `POST /api/alerts` (volá ML služba / .NET job) | #113, #112 |
| Preveriť upozornenie (dáta, počasie, výpadok komunikácie) | O | `Alert` → `UnderReview` | `GET /api/alerts/{id}`, `POST /api/alerts/{id}/review` | #114, #251, #252 |
| Falošný poplach zaznamenaný | O | `Alert` → `FalsePositive` (label pre ML) | `POST /api/alerts/{id}/feedback` | #119, #260 |
| Vytvoriť servisný tiket | O | `Alert` → `Confirmed`; `ServiceTicket` → `New` (pôvod `AnomalyDetected`, priorita podľa straty výroby) | `POST /api/alerts/{id}/create-ticket` | #115, #116, #117 |
| Servisný proces (#67) (call activity) | O | `ServiceTicket` prechádza stavmi z #67; po uzavretí `Alert` → `Closed` | pozri #67 | #121, #129 |

## Nezmapované úlohy

Nižšie sú položky BPMN, ktoré nemajú vlastný stav ani endpoint, s odôvodnením. Brány (gateway), udalosti (start, koncové, správy, timery) a pooly zákazníka, distribučky a ML nie sú úlohy a v tabuľkách sú uvedené len tam, kde spúšťajú zmenu stavu.

| Úloha (proces) | Dôvod nezmapovania |
|---|---|
| Vykonať obhliadku objektu (#62), fyzická časť | Fyzická práca na mieste. V systéme sa eviduje iba výsledok (`SiteSurveyDone`, poznámky, fotky), samotná obhliadka nie je krok systému. |
| Montáž FVE: konštrukcia, panely, stringy, invertor (#65), fyzická časť | Fyzická práca technikov. Systém eviduje len stav a checklist, nie samotnú montáž (#90). |
| Odborná prehliadka a skúška revízie (#65) | Vykonáva externý revízny technik. Systém zaznamená len výsledok a revíznu správu (#91). |
| Opraviť alebo vymeniť komponent na mieste (#67), fyzická časť | Fyzická práca. Systém eviduje zásah, materiál a výmenu zariadení (#127, #128). |
| Vykonať funkčnú skúšku s distribučkou (#66), fyzická časť | Skúšku vykonáva technik spolu s distribučkou. Systém ukladá len protokol a stav `FunctionalTestDone`. |
| Elektromer namontovaný, Oznámenie o pripojení (#66) | Akcie externej strany (distribučka). Systém ich nevykonáva, iba ich zaznamená po oznámení (manuálny zápis alebo simulácia, #87). |
| Rozhodnutie distribučky (#64) | Externá strana. V rámci školského projektu simulované (#87), nie je to reálna integrácia. |
| Podpis zmluvy a úhrada zálohy zákazníkom (#63) | Akcia zákazníka mimo systému (podpis, bankový prevod). Systém ju len eviduje (`Signed`, `Paid`). |
| Naplánovať výjazd technika: dohodnutie termínu so zákazníkom (#67) | Telefonická komunikácia mimo systému. Do systému sa zapisuje už len výsledný termín (#125). |
| Pripraviť podklady k zmluve o dodávke (#66) | Podklady slúžia pre externú zmluvu o dodávke elektriny mimo rozsahu SolarOps. Systém ich môže len priložiť ako dokument (#93), nemá vlastný stav. |
| Vypracovať jednopólovú schému (#62) | Odborná práca v externom CAD nástroji. Systém ukladá len výsledný súbor ako prílohu, bez vlastného stavu. |
| Vystaviť zálohovú faktúru (#63) | Účtovníctvo nie je súčasťou SolarOps. Systém eviduje len stav `DepositPayment` (`Invoiced`), faktúru nevystavuje samostatný modul. Chýba dedikovaná story, odporúča sa doplniť pod #81. |
| Doobjednať alebo vymeniť komponent / Objednať komponenty (#65) | Objednávka u dodávateľa je externý proces. Systém eviduje len stav komponentu (`Ordered`, `Received`, `Replaced`), nie objednávku ako samostatnú entitu. |
