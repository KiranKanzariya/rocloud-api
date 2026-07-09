using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class AmcSubscriptionConfiguration : IEntityTypeConfiguration<AmcSubscription>
{
    public void Configure(EntityTypeBuilder<AmcSubscription> b)
    {
        b.ToTable("amc_subscriptions", t =>
            t.HasCheckConstraint("ck_amc_subscriptions_interval", "interval_months IN (3, 6, 12)"));

        b.Property(s => s.PlanName).HasMaxLength(100);
        b.Property(s => s.Amount).HasPrecision(10, 2);

        b.HasIndex(s => new { s.TenantId, s.CustomerId });
        b.HasIndex(s => new { s.TenantId, s.NextDueDate });

        b.HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId);
    }
}
