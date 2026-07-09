using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class PlatformBillingTransactionConfiguration : IEntityTypeConfiguration<PlatformBillingTransaction>
{
    public void Configure(EntityTypeBuilder<PlatformBillingTransaction> b)
    {
        b.ToTable("platform_billing_transactions");
        b.Ignore(t => t.UpdatedAt);  // no updated_at column
        b.Ignore(t => t.IsDeleted);  // no is_deleted column

        b.Property(t => t.PlanType).HasMaxLength(20);
        b.Property(t => t.BillingCycle).HasMaxLength(10);
        b.Property(t => t.Status).HasMaxLength(20);
        b.Property(t => t.Amount).HasPrecision(10, 2);
        b.Property(t => t.RazorpayPaymentId).HasMaxLength(100);

        b.HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId);
        b.HasIndex(t => t.TenantId);
        b.HasIndex(t => t.Status);
    }
}
