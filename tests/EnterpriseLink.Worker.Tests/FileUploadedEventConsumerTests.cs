using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Batch;
using EnterpriseLink.Worker.Consumers;
using EnterpriseLink.Worker.Idempotency;
using EnterpriseLink.Worker.MultiTenancy;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Storage;
using EnterpriseLink.Worker.Validation;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Integration tests for <see cref="FileUploadedEventConsumer"/> using the
/// MassTransit in-memory test harness.
///
/// <para>
/// All tests use <c>ITestHarness</c> so they exercise the full MassTransit
/// dispatch pipeline (serialisation, routing, retry) without a real broker.
/// All dependencies of the consumer are mocked so tests run fully in-memory.
/// </para>
///
/// <para>Acceptance criteria covered:</para>
/// <list type="bullet">
///   <item><description>Subscribes to queue.</description></item>
///   <item><description>Handles messages — valid events succeed, invalid events fault.</description></item>
///   <item><description>Commit every N records — inserter is invoked and wired to the parser.</description></item>
///   <item><description>Duplicate processing avoided — idempotency guard controls flow.</description></item>
///   <item><description>Required fields enforced / Invalid records stored separately — validation pipeline wired.</description></item>
/// </list>
/// </summary>
public sealed class FileUploadedEventConsumerTests : IAsyncLifetime
{
    // ── Harness ──────────────────────────────────────────────────────────────
    private readonly ServiceProvider _provider;
    private readonly ITestHarness _harness;

    // Exposed so individual tests can reconfigure return values.
    private readonly Mock<IFileStorageResolver> _resolverMock;
    private readonly Mock<ICsvStreamingParser> _parserMock;
    private readonly Mock<IValidationPipeline> _pipelineMock;
    private readonly Mock<IBatchRowInserter> _inserterMock;
    private readonly Mock<IInvalidRowPersister> _invalidPersisterMock;
    private readonly Mock<IUploadIdempotencyGuard> _idempotencyMock;

    public FileUploadedEventConsumerTests()
    {
        var loggerMock = new Mock<ILogger<FileUploadedEventConsumer>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _resolverMock = new Mock<IFileStorageResolver>();
        _resolverMock
            .Setup(r => r.ResolveFullPath(It.IsAny<string>()))
            .Returns("/tmp/resolved/data.csv");

        _parserMock = new Mock<ICsvStreamingParser>();
        _parserMock
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable());

        // Default pipeline: returns all rows as valid, no invalid rows.
        _pipelineMock = new Mock<IValidationPipeline>();
        _pipelineMock
            .Setup(p => p.ClassifyAsync(
                It.IsAny<IAsyncEnumerable<ParsedRow>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                (IReadOnlyList<ParsedRow>)Array.Empty<ParsedRow>(),
                (IReadOnlyList<(ParsedRow, IReadOnlyList<ValidationError>, string)>)
                    Array.Empty<(ParsedRow, IReadOnlyList<ValidationError>, string)>()));

