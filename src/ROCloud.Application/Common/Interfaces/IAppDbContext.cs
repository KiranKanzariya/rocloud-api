using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Application.Common.Interfaces;

/// <summary>
/// Abstraction over the EF Core DbContext, exposed to the Application layer so
/// handlers depend on this rather than the concrete AppDbContext.
/// </summary>
public interface IAppDbContext
{
    // Platform
    DbSet<Plan> Plans { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<PlatformUser> PlatformUsers { get; }
    DbSet<PlatformBillingTransaction> PlatformBillingTransactions { get; }
    DbSet<SubscriptionInvoice> SubscriptionInvoices { get; }
    DbSet<SupportTicket> SupportTickets { get; }
    DbSet<AuditSettings> AuditSettings { get; }

    // Tenant-scoped
    DbSet<User> Users { get; }
    DbSet<UserArea> UserAreas { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Area> Areas { get; }
    DbSet<Product> Products { get; }
    DbSet<Customer> Customers { get; }
    DbSet<CustomerSubscription> CustomerSubscriptions { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Delivery> Deliveries { get; }
    DbSet<DeliveryItem> DeliveryItems { get; }
    DbSet<Inventory> Inventories { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<Payment> Payments { get; }
    DbSet<RazorpayOrderIndex> RazorpayOrderIndexes { get; }
    DbSet<ServiceRequest> ServiceRequests { get; }
    DbSet<AmcSubscription> AmcSubscriptions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<NotificationTemplate> NotificationTemplates { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ReminderLog> ReminderLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the provider is a real relational database (false for the in-memory test store). Gate
    /// transaction/savepoint use on this so non-relational providers keep their per-command behaviour.
    /// </summary>
    bool IsRelational { get; }

    /// <summary>
    /// Begins a database transaction so a multi-step write flow can commit once (and, where the provider
    /// supports them, use savepoints). Relational providers only — gate on <see cref="IsRelational"/>.
    /// </summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches all tracked entities. Call after rolling a transaction back to a savepoint so the change
    /// tracker no longer holds the rolled-back inserts/updates that would otherwise be re-applied on the
    /// next save.
    /// </summary>
    void ClearChangeTracker();
}
