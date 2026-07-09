namespace ROCloud.Domain.Enums;

/// <summary>Kind of service request / AMC job. DB: service_requests.service_type.</summary>
public enum ServiceType
{
    FilterChange,
    MembraneReplace,
    Complaint,
    RoutineAMC,
    Installation,
    Other
}
