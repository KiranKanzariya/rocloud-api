using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> b)
    {
        b.ToTable("inventory"); // singular table name

        // inventory has no created_at / updated_at / is_deleted columns
        b.Ignore(i => i.CreatedAt);
        b.Ignore(i => i.UpdatedAt);
        b.Ignore(i => i.IsDeleted);

        b.HasIndex(i => new { i.TenantId, i.ProductId }).IsUnique();

        b.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId);
    }
}
