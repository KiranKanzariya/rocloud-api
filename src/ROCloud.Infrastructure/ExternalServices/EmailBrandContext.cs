using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.ExternalServices;

/// <summary>Scoped, mutable holder for the current email brand. Starts at the ROCloud default.</summary>
public sealed class EmailBrandContext : IEmailBrandContext
{
    public EmailBrand Current { get; set; } = EmailBrand.RoCloud;
}
