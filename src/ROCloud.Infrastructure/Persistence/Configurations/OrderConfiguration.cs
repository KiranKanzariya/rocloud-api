using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders");

        b.Property(o => o.OrderType).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.DeliveryMode).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);

        b.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId);

        b.HasOne(o => o.Area)
            .WithMany(a => a.Orders)
            .HasForeignKey(o => o.AreaId);
    }
}
