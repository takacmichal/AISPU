# Plan: #85 Create a connection request (parent #23)

Sources: ADR spike #217 (model, thresholds, attachments), `docs/bpmn/mapovanie-entit.md` (#64 rows), conventions of `Features/Installations` and `Features/Contracts`.

## Decisions
- New module `Features/ConnectionRequests` (`ConnectionRequestsModule : IModule`), no changes to `Program.cs`, `AppDbContext`, CI. No migration (README: `EnsureCreated` in Sprint 1).
- `ConnectionRequestState` = the 8 states from the ADR; only `Draft` is produced here. State "Pripravená" from the issue maps to ADR `Draft` ("Pripraviť podklady"); no transitions (submit is #86).
- `ConnectionRequestType` `SmallSource` / `LargeSource`, derived from rated AC power (kW) of inverters; threshold 10.8 kW (`ConnectionRequest:SmallSourceMaxKw`, options with default 10.8, per ADR). Deadlines are NOT implemented (#86).
- Linked to an installation by id; the installation must exist (else 400, as Contracts does for a missing quote). CustomerId and SupplyPointId are copied from the installation.
- Attachments: the ADR defines no file storage. Store metadata only (kind, file name, content type, size); no upload endpoint, no bytes. Open question for the team.
- Required kinds: `SingleLineDiagram` (jednopólová schéma) and `OwnerConsent`; optional: `Datasheet`, `SwitchboardPhoto`, `PowerOfAttorney`, `CadastreExtract` (ADR list). A missing required kind gives 400.
- Endpoints: `POST /api/connection-requests` (201), `GET /api/connection-requests/{id}` (404). Write: Admin, Operator, Sales. Read: those + Designer, Technician; Customer sees only its own (ICustomerScope).

## TDD steps
1. Unit tests `Unit/ConnectionRequests/ConnectionRequestTests.cs` (type threshold, state Draft, required kinds) - red.
2. Integration tests `Integration/ConnectionRequests/ConnectionRequestEndpointsTests.cs` (201 Draft + linked, 400 missing attachment, 400 unknown installation, 404, authz, customer scope) - red.
3. Implement entity, config, options, DTOs, service, module, `.http`.
4. `dotnet build`, `dotnet test`.
