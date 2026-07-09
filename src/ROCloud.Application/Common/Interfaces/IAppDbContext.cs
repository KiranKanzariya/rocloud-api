using Microsoft.EntityFrameworkCore;
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
}
