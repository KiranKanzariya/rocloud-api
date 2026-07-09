# Customers — the reference feature module

Every tenant feature module (Orders, Inventory, Invoices, Payments, …) follows this
exact CQRS layout. Copy this structure.

```
Features/<Feature>/
├── Dtos/                      # records: list item, detail, filter, stats, nested
├── Commands/
│   ├── <Action>/<Action>Command.cs   # record(IRequest<TResult>) + Validator + Handler
│   └── <SharedValidation>.cs          # shared enum-string checks (optional)
└── Queries/
    └── <Query>/<Query>.cs             # record(IRequest<TResult>) + Handler
```

## Conventions

- **One file per command/query** holding the `record` (`IRequest<T>`), its
  `AbstractValidator<T>` (FluentValidation, auto-registered), and the
  `IRequestHandler<T>`. Handlers stay thin.
- **Handlers depend on `IAppDbContext`** (never the concrete context) and
  `ITenantContext` when they need the current tenant id (e.g. for inserts or codes).
- **Tenant isolation is automatic** — the global query filter scopes every read to the
  current tenant. Loading by id and getting `null` is how cross-tenant access becomes a
  404 (`throw new NotFoundException(...)`). PostgreSQL RLS is the defence-in-depth net.
- **Mapping is manual** (no AutoMapper — it has a known vulnerability):
  - List/grid queries **project straight to the DTO in SQL** (`.Select(...)`). For
    columns that need post-processing (enum → wire string), project to an anonymous
    type first, then map to the DTO in memory.
  - Detail loads use `Include(...)` + explicit construction of the DTO.
- **Enum inputs are strings** on commands (validated by `CustomerValidation`), parsed to
  the enum in the handler. `BottleSize` uses `BottleSizeExtensions.ToWire/FromWire`
  (e.g. `TwentyL ⇄ "20L"`).
- **Validation** runs in the MediatR `ValidationBehaviour` and throws the Application
  `ValidationException` → 400 with a field→messages map.
- **Errors** propagate as Application exceptions; `ExceptionMiddleware` maps them
  (`NotFoundException`→404, `ValidationException`→400, `ForbiddenAccessException`→403, …).
- **Soft delete** sets `IsDeleted = true`; guard against deleting entities with open
  dependents (e.g. customers with open orders).

## Controller

`Controllers/Tenant/<Feature>Controller.cs` — `[ApiController][Authorize]`, one
`[RequirePermission("<Module>.<Action>")]` per endpoint, responses wrapped in
`ApiResponse<T>`. Plan-gated endpoints add `[RequirePlan("Pro" | "Enterprise")]`.
