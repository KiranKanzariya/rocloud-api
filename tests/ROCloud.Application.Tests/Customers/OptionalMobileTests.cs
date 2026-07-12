using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Customers.Commands.CreateCustomer;
using ROCloud.Application.Features.Customers.Commands.UpdateCustomer;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Customers;

/// <summary>
/// A mobile number is optional: an owner often has none for a shop or an old book entry. The format
/// is still enforced when one IS given, and uniqueness still applies to real numbers only.
/// </summary>
public class OptionalMobileTests
{
    private static readonly Guid TenantA = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"optional-mobile-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static CreateCustomerCommand Create(string? mobile) => new(
        null, "Sharma Tea Stall", mobile, null, null, null, null, null, null,
        nameof(DeliveryMode.HomeDelivery), nameof(PaymentPreference.PerBottle), null, null, null);

    private static UpdateCustomerCommand Update(Guid id, string? mobile) => new(
        id, null, "Sharma Tea Stall", mobile, null, null, null, null, null, null,
        nameof(DeliveryMode.HomeDelivery), nameof(PaymentPreference.PerBottle), null, null, null, true);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_WithoutAMobile_IsValid(string? mobile)
    {
        new CreateCustomerCommandValidator()
            .TestValidate(Create(mobile))
            .ShouldNotHaveValidationErrorFor(c => c.Mobile);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Update_WithoutAMobile_IsValid(string? mobile)
    {
        // An imported customer has no number; editing them must not force the owner to invent one.
        new UpdateCustomerCommandValidator()
            .TestValidate(Update(Guid.NewGuid(), mobile))
            .ShouldNotHaveValidationErrorFor(c => c.Mobile);
    }

    [Theory]
    [InlineData("98765")]
    [InlineData("9876543210")]       // missing the +91 the stored form requires
    [InlineData("+9198765432101")]
    public void AMalformedMobile_IsStillRejected(string mobile)
    {
        new CreateCustomerCommandValidator()
            .TestValidate(Create(mobile))
            .ShouldHaveValidationErrorFor(c => c.Mobile);
    }

    [Fact]
    public async Task Create_WithoutAMobile_StoresNull_NotAnEmptyString()
    {
        var (db, ctx) = NewDb();
        var handler = new CreateCustomerCommandHandler(db, ctx);

        var id = await handler.Handle(Create(""), CancellationToken.None);

        var customer = await db.Customers.FirstAsync(c => c.Id == id);
        Assert.Null(customer.Mobile);
    }

    [Fact]
    public async Task SeveralCustomers_MayHaveNoMobile_WithoutClashing()
    {
        // The "already exists" guard keys on the mobile, so a blank one must not collide with another blank.
        var (db, ctx) = NewDb();
        var handler = new CreateCustomerCommandHandler(db, ctx);

        await handler.Handle(Create(null), CancellationToken.None);
        var second = await handler.Handle(Create(null), CancellationToken.None);

        Assert.Equal(2, await db.Customers.CountAsync());
        Assert.Null((await db.Customers.FirstAsync(c => c.Id == second)).Mobile);
    }

    [Fact]
    public async Task ClearingAMobileOnUpdate_DoesNotTripTheUniquenessGuard()
    {
        var (db, ctx) = NewDb();
        var existing = Guid.NewGuid();
        var target = Guid.NewGuid();
        db.Customers.Add(new Customer { Id = existing, TenantId = TenantA, Name = "Has none", Mobile = null });
        db.Customers.Add(new Customer { Id = target, TenantId = TenantA, Name = "Ramesh", Mobile = "+919876543210" });
        await db.SaveChangesAsync();

        await new UpdateCustomerCommandHandler(db).Handle(Update(target, ""), CancellationToken.None);

        Assert.Null((await db.Customers.FirstAsync(c => c.Id == target)).Mobile);
    }

    [Fact]
    public async Task ARealMobileIsStillUnique()
    {
        var (db, ctx) = NewDb();
        var handler = new CreateCustomerCommandHandler(db, ctx);
        await handler.Handle(Create("+919876543210"), CancellationToken.None);

        await Assert.ThrowsAsync<Application.Common.Exceptions.ValidationException>(
            () => handler.Handle(Create("+919876543210"), CancellationToken.None));
    }
}
