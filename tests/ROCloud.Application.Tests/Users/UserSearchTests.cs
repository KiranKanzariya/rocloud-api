using Microsoft.EntityFrameworkCore;
using ROCloud.Application.Features.Users.Dtos;
using ROCloud.Application.Features.Users.Queries.GetUsers;
using ROCloud.Domain.Entities.Tenant;
using ROCloud.Infrastructure.MultiTenancy;
using ROCloud.Infrastructure.Persistence;

namespace ROCloud.Application.Tests.Users;

/// <summary>
/// The team-members page is server-paged, searched and sorted. It used to fetch one page of 100 and
/// then sort/paginate in the browser, so a team of more than 100 lost everyone past #100.
/// </summary>
public class UserSearchTests
{
    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TechnicianRole = Guid.NewGuid();
    private static readonly Guid ManagerRole = Guid.NewGuid();

    private static AppDbContext NewDb()
    {
        var ctx = new TenantContext { TenantId = TenantA };
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"user-search-{Guid.NewGuid()}").Options, ctx);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Roles.Add(new Role { Id = TechnicianRole, TenantId = TenantA, Name = "Technician" });
        db.Roles.Add(new Role { Id = ManagerRole, TenantId = TenantA, Name = "Manager" });
        await db.SaveChangesAsync();
    }

    private static User Row(string name, Guid roleId, bool active = true, string? email = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = TenantA, Name = name, RoleId = roleId,
        Email = email, IsActive = active
    };

    private static Task<Application.Common.Models.PagedResult<UserListItemDto>> Run(
        AppDbContext db, UserFilterDto filter) =>
        new GetUsersQueryHandler(db).Handle(new GetUsersQuery(filter), CancellationToken.None);

    [Fact]
    public async Task PagingIsServerSide_AndTotalCountIsTheRealTotal()
    {
        var db = NewDb();
        await SeedAsync(db);
        for (var i = 0; i < 130; i++) db.Users.Add(Row($"User {i:D3}", ManagerRole));
        await db.SaveChangesAsync();

        var page2 = await Run(db, new UserFilterDto { Page = 2, PageSize = 25 });

        Assert.Equal(130, page2.TotalCount);   // the real total, not the size of a fetched page
        Assert.Equal(25, page2.Items.Count);
        Assert.Equal("User 025", page2.Items[0].Name);   // sorted by name, second page
    }

    [Fact]
    public async Task SearchIsCaseInsensitive_AndTrimmed()
    {
        var db = NewDb();
        await SeedAsync(db);
        db.Users.Add(Row("Ramesh Patel", ManagerRole, email: "ramesh@example.com"));
        db.Users.Add(Row("Sita Sharma", ManagerRole));
        await db.SaveChangesAsync();

        // A pasted term arrives with a trailing space; lowercase must still match "Ramesh".
        var result = await Run(db, new UserFilterDto { Search = "ramesh ", Page = 1, PageSize = 25 });

        Assert.Equal("Ramesh Patel", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task RoleNameFilter_FindsTechniciansWhoeverElseExists()
    {
        // The technician dropdown: narrowing the first 100 users client-side returned an empty list
        // whenever those 100 happened to hold no technician.
        var db = NewDb();
        await SeedAsync(db);
        for (var i = 0; i < 120; i++) db.Users.Add(Row($"Manager {i:D3}", ManagerRole));
        db.Users.Add(Row("Zahir the Technician", TechnicianRole));   // sorts last by name
        await db.SaveChangesAsync();

        var result = await Run(db, new UserFilterDto
        {
            RoleName = "Technician", IsActive = true, Page = 1, PageSize = 100
        });

        Assert.Equal("Zahir the Technician", Assert.Single(result.Items).Name);
    }

    [Fact]
    public async Task SortIsServerSide_AndHonoursDirection()
    {
        var db = NewDb();
        await SeedAsync(db);
        db.Users.Add(Row("Anil", ManagerRole));
        db.Users.Add(Row("Zoya", ManagerRole));
        await db.SaveChangesAsync();

        var desc = await Run(db, new UserFilterDto
        {
            SortBy = "name", SortDir = "desc", Page = 1, PageSize = 25
        });

        Assert.Equal("Zoya", desc.Items[0].Name);
    }
}
