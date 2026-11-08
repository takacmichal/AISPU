# Ochrana osobných údajov a audit zmien (#140)

Tento dokument popisuje, ako backend spĺňa požiadavky story #140: audit zmien zákazníkov a zmlúv, žiadne heslá
ani tokeny v auditných logoch a anonymizáciu zákazníka.

## Audit zákazníka

Každá zmena zákazníka zapisuje riadok do `customer_audit_entries` v tej istej transakcii ako zmenu entity:

| Stĺpec | Význam |
|---|---|
| `id` | Guid v7 |
| `customer_id` | zákazník, ktorého sa zmena týka |
| `action` | `Created`, `Updated`, `Archived`, `Reactivated`, `Anonymized` |
| `performed_by` | id prihláseného používateľa |
| `performed_at` | čas zmeny v UTC |
| `details` | názvy zmenených polí alebo opis prechodu stavu; **nikdy hodnoty osobných údajov** |

Audit sa číta cez `GET /api/customers/{id}/audit` (len `AdminOnly`) a je zoradený od najstaršieho záznamu.

## Audit zmluvy

Zmluvy už majú audit (`contract_audit_entries`): `Created`, `Signed`, `DepositPaid`, s `performed_by`,
`performed_at` a opisom (`details`). Zmluva sa po vytvorení needituje, preto iné zmeny nevznikajú.
Audit sa číta cez `GET /api/contracts/{id}/audit`.

## Žiadne heslá ani tokeny

- Auditné tabuľky nemajú stĺpce pre heslá, hash-e ani tokeny.
- `details` zákazníka obsahuje len whitelist názvov polí (`FirstName`, `Email`, `Address`, …), nie ich hodnoty.
- Zmluvné `details` skladá výhradne doménový kód z identifikátorov, dátumov a súm.
- Integračný test `AuditTables_DoNotContainCredentials` overuje schému oboch auditných tabuliek.

## Anonymizácia zákazníka

`POST /api/customers/{id}/anonymize` (len `AdminOnly`) vymaže osobné údaje a zákazníka archivuje:

- vyprázdni mená, obchodné údaje, IČO/DIČ/IČ DPH, telefón, poznámku a adresu,
- e-mail nahradí technickou adresou `anonymized-{id}@anonymized.invalid`,
- odpojí prihlasovací účet (`user_id = null`), samotný účet sa nemaže,
- nastaví `anonymized_at`, takže ďalšia anonymizácia, úprava ani reaktivácia vrátia `409`,
- zachová `id`, časové značky a celý audit; zmluvy, ponuky a inštalácie tak nestratia referenciu.

Zákazník sa po anonymizácii zobrazuje ako „Anonymizovaný zákazník“. Anonymizovaný zákazník sa nedá znova
aktivovať, takže z neho nevzniknú nové ponuky ani zmluvy.
