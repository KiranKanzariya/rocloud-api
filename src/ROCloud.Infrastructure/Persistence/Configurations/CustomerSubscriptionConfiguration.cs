using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Tenant;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class CustomerSubscriptionConfiguration : IEntityTypeConfiguration<CustomerSubscription>
{
    public void Configure(EntityTypeBuilder<CustomerSubscription> b)
    {
        b.ToTable("customer_subscriptions");

        b.Property(s => s.Frequency).HasConversion<string>().HasMaxLength(20);
        b.Property(s => s.RatePerUnit).HasPrecision(10, 2);

        b.HasOne(s => s.Customer)
            .WithMany(c => c.Subscriptions)
            .HasForeignKey(s => s.CustomerId);

        b.HasOne(s => s.Product)
            .WithMany(p => p.Subscriptions)
            .HasForeignKey(s => s.ProductId);
    }
}
