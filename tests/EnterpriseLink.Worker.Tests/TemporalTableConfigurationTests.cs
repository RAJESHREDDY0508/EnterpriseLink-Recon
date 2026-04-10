using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests verifying that SQL Server temporal table configuration is applied
/// to all mutable entities via EF Core's <c>IsTemporal</c> model annotations.
///
/// <para>Acceptance criterion: <b>Full row-level history stored</b></para>
///
/// <para>
/// These tests inspect the EF Core compiled model to confirm that each of the
/// five mutable tables has a temporal period start (<c>SysStartTime</c>) and
/// period end (<c>SysEndTime</c>) shadow property, and that a history table name
/// is configured. SQL Server will automatically populate the history tables on
/// every DML operation when the migration is applied.
/// </para>
///
/// <para>
/// Tests run against the InMemory provider (no SQL Server required). The InMemory
/// provider ignores temporal SQL but the <em>model annotations</em> are still
/// present — this is what we verify. Integration tests on real SQL Server would
/// confirm the history-table writes, which are outside the scope of unit tests.
/// </para>
/// </summary>
public sealed class TemporalTableConfigurationTests
{
    private readonly IModel _model;

    public TemporalTableConfigurationTests()
    {
        var tenantContext = new WorkerTenantContext { TenantId = Guid.NewGuid() };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var ctx = new AppDbContext(options, tenantContext);
        // Materialise the compiled model so we can inspect annotations.
        _model = ctx.Model;
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the EF Core entity type for <typeparamref name="T"/>.
    /// </summary>
    private IEntityType EntityType<T>() =>
        _model.FindEntityType(typeof(T))
        ?? throw new InvalidOperationException($"{typeof(T).Name} not found in model.");

    /// <summary>
    /// Returns true when the entity has a shadow property with the given name,
    /// which is how EF Core surfaces temporal period columns on the CLR type.
    /// </summary>
    private static bool HasShadowProperty(IEntityType entityType, string propertyName) =>
        entityType.FindProperty(propertyName)?.IsShadowProperty() == true;

    /// <summary>
    /// Returns true when the entity's table mapping contains a temporal history-
    /// table annotation — i.e. <c>IsTemporal()</c> was called in configuration.
    /// </summary>
    private static bool HasTemporalHistoryTable(IEntityType entityType)
    {
        // EF Core stores the history table name in the "SqlServer:TemporalHistoryTableName"
        // annotation on the entity type. If present (non-null), temporal is configured.
        var annotation = entityType.FindAnnotation("SqlServer:TemporalHistoryTableName");
        return annotation?.Value is string { Length: > 0 };
    }

    // ── Transactions ──────────────────────────────────────────────────────────

    [Fact]
    public void Transactions_table_has_temporal_history_table_configured()
    {
        HasTemporalHistoryTable(EntityType<Transaction>()).Should().BeTrue(
            "Transactions must be system-versioned with a TransactionsHistory table");
    }

    [Fact]
    public void Transactions_table_has_SysStartTime_shadow_property()
    {
        HasShadowProperty(EntityType<Transaction>(), "SysStartTime").Should().BeTrue(
            "SysStartTime period-start column must be configured on Transaction");
    }

    [Fact]
    public void Transactions_table_has_SysEndTime_shadow_property()
    {
        HasShadowProperty(EntityType<Transaction>(), "SysEndTime").Should().BeTrue(
            "SysEndTime period-end column must be configured on Transaction");
    }

    // ── Tenants ───────────────────────────────────────────────────────────────

    [Fact]
    public void Tenants_table_has_temporal_history_table_configured()
    {
        HasTemporalHistoryTable(EntityType<Tenant>()).Should().BeTrue(
            "Tenants must be system-versioned with a TenantsHistory table");
    }

    [Fact]
    public void Tenants_table_has_SysStartTime_shadow_property()
    {
        HasShadowProperty(EntityType<Tenant>(), "SysStartTime").Should().BeTrue(
            "SysStartTime period-start column must be configured on Tenant");
    }

    [Fact]
    public void Tenants_table_has_SysEndTime_shadow_property()
    {
        HasShadowProperty(EntityType<Tenant>(), "SysEndTime").Should().BeTrue(
            "SysEndTime period-end column must be configured on Tenant");
    }

    // ── Users ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Users_table_has_temporal_history_table_configured()
    {
        HasTemporalHistoryTable(EntityType<User>()).Should().BeTrue(
            "Users must be system-versioned with a UsersHistory table");
    }

    [Fact]
    public void Users_table_has_SysStartTime_shadow_property()
    {
        HasShadowProperty(EntityType<User>(), "SysStartTime").Should().BeTrue(
            "SysStartTime period-start column must be configured on User");
    }

    [Fact]
    public void Users_table_has_SysEndTime_shadow_property()
    {
        HasShadowProperty(EntityType<User>(), "SysEndTime").Should().BeTrue(
            "SysEndTime period-end column must be configured on User");
    }

    // ── ProcessedUploads ──────────────────────────────────────────────────────

    [Fact]
    public void ProcessedUploads_table_has_temporal_history_table_configured()
    {
        HasTemporalHistoryTable(EntityType<ProcessedUpload>()).Should().BeTrue(
            "ProcessedUploads must be system-versioned with a ProcessedUploadsHistory table");
    }

    [Fact]
    public void ProcessedUploads_table_has_SysStartTime_shadow_property()
    {
        HasShadowProperty(EntityType<ProcessedUpload>(), "SysStartTime").Should().BeTrue(
            "SysStartTime period-start column must be configured on ProcessedUpload");
    }

    [Fact]
    public void ProcessedUploads_table_has_SysEndTime_shadow_property()
    {
        HasShadowProperty(EntityType<ProcessedUpload>(), "SysEndTime").Should().BeTrue(
            "SysEndTime period-end column must be configured on ProcessedUpload");
    }

    // ── InvalidTransactions ───────────────────────────────────────────────────

    [Fact]
    public void InvalidTransactions_table_has_temporal_history_table_configured()
    {
        HasTemporalHistoryTable(EntityType<InvalidTransaction>()).Should().BeTrue(
            "InvalidTransactions must be system-versioned with an InvalidTransactionsHistory table");
    }

    [Fact]
    public void InvalidTransactions_table_has_SysStartTime_shadow_property()
    {
        HasShadowProperty(EntityType<InvalidTransaction>(), "SysStartTime").Should().BeTrue(
            "SysStartTime period-start column must be configured on InvalidTransaction");
    }

    [Fact]
    public void InvalidTransactions_table_has_SysEndTime_shadow_property()
    {
        HasShadowProperty(EntityType<InvalidTransaction>(), "SysEndTime").Should().BeTrue(
            "SysEndTime period-end column must be configured on InvalidTransaction");
    }

    // ── AuditLog must NOT be temporal (it is already an audit trail) ──────────

    [Fact]
    public void AuditLog_table_is_NOT_temporal()
    {
        HasTemporalHistoryTable(EntityType<AuditLog>()).Should().BeFalse(
            "AuditLog is an append-only audit trail — system-versioning would be redundant and wasteful");
    }
}
