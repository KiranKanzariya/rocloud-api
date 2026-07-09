using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("customers");

        b.Property(c => c.CustomerCode).HasMaxLength(20);
        b.Property(c => c.Name).HasMaxLength(200);
        b.Property(c => c.Mobile).HasMaxLength(15);
        b.Property(c => c.AlternateMobile).HasMaxLength(15);
        b.Property(c => c.Email).HasMaxLength(200);
        b.Property(c => c.Landmark).HasMaxLength(200);
        b.Property(c => c.PreferredLanguage).HasMaxLength(5);
        b.Property(c => c.Latitude).HasPrecision(10, 8);
        b.Property(c => c.Longitude).HasPrecision(11, 8);
        b.Property(c => c.DeliveryMode).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.PaymentPreference).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.PreferredBottleSize)
            .HasConversion(EnumConverters.BottleSizeConverter)
            .HasMaxLength(20);
        b.Property(c => c.DiscountType).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.DiscountValue).HasPrecision(10, 2);

        b.HasOne(c => c.Area)
            .WithMany(a => a.Customers)
            .HasForeignKey(c => c.AreaId);
    }
}
