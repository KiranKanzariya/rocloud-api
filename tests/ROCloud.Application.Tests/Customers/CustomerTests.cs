using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Common.Exceptions;
using ROCloud.Application.Features.Customers.Commands.CreateCustomer;
using ROCloud.Application.Features.Customers.Commands.DeleteCustomer;
using ROCloud.Application.Features.Customers.Commands.UpdateCustomer;
using ROCloud.Application.Features.Customers.Dtos;
using ROCloud.Application.Features.Customers.Queries.GetCustomers;
using ROCloud.Domain.Entities.Platform;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Domain.Enums;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;
using ValidationException = ROCloud.Application.Common.Exceptions.ValidationException;

namespace ROCloud.Application.Tests.Customers;

public class CustomerTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static (AppDbContext Db, TenantContext Ctx) NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"customers-{Guid.NewGuid()}").Options, ctx);
        return (db, ctx);
    }

    private static CreateCustomerCommand NewCreateCommand(string name = "Ravi Kumar", string mobile = "9876543210") =>
        new(null, name, mobile, null, null, null, null, null, null, "HomeDelivery", "PerBottle", "20L", null, null);

    [Fact]
    public async Task CreateCustomer_ValidInput_CreatesCustomer()
    {
        var (db, ctx) = NewDb();
        var handler = new CreateCustomerCommandHandler(db, ctx);

        var id = await handler.Handle(NewCreateCommand(), CancellationToken.None);

        var created = await db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        Assert.NotNull(created);
        Assert.Equal(TenantA, created!.TenantId);
        Assert.Equal("CUST-00001", created.CustomerCode);
        Assert.Equal(BottleSize.TwentyL, created.PreferredBottleSize);
    }

    [Fact]
    public async Task CreateCustomer_DuplicateMobile_ReturnsValidationError()
    {
        var (db, ctx) = NewDb();
        var handler = new CreateCustomerCommandHandler(db, ctx);
        await handler.Handle(NewCreateCommand(mobile: "9000000001"), CancellationToken.None);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(NewCreateCommand(name: "Other", mobile: "9000000001"), CancellationToken.None));
    }

    [Fact]
    public async Task GetCustomers_WithFilter_ReturnsFilteredResults()
    {
        var (db, _) = NewDb();
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Active One", Mobile = "1", IsActive = true });
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Active Two", Mobile = "2", IsActive = true });
        db.Customers.Add(new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Inactive", Mobile = "3", IsActive = false });
        await db.SaveChangesAsync();

        var handler = new GetCustomersQueryHandler(db);
        var result = await handler.Handle(
            new GetCustomersQuery(new CustomerFilterDto { IsActive = true }), CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, c => Assert.True(c.IsActive));
    }

    [Fact]
    public async Task DeleteCustomer_WithPendingOrders_ReturnsBadRequest()
    {
        var (db, _) = NewDb();
        var customer = new Customer { Id = Guid.NewGuid(), TenantId = TenantA, Name = "Has Orders", Mobile = "5" };
        db.Customers.Add(customer);
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid(),
            TenantId = TenantA,
            CustomerId = customer.Id,
            OrderDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Status = OrderStatus.Pending
        });
        await db.SaveChangesAsync();

        var handler = new DeleteCustomerCommandHandler(db);

        await Assert.ThrowsAsync<ValidationException>(() =>
            handler.Handle(new DeleteCustomerCommand(customer.Id), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateCustomer_OtherTenantCustomer_Returns404()
    {
        var (db, _) = NewDb();
        // Customer belongs to a DIFFERENT tenant; the context is scoped to TenantA.
        var otherTenantCustomer = new Customer { Id = Guid.NewGuid(), TenantId = TenantB, Name = "Theirs", Mobile = "7" };
        db.Customers.Add(otherTenantCustomer);
        await db.SaveChangesAsync();

        var handler = new UpdateCustomerCommandHandler(db);
        var command = new UpdateCustomerCommand(
            otherTenantCustomer.Id, null, "Hacked", "7", null, null, null, null, null, null,
            "HomeDelivery", "PerBottle", null, null, null, true);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(command, CancellationToken.None));
    }

    /// <summary>#5: MaxCustomers is now enforced on create.</summary>
    [Fact]
    public async Task CreateCustomer_ExceedsMaxCustomers_ThrowsPlanLimit()
    {
        var (db, ctx) = NewDb();
        var plan = new Plan { Id = Guid.NewGuid(), Name = "Basic", PlanType = PlanType.Basic, MaxCustomers = 2 };
        db.Plans.Add(plan);
        db.Tenants.Add(new Tenant
        {
            Id = TenantA, PlanId = plan.Id, Name = "Co", Subdomain = "co",
            OwnerName = "O", OwnerEmail = "o@x.com", OwnerMobile = "9"
        });
        await db.SaveChangesAsync();

        var handler = new CreateCustomerCommandHandler(db, ctx);
        await handler.Handle(NewCreateCommand(name: "C1", mobile: "9000000001"), CancellationToken.None);
        await handler.Handle(NewCreateCommand(name: "C2", mobile: "9000000002"), CancellationToken.None);

        await Assert.ThrowsAsync<PlanLimitException>(() =>
            handler.Handle(NewCreateCommand(name: "C3", mobile: "9000000003"), CancellationToken.None));
    }
}
