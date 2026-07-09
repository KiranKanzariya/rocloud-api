namespace ROCloud.Application.Features.Reports.Dtos;

/// <summary>Daily payment collection totals.</summary>
public sealed record DailyCollectionDto(
    DateOnly Date,
    decimal TotalCollected,
    decimal Cash,
    decimal Digital,
    int TransactionCount,
    string? TopPaymentMethods);

/// <summary>One month of collected revenue (for the revenue chart).</summary>
public sealed record MonthlyCollectionDto(
    int Year,
    int Month,
    decimal TotalCollected,
    int TransactionCount);

/// <summary>Per-delivery-boy delivery efficiency for a day.</summary>
public sealed record DeliveryEfficiencyDto(
    Guid? DeliveryBoyId,
    string DeliveryBoyName,
    int Assigned,
    int Delivered,
    int Pending,
    int Failed,
    decimal CollectedAmount,
    double EfficiencyPct,
    double? AvgDeliveryMinutes);

/// <summary>Outstanding dues per customer, bucketed by age.</summary>
public sealed record OutstandingDuesReportDto(
    Guid CustomerId,
    string CustomerName,
    string? Mobile,
    decimal TotalOutstanding,
    decimal Bucket0To7,
    decimal Bucket7To30,
    decimal Bucket30To60,
    decimal Bucket60Plus);

/// <summary>Per-area performance over a period.</summary>
public sealed record AreaPerformanceDto(
    Guid? AreaId,
    string AreaName,
    int CustomerCount,
    int OrderCount,
    decimal Revenue,
    decimal AverageOrderValue);

/// <summary>A high-value customer over a period.</summary>
public sealed record TopCustomerDto(
    Guid CustomerId,
    string CustomerName,
    string? Mobile,
    decimal Revenue,
    int OrderCount,
    decimal Outstanding);

/// <summary>Bottle (jar) float tracking per bottle size.</summary>
public sealed record BottleTrackingReportDto(
    string BottleSize,
    int TotalStock,
    int Issued,
    int Returned,
    int Damaged,
    int Missing,
    double DamageRatePct);

/// <summary>New vs churned customers for a month.</summary>
public sealed record CustomerAcquisitionDto(
    int Year,
    int Month,
    int NewCustomers,
    int Churned);
