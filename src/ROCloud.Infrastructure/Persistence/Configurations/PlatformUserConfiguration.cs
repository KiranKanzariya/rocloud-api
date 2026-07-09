using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> b)
    {
        b.ToTable("platform_users");
        b.Ignore(p => p.IsDeleted); // platform_users has no is_deleted column

        b.Property(p => p.Name).HasMaxLength(200);
        b.Property(p => p.Email).HasMaxLength(200);
        b.Property(p => p.PlatformRole).HasMaxLength(30);
    }
}
