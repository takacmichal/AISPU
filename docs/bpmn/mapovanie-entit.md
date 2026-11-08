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
| `ConnectionRequest` | `Draft`, `Submitted`, `InfoRequested`, `Rejected`, `ContractOffered`, `ContractSigned`, `ContractSubmitted`, `Concluded` | Typ žiadosti: `SmallSource` (≤ 10,8 kW) alebo `LargeSource` (posúdenie, štúdia). Lehoty 10 pracovných dní / 30 dní (#85 až #87). `Draft` zodpovedá stavu „Pripravená“ z ticketu #85; prílohy (`SingleLineDiagram`, `OwnerConsent`) sa ukladajú aj s obsahom v DB (#85). |
| `Installation` | `Planned`, `ComponentsOrdered`, `ComponentsReceived`, `Mounting`, `MountingDone`, `Inspection`, `InspectionFailed`, `InspectionPassed`, `Commissioned`, `HandedOver` | Pozri tabuľky procesov #65 a #66 (#82, #88 až #93). |
| `Component` (panel, invertor) | `Ordered`, `Received`, `Installed`, `Faulty`, `Replaced` | Sériové čísla (#83, #84, #128). |
| `ConnectionMilestone` (súčasť `Installation`) | `FunctionalTestDone`, `MeterInstalled`, `ConnectionNotified` | Len pre väčší zdroj a podľa distribučky (#92). |
| `HandoverProtocol` | `Issued`, `Signed` | Preberací protokol (#93). |
| `ServiceTicket` | `New`, `Triaged`, `RemoteDiagnosis`, `VisitScheduled`, `OnSite`, `Verified`, `Resolved`, `Closed` | Pôvod: `CustomerReport`, `Manual`, `AnomalyDetected`. Pozri #121, #115, #120. |
| `ServiceVisit` | `Planned`, `InProgress`, `Done`, `Cancelled` | Výjazd technika, servisný protokol, použitý materiál (#125, #127, #128). |
| `Alert` (anomália) | `Open`, `UnderReview`, `Confirmed`, `FalsePositive`, `Closed` | Upozornenie ML na podvýrobu; `Confirmed` vytvorí `ServiceTicket`; `FalsePositive` je label pre ML (#113, #114, #119). |
| `Notification` | `Pending`, `Sent`, `Failed` | E-mail zákazníkovi / technikovi (#130, #131, #271). |
| `AppUser` (roly) | roly `Sales`, `Designer`, `Technician`, `Operator`, `Customer`, `Admin` | Mapovanie BPMN lanes na roly (#135, #207). |

Poznámka k lanes v tabuľkách: **O** = Obchod/Operátor (back office), **P** = Projektant, **T** = Technik, **Z** = Zákazník, **D** = Distribučka, **M** = ML služba. **S** = Systém (automaticky, #68), **Op** = Operátor (#68). D a M sú zbalené pooly (externé strany), v tabuľkách sa objavujú len ako správy. Bazén Zákazník je od Sprintu 4 na stranách #61 až #67 a na L1 rozbalený: úlohy zákazníka sú v tabuľkách s lane Z. Zákazník ich robí na portáli alebo mimo systému (podpis, platba).

## #61 Dopyt a cenová ponuka

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Zaznamenať zákazníka a odberné miesto | O | `Customer` → `Active`; `SupplyPoint` vznikne | `POST /api/customers`, `POST /api/customers/{id}/supply-points` | #70, #72 |
| Overiť odberné miesto (EIC, hlavný istič) | O | `SupplyPoint` (EIC, istič, distribučka) | `PUT /api/customers/{customerId}/supply-points/{id}` | #72 |
| Zostaviť cenovú ponuku z komponentov | O | `Quote` → `Draft` | `POST /api/quotes`, `PUT /api/quotes/{id}/items`, `GET /api/components` | #73, #74 |
| Odoslať ponuku zákazníkovi | O | `Quote` → `Sent` | `POST /api/quotes/{id}/send` | #75 |
| Odpoveď zákazníka: brána riadená udalosťami "Čakať na odpoveď" (prijatá, zamietnutá, žiadosť o úpravu) | O | `Quote` → `Accepted` / `Rejected`; úprava = nová verzia ponuky v `Draft` | `POST /api/quotes/{id}/accept`, `POST /api/quotes/{id}/reject` | #75 |
| Platnosť ponuky uplynula (timer) → Označiť ponuku ako expirovanú | O | `Quote` → `Expired` (platnosť predvolene 30 dní, `Quotes:DefaultValidityDays`) | automaticky `QuoteExpirationService` | #75 |
| Vyplniť dopyt (web, telefón) | Z | podklad pre `Customer` a `SupplyPoint` | – (zapíše Obchod) | #70 |
| Posúdiť cenovú ponuku | Z | `Quote` (`Sent`), zákazník vidí svoju ponuku | `GET /api/quotes/{id}` | #75 |
| Prijať ponuku / Odmietnuť ponuku | Z | `Quote` → `Accepted` / `Rejected` | `POST /api/quotes/{id}/accept`, `POST /api/quotes/{id}/reject` | #75 |
| Požiadať o úpravu ponuky | Z | žiadna (komunikácia mimo systému) | – | #75 |

## #62 Návrh FVE projektantom

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Odovzdať podklady projektantovi | O | `FveDesign` → `Requested` | `POST /api/quotes/{id}/designs` | #76, #78 |
| Dohodnúť termín obhliadky so zákazníkom | P | žiadna (telefón, e-mail) | – | #76 |
| Potvrdiť termín a sprístupniť objekt | Z | žiadna (mimo systému) | – | #76 |
| Vykonať obhliadku objektu | P | `FveDesign` → `SiteSurveyDone` (poznámky, fotky) | `POST /api/designs/{id}/site-survey` | #76 |
| Brána "Realizovateľné?" (nie) | P | `FveDesign` → `Infeasible` | `POST /api/designs/{id}/mark-infeasible` | #76 |
| Zadať parametre strechy a spotreby | P | `FveDesign` → `InDesign` | `PUT /api/designs/{id}/parameters` | #76 |
| Navrhnúť konfiguráciu FVE a odhad výroby | P | `FveDesign` (`InDesign`), vypočítaný odhad výroby | `POST /api/designs/{id}/calculate` | #77 |
| Vypracovať jednopólovú schému | P | `FveDesign` (`InDesign`), príloha schémy | `POST /api/designs/{id}/attachments` | #77 |
| Schváliť návrh (brána "Návrh vyhovuje?" áno) | O | `FveDesign` → `Approved`; nie → `InDesign` (prepracovať) | `POST /api/designs/{id}/approve`, `POST /api/designs/{id}/rework` (implementované v #78; schvaľuje Admin/Obchod, schválenie eviduje autora a verziu) | #78 |
| Previazať návrh s ponukou | O | `Quote.DesignId` + `DesignVersionNumber` (väzba je na ponuke, nie na návrhu); zmena návrhu nastaví `Quote.NeedsRecalculation` | `POST /api/designs/{id}/quote` (vytvorí `Draft` ponuku zo schváleného návrhu) | #78 |

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
| Poslať upomienku zálohy | O | žiadna (e-mail mimo systému, v aplikácii nie je) | – | #81 |
| Založiť inštaláciu k zmluve | O | `Installation` → `Planned` (jedna inštalácia na zmluvu; objednať komponenty sa dá až po úhrade zálohy) | `POST /api/installations` | #82 |
| Skontrolovať a podpísať zmluvu | Z | `Contract` → `Signed` (zapíše Obchod) | `GET /api/contracts/{id}/pdf` | #79, #81 |
| Uhradiť zálohu | Z | `DepositStatus` → `Paid` (zapíše Obchod) | – (bankový prevod) | #81 |

## #64 Žiadosť o pripojenie k distribučke

| Úloha | Lane | Entita → stav | Endpoint (navrhovaný) | Story |
|---|---|---|---|---|
| Pripraviť podklady pre pripojenie | O | `ConnectionRequest` → `Draft` | `POST /api/connection-requests` | #85, #217 |
| Brána "Výkon ≤ 10,8 kW?" | P | `ConnectionRequest.Type` = `SmallSource` / `LargeSource` | (súčasť `POST /api/connection-requests`, pole `type` odvodené z výkonu) | #85 |
| Pripraviť žiadosť pre malý zdroj (online) | P | `ConnectionRequest` (`Draft`, typ `SmallSource`) | `PUT /api/connection-requests/{id}` | #85 |
| Pripraviť žiadosť pre väčší zdroj (posúdenie, štúdia) | P | `ConnectionRequest` (`Draft`, typ `LargeSource`) | `PUT /api/connection-requests/{id}` | #85 |
| Podať žiadosť distribučke | P | `ConnectionRequest` → `Submitted` (začnú plynúť lehoty) | `POST /api/connection-requests/{id}/submit` | #85, #86 |
| Rozhodnutie distribučky (catch) + brána "Obsah rozhodnutia?" | P | `ConnectionRequest` → `InfoRequested` / `Rejected` / `ContractOffered` | `POST /api/connection-requests/{id}/decision` (simulácia distribučky) | #86, #87 |
| Doplniť a opraviť žiadosť | O | `ConnectionRequest` → `Submitted` (z `InfoRequested`) | `POST /api/connection-requests/{id}/resubmit` | #86 |
| Skontrolovať technické podmienky | P | `ConnectionRequest` (`ContractOffered`), kontrola podmienok | `POST /api/connection-requests/{id}/review-conditions` | #86 |
| Odoslať zmluvu o pripojení zákazníkovi | O | `ConnectionRequest` (`ContractOffered`) + notifikácia | `POST /api/connection-requests/{id}/send-contract` | #86, #130 |
| Podpísaná zmluva od zákazníka (catch) | O | `ConnectionRequest` → `ContractSigned` | `POST /api/connection-requests/{id}/customer-signed` | #86 |
| Dodať súhlas vlastníka objektu a podklady | Z | prílohy `ConnectionRequest` | – (podklady zapíše Obchod ako prílohy) | #85, #217 |
| Podpísať zmluvu o pripojení | Z | `ConnectionRequest` → `ContractSigned` (zapíše Obchod) | – | #86 |
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

## #65a Podproces Montáž FVE

Rozkreslenie podprocesu Montáž FVE zo strany #65. Endpointy v tejto a nasledujúcej sekcii sú už **implementované**. Kroky 1 až 9 checklistu sú povinné, fotodokumentácia a upratanie voliteľné (`ChecklistTemplate` v `Features/Installations/InstallationChecklist.cs`).

| Úloha | Lane | Entita → stav | Endpoint | Story |
|---|---|---|---|---|
| Skontrolovať dodané komponenty na mieste | T | `InstallationDevice` (sériové čísla) | `GET /api/installations/{id}/devices` | #83 |
| Chýba alebo je chybný komponent (error) | T | výmena zariadenia | `POST /api/installations/{id}/devices/{deviceId}/replace` | #128 |
| Zabezpečiť pracovisko a BOZP | T | checklist `SiteSafety` | `PUT /api/installations/{id}/checklist/SiteSafety` | #90 |
| Namontovať nosnú konštrukciu | T | checklist `MountingStructure` | `PUT /api/installations/{id}/checklist/MountingStructure` | #90 |
| Osadiť a upevniť panely | T | checklist `PanelsMounted` | `PUT /api/installations/{id}/checklist/PanelsMounted` | #90 |
| Zapojiť stringy a DC kabeláž | T | checklist `DcCabling`; `PanelString` | `PUT /api/installations/{id}/checklist/DcCabling`, `POST /api/installations/{id}/strings` | #84, #90 |
| Namontovať a zapojiť striedač | T | checklist `InverterMounted` | `PUT /api/installations/{id}/checklist/InverterMounted` | #90 |
| Osadiť DC a AC ochrany a odpojovač | T | checklist `Protections` | `PUT /api/installations/{id}/checklist/Protections` | #90 |
| Pripojiť AC stranu do rozvádzača | T | checklist `AcConnection` | `PUT /api/installations/{id}/checklist/AcConnection` | #90 |
| Uzemniť a pospájať, ochrana pred bleskom | T | checklist `Grounding` | `PUT /api/installations/{id}/checklist/Grounding` | #90 |
| Zmerať izolačný odpor a polaritu stringov | T | checklist `StringMeasurements` | `PUT /api/installations/{id}/checklist/StringMeasurements` | #90 |
| Nafotiť dokumentáciu montáže (voliteľné) | T | checklist `PhotoDocumentation` | `PUT /api/installations/{id}/checklist/PhotoDocumentation` | #90 |
| Upratať pracovisko (voliteľné) | T | checklist `SiteCleanup` | `PUT /api/installations/{id}/checklist/SiteCleanup` | #90 |
| Dokončiť a odškrtnúť chýbajúce kroky | T | checklist, `MissingRequiredChecklistSteps` | `GET /api/installations/{id}/checklist` | #90 |
| Označiť montáž ako dokončenú | T | `Installation` → `MountingDone` (409, ak chýba povinný krok) | `POST /api/installations/{id}/transitions` (`MountingDone`) | #90 |

## #65b Podproces Revízia

Rozkreslenie podprocesu Revízia zo strany #65. Revíziu robí externý revízny technik (rozbalený bazén), SolarOps eviduje jej výsledok.

| Úloha | Lane | Entita → stav | Endpoint | Story |
|---|---|---|---|---|
| Odovzdať inštaláciu na revíziu (stav Revízia) | T | `Installation` → `Inspection` | `POST /api/installations/{id}/transitions` (`Inspection`) | #91 |
| Objednať revízneho technika a odovzdať podklady | T | žiadna (mimo systému) | podklady: `GET /api/installations/{id}/documentation` | #91, #93 |
| Vykonať vizuálnu prehliadku FVE | Revízny technik | žiadna (externá strana) | – | #91 |
| Zmerať izolačný odpor a uzemnenie | Revízny technik | žiadna (externá strana) | – | #91 |
| Vyskúšať ochrany a funkciu striedača | Revízny technik | žiadna (externá strana) | – | #91 |
| Vypracovať a podpísať revíznu správu | Revízny technik | žiadna (externá strana) | – | #91 |
| Zaevidovať revíznu správu (výsledok, platnosť, súbor) | T | `RevisionReport`; `Installation` → `InspectionPassed` / `InspectionFailed` | `POST /api/installations/{id}/revision-reports` | #91 |

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
| Uzavrieť zmluvu o dodávke elektriny s dodávateľom | Z | žiadna (zmluva s dodávateľom elektriny je mimo SolarOps) | – | #93 |
| Prevziať FVE a podpísať preberací protokol | Z | `HandoverProtocol` → `Signed` | `GET /api/installations/{id}/documentation/handover-protocol` | #93 |
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

| Nahlásiť poruchu (portál, telefón) | Z | `ServiceTicket` → `New` (pôvod `CustomerReport`) | `POST /api/tickets` (rola Customer, len vlastná inštalácia) | #120 |
| Potvrdiť termín a sprístupniť FVE | Z | `ServiceVisit` (termín) | – (potvrdenie mimo systému) | #125 |

Poznámka: úloha "Vyriešené na diaľku?" je brána, ktorá nie je úloha; jej vetva "áno" používa `POST /api/tickets/{id}/close` priamo z `RemoteDiagnosis`.

## #68 Automatický servis z detekcie podvýroby

Strana zodpovedá implementácii zo Sprintu 3 a 4: alarm aj tiket vytvára plánovaná analýza bez zásahu človeka (`MlScheduledAnalysis`, každú hodinu, v Azure ako Container Apps Job). Operátor tiket až preveruje. Endpointy v tejto sekcii sú **implementované**.

| Úloha | Lane | Entita → stav | Endpoint | Story |
|---|---|---|---|---|
| Každú hodinu (časovač, štart) | S | – | `MlScheduledAnalysis`, `MlAnalysisJob` | #33 |
| Načítať telemetriu a predpoveď počasia | S | 5-min merania (ADR-185), `WeatherForecast` | `POST /api/telemetry` (príjem), Open-Meteo (ADR-180) | #95 |
| Predikovať očakávanú výrobu | S | `StoredPrediction` | ML služba (predikcia); ručne `POST /api/installations/{installationId}/analysis` | #109, #110, #111 |
| Vyhodnotiť podvýrobu (pod 85 % predikcie) | S | – | ML služba (detekcia, ADR #194) | #112, #113, #114 |
| Vytvoriť alebo aktualizovať alarm podvýroby | S | `UnderproductionAlarm` (stabilné id pre inštaláciu a začiatok) | `GET /api/underproduction-alarms` | #113, #251 |
| Otvoriť servisný tiket s prioritou | S | `ServiceTicket` → `New` (pôvod z detekcie, priorita podľa straty, `AlarmTicketRules`) | interné (`OpenOrSupplementAsync`) | #33, #115, #117 |
| Eskalovať prioritu existujúceho tiketu | S | `ServiceTicket.Priority` len zvýšiť | interné (`EscalateAsync`) | #116, #117 |
| Preveriť alarm (dáta, počasie, výpadok komunikácie) | Op | `ServiceTicket` → `Triaged` | `PUT /api/tickets/{id}/triage` | #121, #251, #252 |
| Uzavrieť tiket ako falošný poplach | Op | `ServiceTicket` → `Closed` (poznámka) | `POST /api/tickets/{id}/transition` | #129 |
| Servisný proces (#67) (call activity) | Op | `ServiceTicket` prechádza stavmi z #67 | pozri #67 | #121, #129 |

Spätná väzba „falošný poplach“ do ML (pôvodne `Alert` → `FalsePositive`) v aplikácii nie je, preto ju diagram už nemá.

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
