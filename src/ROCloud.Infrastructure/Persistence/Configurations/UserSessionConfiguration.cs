using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> b)
    {
        b.ToTable("user_sessions");
        b.Ignore(s => s.UpdatedAt);  // rows are written once, then revoked — no updated_at column
        b.Ignore(s => s.IsDeleted);  // revoked_at is the lifecycle; there is no soft delete here

        b.Property(s => s.TokenHash).HasMaxLength(128).IsRequired();

        // The refresh path's only lookup, and unique because two live sessions sharing a token hash
        // would mean a hash collision or a bug — either way it must fail loudly, not silently
        // hand one device the other's session.
        b.HasIndex(s => s.TokenHash).IsUnique();

        // Revoking a device's chain, and listing what is signed in.
        b.HasIndex(s => new { s.UserId, s.SessionId });
    }
}
