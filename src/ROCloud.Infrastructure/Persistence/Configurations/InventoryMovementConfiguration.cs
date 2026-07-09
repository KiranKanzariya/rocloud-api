using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.ToTable("inventory_movements");

        // inventory_movements has no updated_at / is_deleted columns
        b.Ignore(m => m.UpdatedAt);
        b.Ignore(m => m.IsDeleted);

        b.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(20);

        b.HasOne(m => m.Product)
            .WithMany()
            .HasForeignKey(m => m.ProductId);
    }
}
