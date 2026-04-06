using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Worker.Consumers;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Storage;
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
/// <see cref="IFileStorageResolver"/> and <see cref="ICsvStreamingParser"/> are
/// mocked so tests remain in-memory with no filesystem or CsvHelper dependency.
/// </para>
///
/// <para>Acceptance criteria covered:</para>
/// <list type="bullet">
///   <item><description>Subscribes to queue — consumer is registered and receives messages.</description></item>
///   <item><description>Handles messages — consumer processes valid events and faults on invalid ones.</description></item>
/// </list>
/// </summary>
public sealed class FileUploadedEventConsumerTests : IAsyncLifetime
{
    // ── Harness ──────────────────────────────────────────────────────────────
    private readonly ServiceProvider _provider;
    private readonly ITestHarness _harness;

    // Exposed so individual tests can configure return values.
    private readonly Mock<IFileStorageResolver> _resolverMock;
    private readonly Mock<ICsvStreamingParser> _parserMock;

    public FileUploadedEventConsumerTests()
    {
        var loggerMock = new Mock<ILogger<FileUploadedEventConsumer>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _resolverMock = new Mock<IFileStorageResolver>();
        // Default: return a harmless path for any input — tests that need different
        // behaviour override this setup individually.
        _resolverMock
            .Setup(r => r.ResolveFullPath(It.IsAny<string>()))
            .Returns("/tmp/resolved/data.csv");

        _parserMock = new Mock<ICsvStreamingParser>();
        // Default: return an empty async enumerable — no rows, no I/O.
        _parserMock
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable());

        _provider = new ServiceCollection()
            .AddSingleton(loggerMock.Object)
            .AddSingleton(_resolverMock.Object)
            .AddSingleton(_parserMock.Object)
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

#pragma warning disable CS1998 // async method with no await — intentional empty iterator
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
            DataRowCount = 0, // 0 matches empty mock parser — no warning logged
            SourceSystem = "SalesForce",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    // ── Subscribes to queue ───────────────────────────────────────────────────

    [Fact]
    public async Task Consumer_is_registered_and_receives_published_event()
    {
        var evt = ValidEvent();
        await _harness.Bus.Publish(evt);

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
        (await consumerHarness.Consumed.Any<FileUploadedEvent>())
            .Should().BeTrue();

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
    public async Task Consume_valid_event_succeeds_for_multiple_tenants()
    {
        var events = Enumerable.Range(0, 3).Select(_ => ValidEvent()).ToList();

        foreach (var evt in events)
            await _harness.Bus.Publish(evt);

        await Task.Delay(500);

        var consumerHarness = _harness.GetConsumerHarness<FileUploadedEventConsumer>();
        consumerHarness.Consumed.Select<FileUploadedEvent>().Should().HaveCount(3,
            "all three events must be handled");
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

    // ── Handles messages — validation guards ──────────────────────────────────

    [Fact]
    public async Task Consume_event_with_empty_UploadId_faults()
    {
        var evt = ValidEvent(uploadId: Guid.Empty);
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("empty UploadId must cause a fault after retries exhausted");
    }

    [Fact]
    public async Task Consume_event_with_empty_TenantId_faults()
    {
        var evt = ValidEvent(tenantId: Guid.Empty);
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("empty TenantId must cause a fault after retries exhausted");
    }

    [Fact]
    public async Task Consume_event_with_blank_StoragePath_faults()
    {
        var evt = ValidEvent() with { StoragePath = "   " };
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("blank StoragePath must cause a fault");
    }

    [Fact]
    public async Task Consume_event_with_blank_FileName_faults()
    {
        var evt = ValidEvent() with { FileName = "" };
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("blank FileName must cause a fault");
    }

    // ── Retry behaviour ───────────────────────────────────────────────────────

    [Fact]
    public async Task Faulted_message_is_published_to_fault_topic()
    {
        var malformed = ValidEvent(uploadId: Guid.Empty);
        await _harness.Bus.Publish(malformed);
        await Task.Delay(500);

        _harness.Published.Select<Fault<FileUploadedEvent>>()
            .Should().NotBeEmpty("MassTransit must publish a Fault<FileUploadedEvent> to the error topic");
    }

    // ── UploadedAt future-date guard ──────────────────────────────────────────

    [Fact]
    public async Task Consume_event_with_future_UploadedAt_beyond_grace_faults()
    {
        var evt = ValidEvent() with { UploadedAt = DateTimeOffset.UtcNow.AddMinutes(10) };
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("an UploadedAt timestamp far in the future must cause a fault");
    }

    [Fact]
    public async Task Consume_event_with_UploadedAt_within_grace_window_succeeds()
    {
        var evt = ValidEvent() with { UploadedAt = DateTimeOffset.UtcNow.AddMinutes(2) };
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a timestamp within the clock-skew grace window must not cause a fault");
    }

    [Fact]
    public async Task Consume_event_with_past_UploadedAt_succeeds()
    {
        var evt = ValidEvent() with { UploadedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a past UploadedAt timestamp is the normal case and must not fault");
    }

    // ── CSV streaming integration ─────────────────────────────────────────────

    /// <summary>
    /// Consumer iterates all rows returned by the parser — verifies the
    /// <c>await foreach</c> loop in the consumer is wired correctly.
    /// </summary>
    [Fact]
    public async Task Consume_iterates_all_rows_from_csv_parser()
    {
        // Arrange — parser returns 3 rows.
        static async IAsyncEnumerable<ParsedRow> ThreeRows()
        {
            for (var i = 1; i <= 3; i++)
            {
                await Task.Yield();
                yield return new ParsedRow(i,
                    new Dictionary<string, string> { ["Id"] = i.ToString() }.AsReadOnly());
            }
        }

        _parserMock
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ThreeRows());

        var evt = ValidEvent() with { DataRowCount = 3 };

        // Act
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        // Assert — no fault: all 3 rows consumed successfully.
        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("consuming 3 rows from the parser must not cause a fault");
    }

    /// <summary>
    /// If the storage resolver throws (e.g. path traversal blocked), the consumer
    /// faults and lets MassTransit retry / dead-letter the message.
    /// </summary>
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

    /// <summary>
    /// If the CSV parser throws <see cref="FileNotFoundException"/>, the consumer
    /// faults — the file was deleted between ingestion and processing.
    /// </summary>
    [Fact]
    public async Task Consume_faults_when_csv_file_not_found()
    {
        static async IAsyncEnumerable<ParsedRow> ThrowFileNotFound()
        {
            await Task.Yield();
            throw new FileNotFoundException("File was deleted");
#pragma warning disable CS0162 // Unreachable code — required to mark method as async iterator
            yield break;
#pragma warning restore CS0162
        }

        _parserMock
            .Setup(p => p.ParseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(ThrowFileNotFound());

        await _harness.Bus.Publish(ValidEvent());
        await Task.Delay(500);

        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeTrue("a missing file must cause a fault so the message can be retried");
    }
}
