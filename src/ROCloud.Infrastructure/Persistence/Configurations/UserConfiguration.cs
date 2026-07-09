using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");

        b.Property(u => u.Name).HasMaxLength(200);
        b.Property(u => u.Mobile).HasMaxLength(15);
        b.Property(u => u.Email).HasMaxLength(200);
        b.Property(u => u.GoogleId).HasMaxLength(200);
        b.Property(u => u.GoogleEmail).HasMaxLength(200);
        b.Property(u => u.PreferredLanguage).HasMaxLength(5);
        b.Property(u => u.AuthProvider)
            .HasConversion(EnumConverters.AuthProviderConverter)
            .HasMaxLength(20);

        b.HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId);
    }
}
