using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        b.ToTable("notifications");
        b.Ignore(n => n.IsDeleted); // notifications has no is_deleted column

        b.Property(n => n.Type).HasMaxLength(40).IsRequired();
        b.Property(n => n.Title).HasMaxLength(200).IsRequired();
        b.Property(n => n.Link).HasMaxLength(200);
        b.Property(n => n.ReferenceKey).HasMaxLength(120).IsRequired();

        // One row per (tenant, user, type): generation updates it in place.
        b.HasIndex(n => new { n.TenantId, n.UserId, n.Type }).IsUnique();
        b.HasIndex(n => new { n.TenantId, n.UserId, n.IsRead });
    }
}
