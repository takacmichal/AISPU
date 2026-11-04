# Plan: #36 + #37 Visits and intervention (stories #124 to #129; epic #8), stacked on #35

Sources: mapping `docs/bpmn/mapovanie-entit.md` (`ServiceVisit` Planned/InProgress/Done/Cancelled, BPMN #67), ADR #207 (Technician RW, T on visit/report/verify), ADR #194, module `Features/Tickets`, device replacement of `Features/Installations`.

## Decisions
- Lives in `Features/Tickets` as `VisitsModule` (second IModule, same pattern as DevicesModule in Installations). No change to `Program.cs`, `AppDbContext`, CI.
- No "Pridelený" state (mapping is source of truth): assignment is its own record `ServiceTicketAssignment` (one row per ticket). Notification of the technician is not built (no notification module yet) - deviation.
- `ServiceVisit` (technician, planned window = start + estimated minutes, state, reschedule log with reason, protocol: cause from enum, findings, work done, arrival/departure), `ServiceVisitMaterial` (ticket + visit), `ServiceVisitDeviceReplacement` (link to the installation device replace flow, reused via `IDeviceService.ReplaceAsync` in one transaction), `ServiceTicketConfirmation` (customer confirm / reopen).
- Ticket methods: `ScheduleVisit`, `StartVisit`, `Verify`, `Close`, `Reopen` (Closed to RemoteDiagnosis within 7 days of the last close; not an edge of the automaton so the generic transition stays closed).
- Collision: same technician, visit Planned/InProgress, `a.start < b.end && b.start < a.end` gives 409.
- Technician acts only on own visits/tickets (other technician: 404); Admin/Operator bypass.
- Policies: new `TicketsField` (Admin, Operator, Technician) and `TicketsConfirm` (Customer); reuse `TicketsRead`, `TicketsManage`.
- Closing requires a Done visit protocol when the ticket had a visit; the remote path (BPMN note) closes without one.
- Cause `NoFaultFound` prepares the false-alarm statistics (Alerts module #113 to #119 does not exist yet).

## Endpoints
PUT /api/tickets/{id}/technician, POST|GET /api/tickets/{id}/visits, PUT /api/visits/{id}/schedule, GET /api/visits/{id}, POST /api/visits/{id}/start|report, PUT /api/visits/{id}/materials, POST /api/visits/{id}/device-replacements, POST /api/tickets/{id}/verify|close|confirm|reopen, GET /api/technicians/me/visits, GET /api/technicians/{technicianId}/visits.

## TDD
Unit tests (visit, ticket methods, requests) red, then entities/config/service/module, integration tests, policies + docs, migration `AddServiceVisits` last.
