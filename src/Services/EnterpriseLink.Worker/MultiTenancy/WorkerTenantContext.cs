using EnterpriseLink.Shared.Infrastructure.MultiTenancy;

namespace EnterpriseLink.Worker.MultiTenancy;

/// <summary>
/// Mutable <see cref="ITenantContext"/> implementation for the Worker service.
///
/// <para>
/// Each MassTransit message scope creates one instance via DI. The
/// <c>FileUploadedEventConsumer</c> sets <see cref="TenantId"/> from
/// <c>FileUploadedEvent.TenantId</c> before performing any database operations,
/// so the <c>AppDbContext</c> global query filters and <c>ApplyTenantId</c>
/// interceptor are scoped to the correct tenant for every batch insert.
/// </para>
///
/// <para><b>Thread safety</b>
/// MassTransit guarantees that a single consumer scope is not accessed concurrently,
/// so mutation of <see cref="TenantId"/> within one <c>Consume</c> call is safe.
/// Do NOT share a <see cref="WorkerTenantContext"/> instance across concurrent scopes.
/// </para>
///
/// <para><b>Why not <c>FixedTenantContext</c>?</b>
/// <c>FixedTenantContext</c> is immutable (TenantId is get-only) and is designed for
/// unit tests that know the TenantId at construction time. The Worker does not know
/// the TenantId until the message is deserialized at consume time, hence the need
/// for a mutable context that can be set in the consuming method body.
/// </para>
/// </summary>
public sealed class WorkerTenantContext : ITenantContext
{
    /// <inheritdoc />
    /// <remarks>Defaults to <see cref="Guid.Empty"/> until set by the consumer.</remarks>
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public bool HasTenant => TenantId != Guid.Empty;
}
