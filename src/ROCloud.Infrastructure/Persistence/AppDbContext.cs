using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Domain.Entities.Common;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence;

/// <summary>
/// The EF Core database context. Applies entity configurations, a snake_case
/// column naming convention, automatic tenant isolation query filters, and
/// audit-timestamp maintenance (guide §4).
/// </summary>
public class AppDbContext : DbContext, IAppDbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // Platform tables
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<PlatformBillingTransaction> PlatformBillingTransactions => Set<PlatformBillingTransaction>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<AuditSettings> AuditSettings => Set<AuditSettings>();

    // Tenant tables
    public DbSet<User> Users => Set<User>();
    public DbSet<UserArea> UserAreas => Set<UserArea>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerSubscription> CustomerSubscriptions => Set<CustomerSubscription>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Delivery> Deliveries => Set<Delivery>();
    public DbSet<DeliveryItem> DeliveryItems => Set<DeliveryItem>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<RazorpayOrderIndex> RazorpayOrderIndexes => Set<RazorpayOrderIndex>();
    public DbSet<ServiceRequest> ServiceRequests => Set<ServiceRequest>();
    public DbSet<AmcSubscription> AmcSubscriptions => Set<AmcSubscription>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ReminderLog> ReminderLogs => Set<ReminderLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // snake_case naming convention (chosen in Phase 3): map every property to
        // its snake_case column. Table names are set explicitly per configuration.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
                property.SetColumnName(ToSnakeCase(property.Name));
        }

        // Auto-apply tenant isolation to every ITenantEntity. Entities whose tables
        // carry an is_deleted column also get the soft-delete clause; the rest get a
        // tenant-only filter (their IsDeleted property is ignored in configuration).
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (!typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                continue;

            var hasSoftDelete = entityType.FindProperty(nameof(BaseEntity.IsDeleted)) != null;
            var methodName = hasSoftDelete
                ? nameof(SetTenantAndSoftDeleteFilter)
                : nameof(SetTenantFilter);

            typeof(AppDbContext)
                .GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [builder]);
        }
    }

    private void SetTenantAndSoftDeleteFilter<T>(ModelBuilder builder)
        where T : BaseEntity, ITenantEntity
        => builder.Entity<T>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId && !e.IsDeleted);

    private void SetTenantFilter<T>(ModelBuilder builder)
        where T : class, ITenantEntity
        => builder.Entity<T>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.CreatedAt == default:
                    entry.Entity.CreatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>True for a real relational provider; false for the in-memory test store.</summary>
    public bool IsRelational => Database.IsRelational();

    /// <summary>Begins a database transaction (relational providers only — see <see cref="IsRelational"/>).</summary>
    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(
        CancellationToken cancellationToken = default)
        => Database.BeginTransactionAsync(cancellationToken);

    /// <summary>Detaches all tracked entities (see <see cref="IAppDbContext.ClearChangeTracker"/>).</summary>
    public void ClearChangeTracker() => ChangeTracker.Clear();

    /// <summary>Converts a PascalCase CLR name to snake_case (e.g. TenantId → tenant_id).</summary>
    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 &&
                    (char.IsLower(name[i - 1]) ||
                     (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                {
                    sb.Append('_');
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
