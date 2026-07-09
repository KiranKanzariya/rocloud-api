using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("notification_templates");
        b.Ignore(t => t.IsDeleted); // notification_templates has no is_deleted column

        b.Property(t => t.TemplateCode).HasMaxLength(50);
        b.Property(t => t.LanguageCode).HasMaxLength(5);
        b.Property(t => t.Channel).HasMaxLength(20);
        b.Property(t => t.Subject).HasMaxLength(200);
        b.Property(t => t.Body).IsRequired();

        b.HasIndex(t => new { t.TenantId, t.TemplateCode, t.LanguageCode, t.Channel }).IsUnique();
    }
}
