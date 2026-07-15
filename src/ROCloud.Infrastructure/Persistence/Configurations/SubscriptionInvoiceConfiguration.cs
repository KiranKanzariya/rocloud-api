using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> b)
    {
        b.ToTable("subscription_invoices");
        b.Ignore(t => t.IsDeleted);   // no is_deleted column — we use the Void status instead

        b.Property(t => t.InvoiceNumber).HasMaxLength(30).IsRequired();
        b.Property(t => t.PlanType).HasMaxLength(20);
        b.Property(t => t.BillingCycle).HasMaxLength(10);
        b.Property(t => t.Status).HasMaxLength(20);
        b.Property(t => t.Description).HasMaxLength(200);
        b.Property(t => t.RazorpayOrderId).HasMaxLength(100);
        b.Property(t => t.RazorpayPaymentId).HasMaxLength(100);
        b.Property(t => t.GrossAmount).HasPrecision(10, 2);
        b.Property(t => t.DiscountAmount).HasPrecision(10, 2);
        b.Property(t => t.Amount).HasPrecision(10, 2);

        b.HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId);
        b.HasIndex(t => t.InvoiceNumber).IsUnique();
        b.HasIndex(t => t.TenantId);
        b.HasIndex(t => t.Status);
    }
}
