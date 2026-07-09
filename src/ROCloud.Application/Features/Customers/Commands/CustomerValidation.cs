using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.Customers.Commands;

/// <summary>Shared value checks for customer enum-string inputs.</summary>
public static class CustomerValidation
{
    public static bool IsDeliveryMode(string? value) => value is not null && Enum.GetNames<DeliveryMode>().Contains(value);
    public static bool IsPaymentPreference(string? value) => value is not null && Enum.GetNames<PaymentPreference>().Contains(value);
    public static bool IsBottleSize(string? value) => BottleSizeExtensions.FromWire(value) is not null;
}
