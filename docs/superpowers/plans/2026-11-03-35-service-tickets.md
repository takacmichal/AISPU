# Plan: #35 Service tickets (stories #120, #121, #122, #123; epic #8)

Sources: `docs/bpmn/mapovanie-entit.md` (`ServiceTicket`, process #67 and #68), BPMN #67, ADR spike #207 (roles, state enums, audit), ADR spike #194 (priority Low/Medium/High), conventions of `Features/Quotes` (state map + history), `Features/ConnectionRequests` (attachments in DB, customer scope) and `Features/Customers` (paging).

## Decisions
- New module `Features/Tickets`, entity `ServiceTicket`; no changes to `Program.cs`, `AppDbContext`, CI. EF migration `AddServiceTickets` (README: migrations are part of a schema change).
- States from the mapping doc, not the 5 states in #121: `New, Triaged, RemoteDiagnosis, VisitScheduled, OnSite, Verified, Resolved, Closed`. Transition map follows BPMN #67: New→Triaged→RemoteDiagnosis→(Resolved | VisitScheduled→OnSite→Verified→Resolved)→Closed. "Pridelený" (assignment) belongs to #124 (#36).
- Origin `CustomerReport` (customer account), `Manual` (staff), `AnomalyDetected` (#115, later).
- Priority `Low, Medium, High` (ADR #194). Staff may set it at creation, a customer report starts as `Medium`; triage sets it.
- Every transition appends a `ServiceTicketStateChange` (from, to, user, time, note), including creation (null→New). No update/delete of history or comments.
- Comments: author, time, `IsPublic`; customers see only public ones (filtered in the query). Transition notes are hidden from customers.
- Attachments (photo of the fault): base64 in the create request, stored in DB, max 5 files × 5 MB, types image/jpeg, image/png, application/pdf; download endpoint with `nosniff`.
- Customer creating a ticket for a foreign installation gets the same 400 as for an unknown installation (pattern of ConnectionRequests).
- Extension point: transitions to `VisitScheduled`, `OnSite`, `Verified` exist on the entity but the generic transition endpoint refuses them (409); #36/#37 add visit endpoints.
- Policies: `TicketsRead` (all roles, customer scoped), `TicketsCreate` (Admin, Operator, Technician, Customer), `TicketsManage` (Admin, Operator), `TicketsComment` (Admin, Operator, Technician).

## Endpoints
`POST /api/tickets`, `GET /api/tickets` (state, priority, origin, installationId, page, pageSize, sortBy, sortDir), `GET /api/tickets/{id}`, `PUT /api/tickets/{id}/triage`, `POST /api/tickets/{id}/transition`, `GET|POST /api/tickets/{id}/comments`, `GET /api/tickets/{id}/history`, `GET /api/tickets/{id}/attachments/{attachmentId}`.

## TDD steps
1. Unit tests `Unit/Tickets` (state automaton, history, comments, request validation) - red.
2. Integration tests `Integration/Tickets` (create, scope, transitions, filters/paging, comments/history, 401/403/404) - red.
3. Entity, configuration, contracts, service, module, `.http`, policies + `docs/security/authorization.md`, migration.
4. `dotnet build` (0 warnings), `dotnet test`.
