using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class SupportTicketConfiguration : IEntityTypeConfiguration<SupportTicket>
{
    public void Configure(EntityTypeBuilder<SupportTicket> b)
    {
        b.ToTable("support_tickets");
        b.Ignore(t => t.IsDeleted); // no is_deleted column

        b.Property(t => t.Subject).HasMaxLength(200);
        b.Property(t => t.Status).HasMaxLength(20);
        b.Property(t => t.Priority).HasMaxLength(20);

        b.HasOne(t => t.Tenant).WithMany().HasForeignKey(t => t.TenantId);
        b.HasOne(t => t.AssignedPlatformUser).WithMany().HasForeignKey(t => t.AssignedPlatformUserId);
        b.HasIndex(t => t.Status);
        b.HasIndex(t => t.TenantId);
    }
}
