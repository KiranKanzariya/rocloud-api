using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROCloud.Domain.Entities.Platform;

namespace ROCloud.Infrastructure.Persistence.Configurations;

public class RazorpayOrderIndexConfiguration : IEntityTypeConfiguration<RazorpayOrderIndex>
{
    public void Configure(EntityTypeBuilder<RazorpayOrderIndex> b)
    {
        b.ToTable("razorpay_order_index");
        b.HasKey(x => x.RazorpayOrderId);
        b.Property(x => x.RazorpayOrderId).HasMaxLength(64);
    }
}
