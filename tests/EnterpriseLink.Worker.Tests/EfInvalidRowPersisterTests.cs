using System.Text.Json;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.MultiTenancy;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="EfInvalidRowPersister"/>.
///
/// <para>Acceptance criterion: <b>Invalid records stored separately</b></para>
/// </summary>
public sealed class EfInvalidRowPersisterTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly WorkerTenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _uploadId = Guid.NewGuid();

    public EfInvalidRowPersisterTests()
    {
        _tenantContext = new WorkerTenantContext { TenantId = _tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, _tenantContext);
    }

    public void Dispose() => _context.Dispose();

    private EfInvalidRowPersister BuildPersister(int batchSize = 500)
    {
        var opts = Options.Create(new BatchInsertOptions { BatchSize = batchSize });
        return new EfInvalidRowPersister(_context, opts, NullLogger<EfInvalidRowPersister>.Instance);
    }

    private static (ParsedRow Row, IReadOnlyList<ValidationError> Errors, string FailureReason)
        InvalidEntry(int rowNumber, string amount, ValidationErrorCode code, string reason)
    {
        var row = new ParsedRow(rowNumber, new Dictionary<string, string>
        {
            ["Amount"] = amount,
            ["Id"] = $"REF-{rowNumber}"
        });

        var errors = new[]
        {
            new ValidationError("Amount", $"Row {rowNumber}: test error", code)
        };

        return (row, errors.AsReadOnly(), reason);
    }

    // ── Basic persistence ─────────────────────────────────────────────────────

    [Fact]
    public async Task PersistAsync_returns_zero_for_empty_list()
    {
        var persister = BuildPersister();
        var count = await persister.PersistAsync([], _uploadId);
        count.Should().Be(0);
    }

    [Fact]
    public async Task PersistAsync_returns_count_of_persisted_rows()
    {
        var persister = BuildPersister();
        var entries = new[]
        {
            InvalidEntry(1, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema"),
            InvalidEntry(2, "-5", ValidationErrorCode.ValueOutOfRange, "BusinessRule"),
            InvalidEntry(3, "10", ValidationErrorCode.DuplicateRecord, "Duplicate"),
        };

        var count = await persister.PersistAsync(entries, _uploadId);

        count.Should().Be(3);
    }

    [Fact]
    public async Task PersistAsync_writes_correct_row_count_to_database()
    {
        var persister = BuildPersister();
        var entries = new[]
        {
            InvalidEntry(1, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema"),
            InvalidEntry(2, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema"),
        };

        await persister.PersistAsync(entries, _uploadId);

        var dbCount = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .CountAsync();

        dbCount.Should().Be(2, "both invalid rows must be persisted");
    }

    // ── Field mapping ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PersistAsync_sets_UploadId_on_all_records()
    {
        var persister = BuildPersister();
        var entries = new[] { InvalidEntry(1, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema") };

        await persister.PersistAsync(entries, _uploadId);

        var record = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .SingleAsync();

        record.UploadId.Should().Be(_uploadId);
    }

    [Fact]
    public async Task PersistAsync_sets_RowNumber_correctly()
    {
        var persister = BuildPersister();
        var entries = new[] { InvalidEntry(42, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema") };

        await persister.PersistAsync(entries, _uploadId);

        var record = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .SingleAsync();

        record.RowNumber.Should().Be(42);
    }

    [Fact]
    public async Task PersistAsync_sets_FailureReason_correctly()
    {
        var persister = BuildPersister();
        var entries = new[] { InvalidEntry(1, "-1", ValidationErrorCode.ValueOutOfRange, "BusinessRule") };

        await persister.PersistAsync(entries, _uploadId);

        var record = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .SingleAsync();

        record.FailureReason.Should().Be("BusinessRule");
    }

    [Fact]
    public async Task PersistAsync_serialises_RawData_as_json()
    {
        var persister = BuildPersister();
        var entries = new[] { InvalidEntry(1, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema") };

        await persister.PersistAsync(entries, _uploadId);

        var record = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .SingleAsync();

        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(record.RawData);
        parsed.Should().NotBeNull();
        parsed!.Should().ContainKey("Amount");
    }

    [Fact]
    public async Task PersistAsync_serialises_ValidationErrors_as_json_array()
    {
        var persister = BuildPersister();
        var entries = new[] { InvalidEntry(1, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema") };

        await persister.PersistAsync(entries, _uploadId);

        var record = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .SingleAsync();

        var errors = JsonSerializer.Deserialize<List<string>>(record.ValidationErrors);
        errors.Should().NotBeNull();
        errors!.Should().HaveCount(1);
        errors[0].Should().Contain("RequiredFieldMissing");
    }

    // ── Batching ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task PersistAsync_handles_more_rows_than_batch_size()
    {
        var persister = BuildPersister(batchSize: 3);
        var entries = Enumerable.Range(1, 10)
            .Select(i => InvalidEntry(i, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema"))
            .ToList();

        var count = await persister.PersistAsync(entries, _uploadId);

        count.Should().Be(10);

        var dbCount = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .CountAsync();

        dbCount.Should().Be(10, "all 10 rows must be persisted across multiple batches");
    }

    // ── Tenant injection ──────────────────────────────────────────────────────

    [Fact]
    public async Task PersistAsync_sets_TenantId_via_context()
    {
        var persister = BuildPersister();
        var entries = new[] { InvalidEntry(1, "N/A", ValidationErrorCode.RequiredFieldMissing, "Schema") };

        await persister.PersistAsync(entries, _uploadId);

        var record = await _context.InvalidTransactions
            .IgnoreQueryFilters()
            .SingleAsync();

        record.TenantId.Should().Be(_tenantId,
            "TenantId must be auto-injected from WorkerTenantContext by AppDbContext.ApplyTenantId");
    }
}
