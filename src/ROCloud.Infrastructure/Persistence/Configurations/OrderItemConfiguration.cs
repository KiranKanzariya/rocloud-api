using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items");

        // order_items has no created_at / updated_at / is_deleted columns
        b.Ignore(i => i.CreatedAt);
        b.Ignore(i => i.UpdatedAt);
        b.Ignore(i => i.IsDeleted);

        b.Property(i => i.UnitRate).HasPrecision(10, 2);
        b.Property(i => i.TotalAmount)
            .HasPrecision(10, 2)
            .HasComputedColumnSql("quantity * unit_rate", stored: true);

        b.HasOne(i => i.Order)
            .WithMany(o => o.OrderItems)
            .HasForeignKey(i => i.OrderId);

        b.HasOne(i => i.Product)
            .WithMany(p => p.OrderItems)
            .HasForeignKey(i => i.ProductId);
    }
}
