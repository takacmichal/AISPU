# Simulátor distribučky (mock odpovede)

Distribučka (ZSD, SSD, VSD) nie je v SolarOps napojená. Proces pripojenia z BPMN #64 sa preto dá predviesť pomocou dvoch mock endpointov, ktoré hrajú odpovede distribučky. Volajú tie isté doménové prechody (`RequestInfo`, `Reject`, `OfferContract`, `Conclude` na entite `ConnectionRequest`), aké by vyvolala reálna odpoveď, takže stavy, lehoty a poznámky sa správajú rovnako.

## Dostupnosť

- Roly: `Admin`, `Operator`. Ostatné roly dostanú 403, bez prihlásenia 401.
- Zapnuté vo všetkých prostrediach okrem `Production`. Prepísať sa dá konfiguráciou `ConnectionRequest:Simulator:Enabled` (`true` alebo `false`), napr. premennou prostredia `ConnectionRequest__Simulator__Enabled=true`. Vypnutý simulátor odpovedá 404, akoby endpoint neexistoval.

## Endpointy

| Endpoint | Prechod |
|---|---|
| `POST /api/connection-requests/{id}/decision` | `Submitted` na `ContractOffered`, `InfoRequested` alebo `Rejected` |
| `POST /api/connection-requests/{id}/conclude` | `ContractSubmitted` na `Concluded` |

Telo `decision`:

```json
{ "decision": "Approve" }
{ "decision": "RequestInfo", "note": "Chýba datasheet meniča." }
{ "decision": "Reject", "note": "Nie je voľná kapacita siete." }
```

- `decision`: `Approve` (schváliť, ponúknuť zmluvu), `RequestInfo` (vyžiadať doplnenie), `Reject` (zamietnuť).
- `note`: povinná pri `RequestInfo` a `Reject` (najviac 1000 znakov), pri `Approve` voliteľná. Chýbajúca poznámka dáva 400.
- Rozhodnúť sa dá len o žiadosti v stave `Submitted` (inak 409). Rozhodnutím sa lehota na návrh zmluvy zastaví (`proposalRemainingDays` je `null`).

## Postup demonštrácie procesu

1. Vytvoriť žiadosť (`POST /api/connection-requests`, Obchod/Operátor), podať ju distribučke (`POST .../submit`, Projektant). Začne bežať lehota: 10 pracovných dní pre malý zdroj, 30 dní pre väčší (konfigurácia `ConnectionRequest:SmallSource:ProposalWorkingDays`, `ConnectionRequest:LargeSource:ProposalCalendarDays`).
2. Odpoveď distribučky simulovať cez `decision`:
   - `RequestInfo`: žiadosť opraviť (`PUT`), poslať znova (`POST .../resubmit`), lehota sa spustí odznova.
   - `Reject`: koniec, vznikne nová žiadosť.
   - `Approve`: pokračovať krokmi nižšie.
3. Po schválení: `review-conditions` (Projektant), `send-contract` a `customer-signed` (Obchod), `submit-signed-contract` (Projektant).
4. Záver distribučky simulovať cez `conclude`; žiadosť je v stave `Concluded`.

Ukážkové requesty sú v `src/SolarOps.Api/Features/ConnectionRequests/ConnectionRequests.http`, kontrakt v Scalar UI (`/scalar/v1`).

## Obmedzenia

Simulátor neposiela nič von a nemá vlastný čas ani oneskorenie odpovede; rozhodnutie zadáva používateľ. Prekročenie lehoty sa dá predviesť tak, že sa žiadosť nechá v stave `Submitted` (príznak `proposalOverdue` a filter `GET /api/connection-requests?overdue=true`).
