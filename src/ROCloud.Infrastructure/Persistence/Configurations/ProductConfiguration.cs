using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products");

        b.Property(p => p.Name).HasMaxLength(200);
        b.Property(p => p.Unit).HasMaxLength(20);
        b.Property(p => p.Hsn).HasMaxLength(8);
        b.Property(p => p.DefaultRate).HasPrecision(10, 2);
        b.Property(p => p.BottleSize)
            .HasConversion(EnumConverters.BottleSizeConverter)
            .HasMaxLength(20);
    }
}
