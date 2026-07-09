namespace ROCloud.Domain.Enums;

/// <summary>
/// Service request workflow state. DB: service_requests.status.
/// Added under the "fully typed" enum strategy (every status/type column
/// gets an enum) — sibling to the four explicitly approved additions.
/// </summary>
public enum ServiceRequestStatus
{
    Open,
    InProgress,
    Resolved,
    Cancelled
}