        _inserterMock = new Mock<IBatchRowInserter>();
        _inserterMock
            .Setup(i => i.InsertAsync(
                It.IsAny<IAsyncEnumerable<ParsedRow>>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0); // 0 matches ValidEvent().DataRowCount — no mismatch warning

        _invalidPersisterMock = new Mock<IInvalidRowPersister>();
        _invalidPersisterMock
            .Setup(p => p.PersistAsync(
                It.IsAny<IReadOnlyList<(ParsedRow, IReadOnlyList<ValidationError>, string)>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        _idempotencyMock = new Mock<IUploadIdempotencyGuard>();
        _idempotencyMock
            .Setup(g => g.TryBeginAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _idempotencyMock
            .Setup(g => g.CompleteAsync(
                It.IsAny<Guid>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _idempotencyMock
            .Setup(g => g.FailAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _provider = new ServiceCollection()
            .AddSingleton(loggerMock.Object)
            // WorkerTenantContext is scoped — each MassTransit message scope gets its own.
            .AddScoped<WorkerTenantContext>()
            // Mocked dependencies registered as singletons so Verify() sees all calls.
            .AddSingleton(_resolverMock.Object)
            .AddSingleton(_parserMock.Object)
            .AddSingleton(_pipelineMock.Object)
            .AddSingleton(_inserterMock.Object)
            .AddSingleton(_invalidPersisterMock.Object)
            .AddSingleton(_idempotencyMock.Object)
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<FileUploadedEventConsumer>();
            })
            .BuildServiceProvider(validateScopes: true);

        _harness = _provider.GetRequiredService<ITestHarness>();
    }

    public async Task InitializeAsync() => await _harness.Start();

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

#pragma warning disable CS1998
    private static async IAsyncEnumerable<ParsedRow> EmptyAsyncEnumerable()
    {
        yield break;
    }
#pragma warning restore CS1998

    private static FileUploadedEvent ValidEvent(Guid? uploadId = null, Guid? tenantId = null) =>
        new()
        {
            UploadId = uploadId ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            StoragePath = "tenant-1/upload-abc/data.csv",
            FileName = "data.csv",
            FileSizeBytes = 1024,
            DataRowCount = 0, // 0 matches default inserter mock return — no mismatch warning
            SourceSystem = "SalesForce",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    // ── Subscribes to queue ───────────────────────────────────────────────────

    [Fact]
    public async Task Consumer_is_registered_and_receives_published_event()
    {
        await _harness.Bus.Publish(ValidEvent());

        (await _harness.Consumed.Any<FileUploadedEvent>())
            .Should().BeTrue("the consumer must subscribe to FileUploadedEvent");
    }

    [Fact]
    public async Task Consumer_harness_consumed_message_with_correct_UploadId()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        var consumerHarness = _harness.GetConsumerHarness<FileUploadedEventConsumer>();
        (await consumerHarness.Consumed.Any<FileUploadedEvent>()).Should().BeTrue();

        var consumed = consumerHarness.Consumed.Select<FileUploadedEvent>().First();
        consumed.Context.Message.UploadId.Should().Be(evt.UploadId);
    }

    // ── Handles messages — happy path ─────────────────────────────────────────

    [Fact]
    public async Task Consume_valid_event_does_not_fault()
    {
        await _harness.Bus.Publish(ValidEvent());
        await _harness.Consumed.Any<FileUploadedEvent>();

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a valid event must not produce a fault");
    }

    [Fact]
    public async Task Consume_valid_event_invokes_storage_resolver()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _resolverMock.Verify(
            r => r.ResolveFullPath(evt.StoragePath),
            Times.Once,
            "consumer must resolve the relative StoragePath to an absolute path");
    }

    [Fact]
    public async Task Consume_valid_event_invokes_csv_parser()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _parserMock.Verify(
            p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer must pass the resolved path to the CSV streaming parser");
    }

    [Fact]
    public async Task Consume_valid_event_invokes_validation_pipeline()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _pipelineMock.Verify(
            p => p.ClassifyAsync(It.IsAny<IAsyncEnumerable<ParsedRow>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer must pass parsed rows through the validation pipeline");
    }

    [Fact]
    public async Task Consume_valid_event_invokes_batch_inserter()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _inserterMock.Verify(
            i => i.InsertAsync(
                It.IsAny<IAsyncEnumerable<ParsedRow>>(),
                evt.TenantId,
                evt.UploadId,
                evt.SourceSystem,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer must invoke the batch inserter with the valid rows");
    }

    [Fact]
    public async Task Consume_valid_event_invokes_invalid_row_persister()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _invalidPersisterMock.Verify(
            p => p.PersistAsync(
                It.IsAny<IReadOnlyList<(ParsedRow, IReadOnlyList<ValidationError>, string)>>(),
                evt.UploadId,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer must invoke the invalid row persister to store rejected rows");
    }

    [Fact]
    public async Task Consume_valid_event_succeeds_for_multiple_tenants()
    {
        var events = Enumerable.Range(0, 3).Select(_ => ValidEvent()).ToList();

        foreach (var evt in events)
            await _harness.Bus.Publish(evt);

        await Task.Delay(500);

        var consumerHarness = _harness.GetConsumerHarness<FileUploadedEventConsumer>();
        consumerHarness.Consumed.Select<FileUploadedEvent>().Should().HaveCount(3);
    }

    [Fact]
    public async Task Consume_preserves_all_event_properties()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        var consumerHarness = _harness.GetConsumerHarness<FileUploadedEventConsumer>();
        var msg = consumerHarness.Consumed.Select<FileUploadedEvent>().First().Context.Message;

        msg.UploadId.Should().Be(evt.UploadId);
        msg.TenantId.Should().Be(evt.TenantId);
        msg.StoragePath.Should().Be(evt.StoragePath);
        msg.FileName.Should().Be(evt.FileName);
        msg.FileSizeBytes.Should().Be(evt.FileSizeBytes);
        msg.DataRowCount.Should().Be(evt.DataRowCount);
        msg.SourceSystem.Should().Be(evt.SourceSystem);
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Consume_invokes_idempotency_guard_before_processing()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _idempotencyMock.Verify(
            g => g.TryBeginAsync(
                evt.UploadId,
                evt.TenantId,
                evt.SourceSystem,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer must call TryBeginAsync before resolving the file path");
    }

    [Fact]
    public async Task Consume_marks_complete_after_successful_insert()
    {
        _inserterMock
            .Setup(i => i.InsertAsync(
                It.IsAny<IAsyncEnumerable<ParsedRow>>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(42);

        var evt = ValidEvent() with { DataRowCount = 42 };
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _idempotencyMock.Verify(
            g => g.CompleteAsync(evt.UploadId, 42, It.IsAny<CancellationToken>()),
            Times.Once,
            "consumer must mark the upload complete with the final row count");
    }

    [Fact]
    public async Task Consume_skips_processing_when_already_completed()
    {
        // Guard returns false → upload already done; consumer must ack and skip.
        _idempotencyMock
            .Setup(g => g.TryBeginAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _harness.Bus.Publish(ValidEvent());
        await _harness.Consumed.Any<FileUploadedEvent>();

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a duplicate message must be silently acknowledged, not faulted");

        _resolverMock.Verify(
            r => r.ResolveFullPath(It.IsAny<string>()),
            Times.Never,
            "storage resolver must not be invoked for already-completed uploads");

        _inserterMock.Verify(
            i => i.InsertAsync(
                It.IsAny<IAsyncEnumerable<ParsedRow>>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "batch inserter must not be invoked for already-completed uploads");
    }

    // ── Handles messages — validation guards ──────────────────────────────────

    [Fact]
    public async Task Consume_event_with_empty_UploadId_faults()
    {
        await _harness.Bus.Publish(ValidEvent(uploadId: Guid.Empty));
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("empty UploadId must cause a fault after retries exhausted");
    }

    [Fact]
    public async Task Consume_event_with_empty_TenantId_faults()
    {
        await _harness.Bus.Publish(ValidEvent(tenantId: Guid.Empty));
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("empty TenantId must cause a fault after retries exhausted");
    }

    [Fact]
    public async Task Consume_event_with_blank_StoragePath_faults()
    {
        await _harness.Bus.Publish(ValidEvent() with { StoragePath = "   " });
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("blank StoragePath must cause a fault");
    }

    [Fact]
    public async Task Consume_event_with_blank_FileName_faults()
    {
        await _harness.Bus.Publish(ValidEvent() with { FileName = "" });
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("blank FileName must cause a fault");
    }

    // ── Retry behaviour ───────────────────────────────────────────────────────

    [Fact]
    public async Task Faulted_message_is_published_to_fault_topic()
    {
        await _harness.Bus.Publish(ValidEvent(uploadId: Guid.Empty));
        await Task.Delay(500);

        _harness.Published.Select<Fault<FileUploadedEvent>>()
            .Should().NotBeEmpty("MassTransit must publish a Fault<FileUploadedEvent> to the error topic");
    }

    // ── UploadedAt future-date guard ──────────────────────────────────────────

    [Fact]
    public async Task Consume_event_with_future_UploadedAt_beyond_grace_faults()
    {
        await _harness.Bus.Publish(ValidEvent() with { UploadedAt = DateTimeOffset.UtcNow.AddMinutes(10) });
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("an UploadedAt timestamp far in the future must cause a fault");
    }

    [Fact]
    public async Task Consume_event_with_UploadedAt_within_grace_window_succeeds()
    {
        await _harness.Bus.Publish(ValidEvent() with { UploadedAt = DateTimeOffset.UtcNow.AddMinutes(2) });
        await _harness.Consumed.Any<FileUploadedEvent>();

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a timestamp within the clock-skew grace window must not cause a fault");
    }

    [Fact]
    public async Task Consume_event_with_past_UploadedAt_succeeds()
    {
        await _harness.Bus.Publish(ValidEvent() with { UploadedAt = DateTimeOffset.UtcNow.AddHours(-1) });
        await _harness.Consumed.Any<FileUploadedEvent>();

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a past UploadedAt timestamp is the normal case and must not fault");
    }

    // ── Error handling — FailAsync is called before re-throw ──────────────────

    [Fact]
    public async Task Consume_marks_failed_when_storage_resolver_throws()
    {
        var evt = ValidEvent();
        _resolverMock
            .Setup(r => r.ResolveFullPath(It.IsAny<string>()))
            .Throws(new ArgumentException("Path traversal blocked"));

        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("a resolver exception must propagate as a fault");

        _idempotencyMock.Verify(
            g => g.FailAsync(evt.UploadId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FailAsync must be called before re-throwing so the next retry can distinguish failure");
    }

    [Fact]
    public async Task Consume_marks_failed_when_csv_file_not_found()
    {
        var evt = ValidEvent();
        _parserMock
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new FileNotFoundException("File was deleted between ingestion and processing"));

        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("a missing file must fault so MassTransit can retry / dead-letter");

        _idempotencyMock.Verify(
            g => g.FailAsync(evt.UploadId, It.IsAny<CancellationToken>()),
            Times.Once,
            "FailAsync must be called so the Failed record allows retry on next delivery");
    }

    [Fact]
    public async Task Consume_faults_when_storage_resolver_throws()
    {
        _resolverMock
            .Setup(r => r.ResolveFullPath(It.IsAny<string>()))
            .Throws(new ArgumentException("Path traversal blocked"));

        await _harness.Bus.Publish(ValidEvent());
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("a resolver exception must propagate as a fault");
    }

    [Fact]
    public async Task Consume_faults_when_csv_file_not_found()
    {
        _parserMock
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Throws(new FileNotFoundException("File was deleted"));

        await _harness.Bus.Publish(ValidEvent());
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("a missing file must cause a fault so the message can be retried");
    }

    // ── Validation pipeline integration ───────────────────────────────────────

    [Fact]
    public async Task Consume_invokes_invalid_row_persister_with_upload_id()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        _invalidPersisterMock.Verify(
            p => p.PersistAsync(
                It.IsAny<IReadOnlyList<(ParsedRow, IReadOnlyList<ValidationError>, string)>>(),
                evt.UploadId,
                It.IsAny<CancellationToken>()),
            Times.Once,
            "invalid row persister must receive the event's UploadId for correlation");
    }
}
