using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class AuditSettingsConfiguration : IEntityTypeConfiguration<AuditSettings>
{
    public void Configure(EntityTypeBuilder<AuditSettings> b)
    {
        b.ToTable("audit_settings");
        b.Ignore(s => s.IsDeleted); // audit_settings has no is_deleted column

        // string[] ↔ text[] is handled natively by the Npgsql provider; columns are NOT NULL with
        // array defaults set in the migration.
        b.Property(s => s.Methods).IsRequired();
        b.Property(s => s.SensitivePathPrefixes).IsRequired();
        b.Property(s => s.ExcludeModules).IsRequired();
        b.Property(s => s.AuditReadsForModules).IsRequired();
        b.Property(s => s.AdditionalRedactKeys).IsRequired();
    }
}
