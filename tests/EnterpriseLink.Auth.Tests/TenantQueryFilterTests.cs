using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Auth.Tests;

/// <summary>
/// Proves that the EF Core tenant interceptor correctly:
///   1. Auto-injects TenantId on SaveChanges (INSERT)
///   2. Filters queries so a tenant only sees its own rows (SELECT)
///   3. Blocks cross-tenant data access completely
/// </summary>
public sealed class TenantQueryFilterTests
{
    // Use a shared in-memory database name so all contexts in a test share state.
    private readonly string _dbName = $"el_test_{Guid.NewGuid()}";

    private AppDbContext BuildContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options,
            new FixedTenantContext(tenantId));

    // ── Test 1: TenantId auto-injected on INSERT ──────────────────────────────

    [Fact]
    public async Task SaveChanges_auto_injects_TenantId_on_new_entity()
    {
        var tenantId = Guid.NewGuid();
        await SeedTenantAsync(tenantId);

        await using var ctx = BuildContext(tenantId);
        var user = BuildUser(); // TenantId intentionally NOT set by caller

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        user.TenantId.Should().Be(tenantId,
            "AppDbContext.ApplyTenantId() must stamp TenantId from ITenantContext");
    }

    // ── Test 2: Query returns only the current tenant's rows ──────────────────

    [Fact]
    public async Task Query_returns_only_current_tenant_rows()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedTenantAsync(tenantA);
        await SeedTenantAsync(tenantB);

        // Insert 2 users for Tenant A and 1 for Tenant B
        await using (var ctxA = BuildContext(tenantA))
        {
            ctxA.Users.AddRange(BuildUser(), BuildUser());
            await ctxA.SaveChangesAsync();
        }

        await using (var ctxB = BuildContext(tenantB))
        {
            ctxB.Users.Add(BuildUser());
            await ctxB.SaveChangesAsync();
        }

        // Querying as Tenant A must only return Tenant A's 2 rows
        await using var readCtxA = BuildContext(tenantA);
        var tenantAUsers = await readCtxA.Users.ToListAsync();

        tenantAUsers.Should().HaveCount(2,
            "global query filter must scope SELECT to the current TenantId");

        tenantAUsers.Should().AllSatisfy(u =>
            u.TenantId.Should().Be(tenantA,
                "no cross-tenant rows must leak through the filter"));
    }

    // ── Test 3: Cross-tenant access is completely blocked ─────────────────────

    [Fact]
    public async Task Query_blocks_cross_tenant_access()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedTenantAsync(tenantA);
        await SeedTenantAsync(tenantB);

        // Insert 3 users for Tenant A
        await using (var ctxA = BuildContext(tenantA))
        {
            ctxA.Users.AddRange(BuildUser(), BuildUser(), BuildUser());
            await ctxA.SaveChangesAsync();
        }

        // Tenant B context must see zero rows from Tenant A
        await using var ctxB = BuildContext(tenantB);
        var tenantBUsers = await ctxB.Users.ToListAsync();

        tenantBUsers.Should().BeEmpty(
            "Tenant B must never see Tenant A rows — cross-tenant access must be blocked");
    }

    // ── Test 4: IgnoreQueryFilters bypasses filter (admin use case) ───────────

    [Fact]
    public async Task IgnoreQueryFilters_returns_all_rows_for_admin_operations()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        await SeedTenantAsync(tenantA);
        await SeedTenantAsync(tenantB);

        await using (var ctxA = BuildContext(tenantA))
        {
            ctxA.Users.Add(BuildUser());
            await ctxA.SaveChangesAsync();
        }

        await using (var ctxB = BuildContext(tenantB))
        {
            ctxB.Users.Add(BuildUser());
            await ctxB.SaveChangesAsync();
        }

        // Admin context (NullTenantContext) with IgnoreQueryFilters sees all rows
        await using var adminCtx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options,
            NullTenantContext.Instance);

        var allUsers = await adminCtx.Users.IgnoreQueryFilters().ToListAsync();

        allUsers.Should().HaveCount(2,
            "IgnoreQueryFilters must bypass the tenant filter for admin/system operations");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task SeedTenantAsync(Guid tenantId)
    {
        await using var ctx = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(_dbName)
                .Options,
            NullTenantContext.Instance);

        if (!await ctx.Tenants.IgnoreQueryFilters().AnyAsync(t => t.TenantId == tenantId))
        {
            ctx.Tenants.Add(new Tenant
            {
                TenantId = tenantId,
                Name = $"Tenant-{tenantId:N}",
                IndustryType = IndustryType.Financial,
                IsActive = true,
            });
            await ctx.SaveChangesAsync();
        }
    }

    private static User BuildUser() =>
        new()
        {
            UserId = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@test.com",
            PasswordHash = "hashed",
            Role = UserRole.Operator,
            IsActive = true,
        };
}
