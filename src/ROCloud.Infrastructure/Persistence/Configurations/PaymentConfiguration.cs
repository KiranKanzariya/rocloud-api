using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("payments");

        // payments has no updated_at / is_deleted columns
        b.Ignore(p => p.UpdatedAt);
        b.Ignore(p => p.IsDeleted);

        b.Property(p => p.Amount).HasPrecision(10, 2);
        b.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.PaymentPreference).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.ReferenceNumber).HasMaxLength(100);
        b.Property(p => p.RazorpayPaymentId).HasMaxLength(100);

        b.HasOne(p => p.Customer)
            .WithMany(c => c.Payments)
            .HasForeignKey(p => p.CustomerId);

        b.HasOne(p => p.Invoice)
            .WithMany(i => i.Payments)
            .HasForeignKey(p => p.InvoiceId);
    }
}
