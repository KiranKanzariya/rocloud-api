using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Common.Interfaces;
using ROCloud.Application.Features.TenantSettings.Dtos;

namespace ROCloud.Application.Features.TenantSettings.Commands.VerifyUpiVpa;

/// <summary>
/// Checks a UPI id against the payments network before the owner puts a scan-to-pay QR on customer
/// invoices, and records the result on the tenant when it passes.
///
/// <para><b>What this can and cannot prove.</b> It confirms the id EXISTS and returns the account name
/// it is registered to. It cannot prove the id belongs to this owner — that is the point of showing
/// the name back: the owner reads "Kiran Kanzariya" and recognises their own account. A typo that
/// happens to land on somebody else's real UPI id is caught by the owner reading a stranger's name,
/// not by this call.</para>
///
/// <para>Verifying does not enable anything on its own. The owner still ticks the QR on and saves;
/// this only removes the guesswork about whether the id they typed is real.</para>
/// </summary>
public sealed record VerifyUpiVpaCommand(string Vpa) : IRequest<UpiVerificationDto>;

public class VerifyUpiVpaCommandValidator : AbstractValidator<VerifyUpiVpaCommand>
{
    public VerifyUpiVpaCommandValidator()
    {
        // Same shape rule as saving one — no point spending a network call on "not a UPI id at all".
        RuleFor(c => c.Vpa)
            .NotEmpty()
            .Matches(@"^[a-zA-Z0-9.\-_]{2,64}@[a-zA-Z][a-zA-Z0-9.\-]{1,63}$")
            .WithMessage("Enter a valid UPI ID, for example name@okaxis.");
    }
}

public class VerifyUpiVpaCommandHandler : IRequestHandler<VerifyUpiVpaCommand, UpiVerificationDto>
{
    private readonly IAppDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IRazorpayService _razorpay;

    public VerifyUpiVpaCommandHandler(IAppDbContext db, ITenantContext tenant, IRazorpayService razorpay)
    {
        _db = db;
        _tenant = tenant;
        _razorpay = razorpay;
    }

    public async Task<UpiVerificationDto> Handle(VerifyUpiVpaCommand request, CancellationToken ct)
    {
        var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.Id == _tenant.TenantId, ct)
                     ?? throw new NotFoundException("Tenant", _tenant.TenantId);

        var vpa = request.Vpa.Trim();
        var result = await _razorpay.ValidateVpaAsync(vpa, ct);

        // Could not run the check (no credentials, network, or the endpoint is not enabled on the
        // account). Say so plainly and change NOTHING: recording a failure here would look identical
        // to a genuinely bad id and could talk an owner out of a working setup.
        if (result.Unavailable)
            return new UpiVerificationDto(vpa, Verified: false, PayeeName: null, Unavailable: true);

        if (!result.Valid)
            return new UpiVerificationDto(vpa, Verified: false, PayeeName: null, Unavailable: false);

        // Verifying SAVES the id along with its result.
        //
        // It has to, because the QR cannot be switched on until the saved id is verified: if this only
        // stamped an id that was already stored, a new owner would type an id, verify it, tick the QR
        // on, and be refused — the id they verified was never the saved one. Persisting here is what
        // makes "type → verify → enable" work in a single pass.
        //
        // Nothing is switched on as a side effect: the owner still ticks the QR on and saves. This
        // only records which id was checked, and when.
        tenant.UpiVpa = vpa;
        tenant.UpiVerifiedAt = DateTime.UtcNow;
        tenant.UpiVerifiedName = result.PayeeName;
        await _db.SaveChangesAsync(ct);

        return new UpiVerificationDto(vpa, Verified: true, result.PayeeName, Unavailable: false);
    }
}
