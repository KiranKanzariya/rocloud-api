# Inventory feature — bottle (jar) float tracking

Follows the [Customers reference layout](../Customers/README.md). The float is the heart of
an RO business: jars are an asset that circulates between the plant and customers.

## The counter model

`inventory` holds four counters per product; **availableStock is computed, never stored**:

```
availableStock = totalStock - issuedStock - damagedStock
```

Movement effects live in **one place** — [`InventoryMath.Apply`](../../Services/InventoryMath.cs)
— used by both `InventoryService` and the manual `AddInventoryMovement` command so they
never diverge:

| Movement   | Effect |
|------------|--------|
| Issue      | `issuedStock += qty` |
| Return     | `issuedStock -= qty`, `returnedStock += qty` |
| Damage     | `damagedStock += qty` |
| Restock    | `totalStock += qty` |
| Adjustment | `totalStock += qty` (signed — may be negative) |

## InventoryService (`Application/Services/`)

`RecordIssue/Return/Damage` get-or-create the product's `inventory` row (checking the EF
change-tracker `.Local` first so several calls in one unit of work share the row) and append
an `InventoryMovement`, but **do not call SaveChanges** — the caller owns the transaction.
`UpdateDeliveryStatus` calls it on `Delivered` (jars_delivered → Issue, jars_returned →
Return) and commits everything in its single SaveChanges.

- **Delivery → product:** a delivery carries one delivered/returned count but an order can
  span multiple products, so the float is applied to the **order's primary (first) item's
  product**.
- **Order creation does NOT move stock** — issuance happens at delivery.

## Reconcile

`ReconcileInventory` rebuilds the four counters from the movement ledger (the source of
truth — delivery-driven Issue/Return are already recorded there): `total = ΣRestock+ΣAdjust`,
`issued = ΣIssue−ΣReturn`, `returned = ΣReturn`, `damaged = ΣDamage`.

## Products

`Features/Products/` is full CRUD. Defaults (18L, 20L) are seeded at tenant provisioning
(Phase 5). No dedicated `Products.*` permission exists, so the controller reuses
`Inventory.View` / `Inventory.Manage`.
