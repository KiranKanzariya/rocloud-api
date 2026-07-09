using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class ReminderLogConfiguration : IEntityTypeConfiguration<ReminderLog>
{
    public void Configure(EntityTypeBuilder<ReminderLog> b)
    {
        b.ToTable("reminder_log");
        b.Ignore(r => r.UpdatedAt); // insert-only — no updated_at column
        b.Ignore(r => r.IsDeleted); // no is_deleted column

        b.Property(r => r.ReminderType).HasMaxLength(40).IsRequired();
        b.Property(r => r.Channel).HasMaxLength(20).IsRequired();

        // The lookup the jobs run every pass: recent reminders for a tenant + type (+ subject).
        b.HasIndex(r => new { r.TenantId, r.ReminderType, r.SubjectId, r.CreatedAt });
    }
}
