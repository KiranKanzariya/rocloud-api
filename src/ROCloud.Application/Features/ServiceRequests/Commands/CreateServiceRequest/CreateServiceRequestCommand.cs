using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Common.Sanitisation;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;

namespace ROCloud.Application.Features.ServiceRequests.Commands.CreateServiceRequest;

/// <summary>
/// Opens a new service request / AMC job. Created by Customer Care or the owner. Generates a
/// per-tenant ticket number SR-NNNN and may include an optional scheduled date.
/// </summary>
public sealed record CreateServiceRequestCommand(
    Guid CustomerId,
    string Title,
    [property: SanitizeHtml] string? Description,
    string ServiceType,
    string? Priority,
    DateOnly? ScheduledDate) : IRequest<Guid>;

public class CreateServiceRequestCommandValidator : AbstractValidator<CreateServiceRequestCommand>
{
    public CreateServiceRequestCommandValidator()
    {
        RuleFor(c => c.CustomerId).NotEmpty();
        RuleFor(c => c.Title).NotEmpty().Length(2, 200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.ServiceType)
            .Must(v => Enum.GetNames<ServiceType>().Contains(v))
            .WithMessage("Invalid service type.");
        RuleFor(c => c.Priority)
            .Must(v => v is null || Enum.GetNames<ServicePriority>().Contains(v))
            .WithMessage("Invalid priority.");
    }
}

public class CreateServiceRequestCommandHandler : IRequestHandler<CreateServiceRequestCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;

    public CreateServiceRequestCommandHandler(IAppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    public async Task<Guid> Handle(CreateServiceRequestCommand request, CancellationToken ct)
    {
        var customerExists = await _db.Customers.AnyAsync(c => c.Id == request.CustomerId, ct);
        if (!customerExists)
            throw new NotFoundException("Customer", request.CustomerId);

        var serviceRequest = new ServiceRequest
        {
            Id = Guid.NewGuid(),
            TenantId = _tenant.TenantId,
            CustomerId = request.CustomerId,
            TicketNumber = await NextTicketNumberAsync(ct),
            Title = request.Title,
            Description = request.Description,
            ServiceType = Enum.Parse<ServiceType>(request.ServiceType),
            Priority = request.Priority is { } p ? Enum.Parse<ServicePriority>(p) : ServicePriority.Medium,
            Status = ServiceRequestStatus.Open,
            ScheduledDate = request.ScheduledDate
        };

        _db.ServiceRequests.Add(serviceRequest);
        await _db.SaveChangesAsync(ct);
        return serviceRequest.Id;
    }

    /// <summary>SR-NNNN, sequential per tenant (incl. soft-deleted so numbers aren't reused).</summary>
    private async Task<string> NextTicketNumberAsync(CancellationToken ct)
    {
        var count = await _db.ServiceRequests.IgnoreQueryFilters()
            .CountAsync(s => s.TenantId == _tenant.TenantId, ct);
        return $"SR-{count + 1:D4}";
    }
}
