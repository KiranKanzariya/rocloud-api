using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> b)
    {
        b.ToTable("plans");
        b.Ignore(p => p.IsDeleted); // plans has no is_deleted column

        b.Property(p => p.Name).HasMaxLength(50);
        b.Property(p => p.PlanType).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.MonthlyPrice).HasPrecision(10, 2);
        b.Property(p => p.YearlyPrice).HasPrecision(10, 2);
    }
}
