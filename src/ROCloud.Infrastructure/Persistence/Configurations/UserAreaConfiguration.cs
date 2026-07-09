using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class UserAreaConfiguration : IEntityTypeConfiguration<UserArea>
{
    public void Configure(EntityTypeBuilder<UserArea> b)
    {
        b.ToTable("user_areas");

        // user_areas has no updated_at / is_deleted columns (junction; hard-replaced).
        b.Ignore(ua => ua.UpdatedAt);
        b.Ignore(ua => ua.IsDeleted);

        b.HasIndex(ua => new { ua.UserId, ua.AreaId }).IsUnique();
        b.HasIndex(ua => new { ua.TenantId, ua.AreaId });

        b.HasOne<User>()
            .WithMany(u => u.AreaAssignments)
            .HasForeignKey(ua => ua.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(ua => ua.Area)
            .WithMany()
            .HasForeignKey(ua => ua.AreaId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
