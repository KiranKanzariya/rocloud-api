# Service Requests / AMC feature

Follows the [Customers reference layout](../Customers/README.md). AMC = Annual Maintenance
Contract; a service request is one maintenance/complaint ticket.

- **Ticket numbers** are `SR-NNNN`, sequential per tenant (counted incl. soft-deleted via
  `IgnoreQueryFilters` so numbers are never reused) — same approach as customer/invoice codes.
- **Technician assignment is role-validated**: `AssignTechnician` only accepts a user that is
  active and holds the `Technician` role in the tenant, else a `ValidationException`. Assigning
  does not change status — the technician moves the ticket to `InProgress` themselves.
- **Status flow**: `Open → InProgress → Resolved` (or `Cancelled`). On `Resolved` the handler
  stamps `ResolvedAt` and stores `ResolutionNotes`.
- **My jobs** (`GET /mine`, `AMC.Update`) restricts to `AssignedTechId == currentUser.UserId` —
  a technician only ever sees their own tickets.
- **ScheduleAmcVisits** is driven by the `amc_subscriptions` table (see
  [AmcSubscriptions](../AmcSubscriptions/)): for each active subscription whose `NextDueDate`
  is on or before `AsOfDate + LeadDays` (default today + 7) and not past its `EndDate`, it
  creates a `RoutineAMC` ticket (`ScheduledDate = NextDueDate`), skips customers that already
  have an open RoutineAMC, and **advances the subscription's `NextDueDate` by its interval**
  so it won't re-fire until the next cycle.
- Shared list projection: [ServiceRequestProjection](Queries/ServiceRequestProjection.cs).
- The Phase-14 Hangfire job will scan for visits due in the next 7 days and send WhatsApp
  reminders.
