using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class ServiceRequestConfiguration : IEntityTypeConfiguration<ServiceRequest>
{
    public void Configure(EntityTypeBuilder<ServiceRequest> b)
    {
        b.ToTable("service_requests");

        b.Property(s => s.TicketNumber).HasMaxLength(20);
        b.Property(s => s.Title).HasMaxLength(200);
        b.Property(s => s.ServiceType).HasConversion<string>().HasMaxLength(30);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.Priority).HasConversion<string>().HasMaxLength(10);

        b.HasOne(s => s.Customer)
            .WithMany(c => c.ServiceRequests)
            .HasForeignKey(s => s.CustomerId);
    }
}
