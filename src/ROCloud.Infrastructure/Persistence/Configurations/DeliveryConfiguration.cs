using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> b)
    {
        b.ToTable("deliveries");
        b.Ignore(d => d.IsDeleted); // deliveries has no is_deleted column

        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(d => d.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(d => d.CollectedAmount).HasPrecision(10, 2);
        b.Property(d => d.Latitude).HasPrecision(10, 8);
        b.Property(d => d.Longitude).HasPrecision(11, 8);

        // One order has one delivery (1:1)
        b.HasOne(d => d.Order)
            .WithOne(o => o.Delivery)
            .HasForeignKey<Delivery>(d => d.OrderId);
    }
}
