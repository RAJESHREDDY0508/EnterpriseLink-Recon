using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Idempotency;
using EnterpriseLink.Worker.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="EfUploadIdempotencyGuard"/>.
///
/// <para>
/// All tests use an EF Core InMemory database for full isolation without a real
/// SQL Server. Each test class instance gets its own uniquely named database.
/// </para>
///
/// <para>Acceptance criterion covered:</para>
/// <list type="bullet">
///   <item><description>Duplicate processing avoided — the guard returns <c>false</c>
///   for Completed uploads, and <c>true</c> (with status reset) for retries.</description></item>
/// </list>
/// </summary>
public sealed class UploadIdempotencyGuardTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly EfUploadIdempotencyGuard _guard;
    private readonly Guid _tenantId = Guid.NewGuid();

    public UploadIdempotencyGuardTests()
    {
        var tenantCtx = new WorkerTenantContext { TenantId = _tenantId };

        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"IdempotencyGuardTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(opts, tenantCtx);
        _context.Database.EnsureCreated();

        _guard = new EfUploadIdempotencyGuard(_context, NullLogger<EfUploadIdempotencyGuard>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ── TryBeginAsync — new upload ────────────────────────────────────────────

    [Fact]
    public async Task TryBeginAsync_returns_true_for_new_upload()
    {
        var result = await _guard.TryBeginAsync(Guid.NewGuid(), _tenantId, "SalesForce");
        result.Should().BeTrue("a new UploadId must be claimed successfully");
    }

    [Fact]
    public async Task TryBeginAsync_inserts_ProcessedUpload_row_with_Processing_status()
    {
        var uploadId = Guid.NewGuid();
        await _guard.TryBeginAsync(uploadId, _tenantId, "SAP");

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.Status.Should().Be(UploadProcessingStatus.Processing);
        record.TenantId.Should().Be(_tenantId);
        record.SourceSystem.Should().Be("SAP");
        record.RowsInserted.Should().Be(0);
    }

    // ── TryBeginAsync — completed upload (duplicate) ──────────────────────────

    [Fact]
    public async Task TryBeginAsync_returns_false_for_completed_upload()
    {
        var uploadId = Guid.NewGuid();

        // Seed a Completed record directly.
        _context.ProcessedUploads.Add(new ProcessedUpload
        {
            UploadId = uploadId,
            TenantId = _tenantId,
            Status = UploadProcessingStatus.Completed,
            SourceSystem = "Test",
            RowsInserted = 50,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await _guard.TryBeginAsync(uploadId, _tenantId, "Test");
        result.Should().BeFalse("a Completed upload must not be reprocessed");
    }

    [Fact]
    public async Task TryBeginAsync_does_not_modify_Completed_record()
    {
        var uploadId = Guid.NewGuid();

        _context.ProcessedUploads.Add(new ProcessedUpload
        {
            UploadId = uploadId,
            TenantId = _tenantId,
            Status = UploadProcessingStatus.Completed,
            SourceSystem = "Test",
            RowsInserted = 100,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _guard.TryBeginAsync(uploadId, _tenantId, "Test");

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.Status.Should().Be(UploadProcessingStatus.Completed, "status must remain Completed");
        record.RowsInserted.Should().Be(100, "row count must remain unchanged");
    }

    // ── TryBeginAsync — failed upload (retry allowed) ─────────────────────────

    [Fact]
    public async Task TryBeginAsync_returns_true_for_failed_upload()
    {
        var uploadId = Guid.NewGuid();

        _context.ProcessedUploads.Add(new ProcessedUpload
        {
            UploadId = uploadId,
            TenantId = _tenantId,
            Status = UploadProcessingStatus.Failed,
            SourceSystem = "Oracle",
            RowsInserted = 0,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await _guard.TryBeginAsync(uploadId, _tenantId, "Oracle");
        result.Should().BeTrue("a Failed upload must be retried");
    }

    [Fact]
    public async Task TryBeginAsync_resets_failed_upload_to_Processing()
    {
        var uploadId = Guid.NewGuid();

        _context.ProcessedUploads.Add(new ProcessedUpload
        {
            UploadId = uploadId,
            TenantId = _tenantId,
            Status = UploadProcessingStatus.Failed,
            SourceSystem = "Oracle",
            RowsInserted = 0,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await _guard.TryBeginAsync(uploadId, _tenantId, "Oracle");

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.Status.Should().Be(UploadProcessingStatus.Processing,
            "status must be reset to Processing so the retry proceeds cleanly");
        record.RowsInserted.Should().Be(0, "RowsInserted must be reset to 0 for the retry");
    }

    // ── TryBeginAsync — stale Processing upload (retry allowed) ──────────────

    [Fact]
    public async Task TryBeginAsync_returns_true_for_stale_Processing_upload()
    {
        // Simulate a record left in Processing by a crashed consumer.
        var uploadId = Guid.NewGuid();

        _context.ProcessedUploads.Add(new ProcessedUpload
        {
            UploadId = uploadId,
            TenantId = _tenantId,
            Status = UploadProcessingStatus.Processing,
            SourceSystem = "SalesForce",
            RowsInserted = 0,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await _guard.TryBeginAsync(uploadId, _tenantId, "SalesForce");
        result.Should().BeTrue("a stale Processing record must allow retry");
    }

    // ── CompleteAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_updates_status_to_Completed()
    {
        var uploadId = Guid.NewGuid();
        await _guard.TryBeginAsync(uploadId, _tenantId, "SAP");
        await _guard.CompleteAsync(uploadId, rowsInserted: 42);

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.Status.Should().Be(UploadProcessingStatus.Completed);
    }

    [Fact]
    public async Task CompleteAsync_sets_RowsInserted_to_final_count()
    {
        var uploadId = Guid.NewGuid();
        await _guard.TryBeginAsync(uploadId, _tenantId, "SAP");
        await _guard.CompleteAsync(uploadId, rowsInserted: 1_234);

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.RowsInserted.Should().Be(1_234);
    }

    // ── FailAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FailAsync_updates_status_to_Failed()
    {
        var uploadId = Guid.NewGuid();
        await _guard.TryBeginAsync(uploadId, _tenantId, "SalesForce");
        await _guard.FailAsync(uploadId);

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.Status.Should().Be(UploadProcessingStatus.Failed);
    }

    [Fact]
    public async Task FailAsync_preserves_RowsInserted_as_zero()
    {
        var uploadId = Guid.NewGuid();
        await _guard.TryBeginAsync(uploadId, _tenantId, "SalesForce");
        await _guard.FailAsync(uploadId);

        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);

        record.RowsInserted.Should().Be(0,
            "no rows were inserted before the failure so count stays at zero");
    }

    // ── Full lifecycle ────────────────────────────────────────────────────────

    [Fact]
    public async Task Full_lifecycle_Processing_to_Completed()
    {
        var uploadId = Guid.NewGuid();

        (await _guard.TryBeginAsync(uploadId, _tenantId, "SalesForce"))
            .Should().BeTrue("first claim must succeed");

        await _guard.CompleteAsync(uploadId, rowsInserted: 500);

        // Second delivery of same message — must be skipped.
        (await _guard.TryBeginAsync(uploadId, _tenantId, "SalesForce"))
            .Should().BeFalse("duplicate message for a Completed upload must be rejected");
    }

    [Fact]
    public async Task Full_lifecycle_Processing_to_Failed_to_Retry()
    {
        var uploadId = Guid.NewGuid();

        (await _guard.TryBeginAsync(uploadId, _tenantId, "SAP"))
            .Should().BeTrue("first claim must succeed");

        await _guard.FailAsync(uploadId);

        // MassTransit retry re-delivers the message.
        (await _guard.TryBeginAsync(uploadId, _tenantId, "SAP"))
            .Should().BeTrue("Failed upload must be retried");

        // Verify status is reset to Processing for the retry.
        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .SingleAsync(p => p.UploadId == uploadId);
        record.Status.Should().Be(UploadProcessingStatus.Processing);
    }
}
