using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> b)
    {
        b.ToTable("areas");
        b.Ignore(a => a.UpdatedAt); // areas has no updated_at column

        b.Property(a => a.Name).HasMaxLength(100);
        b.Property(a => a.City).HasMaxLength(100);
        b.Property(a => a.Pincode).HasMaxLength(10);
    }
}
