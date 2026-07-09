using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.ToTable("invoices");

        b.Property(i => i.InvoiceNumber).HasMaxLength(50);
        b.Property(i => i.GstNumber).HasMaxLength(20);
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.SubTotal).HasPrecision(10, 2);
        b.Property(i => i.TaxAmount).HasPrecision(10, 2);
        b.Property(i => i.Discount).HasPrecision(10, 2);
        b.Property(i => i.TotalAmount).HasPrecision(10, 2);
        b.Property(i => i.PaidAmount).HasPrecision(10, 2);

        b.HasOne(i => i.Customer)
            .WithMany(c => c.Invoices)
            .HasForeignKey(i => i.CustomerId);
    }
}
