using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("audit_logs");

        // Range-partitioned by created_at → composite primary key (id, created_at)
        b.HasKey(a => new { a.Id, a.CreatedAt });

        // audit_logs has no updated_at / is_deleted columns
        b.Ignore(a => a.UpdatedAt);
        b.Ignore(a => a.IsDeleted);

        b.Property(a => a.Module).HasMaxLength(50);
        b.Property(a => a.Action).HasMaxLength(50);
        b.Property(a => a.EntityName).HasMaxLength(100);
        b.Property(a => a.IpAddress).HasMaxLength(45);
        b.Property(a => a.OldValues).HasColumnType("jsonb");
        b.Property(a => a.NewValues).HasColumnType("jsonb");
    }
}
