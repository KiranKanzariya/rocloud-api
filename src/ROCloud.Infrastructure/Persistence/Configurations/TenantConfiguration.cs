using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> b)
    {
        b.ToTable("tenants");

        // Subdomain is unique among live tenants (soft-deleted rows may free it). Mirrors
        // scripts/master.sql's idx_tenants_subdomain — declared here so an EF-provisioned database
        // gets the same guard, not just script-provisioned ones (the two had drifted apart).
        b.HasIndex(t => t.Subdomain).IsUnique().HasFilter("is_deleted = false");

        b.Property(t => t.Name).HasMaxLength(200);
        b.Property(t => t.Subdomain).HasMaxLength(100);
        b.Property(t => t.OwnerName).HasMaxLength(200);
        b.Property(t => t.OwnerEmail).HasMaxLength(200);
        b.Property(t => t.OwnerMobile).HasMaxLength(15);
        b.Property(t => t.PrimaryColor).HasMaxLength(7);
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.RazorpaySubscriptionId).HasMaxLength(100);
        b.Property(t => t.RazorpayCustomerId).HasMaxLength(100);
        b.Property(t => t.GstNumber).HasMaxLength(20);
        b.Property(t => t.GstRate).HasPrecision(5, 4);
        b.Property(t => t.City).HasMaxLength(100);
        b.Property(t => t.State).HasMaxLength(100);
        b.Property(t => t.Pincode).HasMaxLength(10);
        b.Property(t => t.UpiVpa).HasMaxLength(100);
        b.Property(t => t.UpiPayeeName).HasMaxLength(100);
        b.Property(t => t.UpiVerifiedName).HasMaxLength(200);
        b.Property(t => t.DefaultLanguage).HasMaxLength(5);
        b.Property(t => t.SubscriptionDiscountType).HasConversion<string>().HasMaxLength(20);
        b.Property(t => t.SubscriptionDiscountValue).HasPrecision(10, 2);

        // Pending downgrade. No navigation property: it is read as an id and resolved explicitly by the
        // renewal job, so a plan row can never be dragged into an unrelated tenant query.
        b.Property(t => t.ScheduledPlanId).HasColumnName("scheduled_plan_id");
    }
}
