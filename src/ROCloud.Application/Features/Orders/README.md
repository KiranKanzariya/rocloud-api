# Orders feature

Follows the [Customers reference layout](../Customers/README.md). Notes specific to Orders:

- **One order → one delivery (1:1), many order items (1:M).** `CreateOrder` and the
  bulk run create the `Order`, its `OrderItem`s, and a `Delivery{Status=Pending}` together
  and call `SaveChangesAsync` **once** (single DB transaction, InMemory-safe).
- **`OrderItem.TotalAmount` is a stored generated column** (`quantity * unit_rate`). Never
  set it. Queries that need a total compute `quantity * unit_rate` in SQL so the value is
  correct on both Postgres and the InMemory test provider.
- **Delivery-boy auto-assignment** ([DeliveryBoyResolver](Commands/DeliveryBoyResolver.cs)):
  there is no user↔area table in v1, so it prefers the boy most recently assigned to an
  order in the customer's area, else the first active `DeliveryBoy`-role user, else null.
- **Bulk-from-subscriptions** applies frequency rules (Daily / AlternateDay / Weekly /
  Monthly; Custom is skipped) against a target date and skips customers that already have
  an order that day.
- **Inventory decrement is a logged TODO** — wired up in Phase 9 via `InventoryService`.
