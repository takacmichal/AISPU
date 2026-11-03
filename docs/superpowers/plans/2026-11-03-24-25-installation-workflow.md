# Plán: plánovanie, montáž, revízia a uvedenie do prevádzky (#24, #25)

Stories: #88, #89, #90, #91, #92, #93. Modul `Features/Installations` sa rozširuje, nové politiky netreba
(InstallationsRead / Plan / Field).

## Doplnenie #88 a #89 (časť už existuje)

- Termín: začiatok nesmie byť v minulosti (ak sa mení), koniec nesmie byť v minulosti (entita `Schedule`).
- Plánovanie len pri schválenej žiadosti o pripojenie (`ContractOffered` a ďalšie stavy), inak 409.
- Kolízia termínu technika s inou neodovzdanou inštaláciou vráti 409 (pri plánovaní aj pri pridelení).
- `GET /api/installations/technicians` (Plan): zoznam technikov pre plánovača.
- Upozornenie zákazníka: notifikácie nie sú (#130), zákazník vidí termín v detaile. Uvedené ako odchýlka.

## #90 Checklist montáže

- Šablóna v kóde (11 krokov, 9 povinných); v DB len stav kroku (`installation_checklist_items`).
- `GET /api/installations/{id}/checklist` (Read), `PUT /api/installations/{id}/checklist/{stepCode}` (Field), len v stave `Mounting`.
- `MountingDone` vyžaduje všetky povinné kroky; zmeny majú `changedAt`, `changedBy`.

## #91 Revízna správa

- `installation_revision_reports`: dátum, revízny technik, výsledok, platnosť, súbor (PDF/JPEG/PNG, max 5 MB, SHA-256).
- `POST /api/installations/{id}/revision-reports` (Field) len v `Inspection`: výsledok prepne na `InspectionPassed` / `InspectionFailed`.
- `GET …/revision-reports` (Read), `GET …/revision-reports/{reportId}/file` (Read, nosniff, zaznamenané stiahnutie).

## #92 Uvedenie do prevádzky

- `POST /api/installations/{id}/commission` (Field): vyhovujúca a platná revízia, schválená žiadosť, preberací protokol
  (dátum, kto prevzal, poznámka); `Commissioned`, `MonitoringEnabled = true`; filter `monitoringEnabled` v zozname.

## #93 Dokumentácia

- `GET /api/installations/{id}/documentation` (Read): revízia, protokol, zoznam zariadení, história stiahnutí (od `Commissioned`).
- `GET /api/installations/{id}/documentation/protocol.pdf` (Read, QuestPDF): protokol so zoznamom zariadení; stiahnutie sa zapíše.

## Generický prechod

`/transitions` nesmie obísť pravidlá: `InspectionPassed`/`InspectionFailed` len cez revíznu správu, `Commissioned` len cez
`/commission`, `MountingDone` len so splneným checklistom.

## Testy

Unit (entita, šablóna, DTO validácia), integračné (úspech, 400, 404, 409, 401, 403, cudzí zákazník 404), pevné hodiny.
Migrácia `AddInstallationWorkflow`.
