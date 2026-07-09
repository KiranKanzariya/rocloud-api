# Deliveries feature

Follows the [Customers reference layout](../Customers/README.md). Notes specific to Deliveries:

- **Shared projection** ([DeliveryProjection](Queries/DeliveryProjection.cs)) and shared
  filter (`GetDeliveriesQueryHandler.ApplyFilter`) back the list, board, summary and route
  queries so they stay consistent.
- **Board** (`GET /board`) groups into `{ pending, inTransit, delivered }`; the delivered
  column also includes Failed/Skipped terminal stops.
- **My-route** (`GET /my-route`, `Deliveries.ViewOwn`) forces
  `DeliveryBoyId == currentUser.UserId` regardless of any filter value — a delivery boy
  can only ever see their own stops.
- **UpdateDeliveryStatus** is the mobile workflow. On `Delivered` it records the proof
  fields, syncs the order to `Delivered`, and **auto-creates a `Payment`** when
  `collectedAmount > 0` (a payment method is then required). Inventory updates are a
  logged Phase-9 TODO.
- **Proof upload** (`POST /{id}/proof`, multipart) → [DeliveryProofService](Services/DeliveryProofService.cs):
  size ≤ 5 MB, extension + MIME whitelist, **magic-byte check**, **ImageSharp re-encode to
  JPEG** (strips embedded payloads), random GUID filename, stored via `IFileStorage` under
  `{tenantId}/delivery-proofs/`. The service is AspNetCore-free (takes raw bytes); the
  controller reads the `IFormFile`. Never touches the filesystem directly.
