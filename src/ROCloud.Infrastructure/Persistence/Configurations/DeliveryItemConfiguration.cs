using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class DeliveryItemConfiguration : IEntityTypeConfiguration<DeliveryItem>
{
    public void Configure(EntityTypeBuilder<DeliveryItem> b)
    {
        b.ToTable("delivery_items");
        // delivery_items has created_at but no updated_at / is_deleted columns.
        b.Ignore(d => d.UpdatedAt);
        b.Ignore(d => d.IsDeleted);

        b.HasOne(d => d.Delivery)
            .WithMany(d => d.Items)
            .HasForeignKey(d => d.DeliveryId);

        b.HasIndex(d => d.DeliveryId);
    }
}
