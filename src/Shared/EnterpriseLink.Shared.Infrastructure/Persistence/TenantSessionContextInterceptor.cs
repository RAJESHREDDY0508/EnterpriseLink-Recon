using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace EnterpriseLink.Shared.Infrastructure.Persistence;

/// <summary>
/// DbConnection interceptor that sets SQL Server SESSION_CONTEXT on every
/// connection open. This is the runtime bridge for Row-Level Security (RLS).
///
/// SQL Server RLS policy reads SESSION_CONTEXT(N'TenantId') inside the
/// predicate function fn_TenantAccessPredicate. Without this interceptor
/// the session context is NULL, causing ALL rows to be filtered out.
///
/// Security note: the context is set with @read_only = 1, which prevents
/// any T-SQL inside the session from overriding the TenantId value.
/// </summary>
internal sealed class TenantSessionContextInterceptor : DbConnectionInterceptor
{
    private readonly ITenantContext _tenantContext;

    public TenantSessionContextInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetSessionContextAsync(connection, cancellationToken);
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        SetSessionContextAsync(connection, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private async Task SetSessionContextAsync(DbConnection connection, CancellationToken ct)
    {
        if (!_tenantContext.HasTenant)
            return;

        await using var cmd = connection.CreateCommand();

        // @read_only = 1 locks the value for the life of the connection,
        // preventing elevation attacks from stored procedures or dynamic SQL.
        cmd.CommandText =
            "EXEC sp_set_session_context N'TenantId', @tenantId, @read_only = 1;";

        var param = cmd.CreateParameter();
        param.ParameterName = "@tenantId";
        param.Value = _tenantContext.TenantId;
        cmd.Parameters.Add(param);

        await cmd.ExecuteNonQueryAsync(ct);
    }
}
