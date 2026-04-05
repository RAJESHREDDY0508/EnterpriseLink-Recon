using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Worker.Consumers;
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
/// Acceptance criteria covered:
/// </para>
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

    public FileUploadedEventConsumerTests()
    {
        var loggerMock = new Mock<ILogger<FileUploadedEventConsumer>>();
        loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

        _provider = new ServiceCollection()
            .AddSingleton(loggerMock.Object)
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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static FileUploadedEvent ValidEvent(Guid? uploadId = null, Guid? tenantId = null) =>
        new()
        {
            UploadId = uploadId ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            StoragePath = "tenant-1/upload-abc/data.csv",
            FileName = "data.csv",
            FileSizeBytes = 1024,
            DataRowCount = 50,
            SourceSystem = "SalesForce",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    // ── Subscribes to queue ───────────────────────────────────────────────────

    [Fact]
    public async Task Consumer_is_registered_and_receives_published_event()
    {
        // Arrange
        var evt = ValidEvent();

        // Act
        await _harness.Bus.Publish(evt);

        // Assert — harness received the message
        (await _harness.Consumed.Any<FileUploadedEvent>())
            .Should().BeTrue("the consumer must subscribe to FileUploadedEvent");
    }

    [Fact]
    public async Task Consumer_harness_consumed_message_with_correct_UploadId()
    {
        // Arrange
        var evt = ValidEvent();

        // Act
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        // Assert — the specific consumer saw the message
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
        // Act
        await _harness.Bus.Publish(ValidEvent());
        await _harness.Consumed.Any<FileUploadedEvent>();

        // Assert — no fault published
        (await _harness.Published.Any<Fault<FileUploadedEvent>>())
            .Should().BeFalse("a valid event must not produce a fault");
    }

    [Fact]
    public async Task Consume_valid_event_succeeds_for_multiple_tenants()
    {
        // Arrange
        var events = Enumerable.Range(0, 3)
            .Select(_ => ValidEvent())
            .ToList();

        // Act
        foreach (var evt in events)
            await _harness.Bus.Publish(evt);

        await Task.Delay(500); // allow harness to drain

        // Assert
        var consumerHarness = _harness.GetConsumerHarness<FileUploadedEventConsumer>();
        var consumed = consumerHarness.Consumed.Select<FileUploadedEvent>().ToList();
        consumed.Should().HaveCount(3, "all three events must be handled");
    }

    [Fact]
    public async Task Consume_preserves_all_event_properties()
    {
        // Arrange
        var evt = ValidEvent();

        // Act
        await _harness.Bus.Publish(evt);
        await _harness.Consumed.Any<FileUploadedEvent>();

        // Assert — round-trip the full payload
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
        // Arrange — UploadId = Guid.Empty is malformed
        var evt = ValidEvent(uploadId: Guid.Empty);

        // Act
        await _harness.Bus.Publish(evt);
        await Task.Delay(500);

        // Assert
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
        // Arrange — inject a bad message that will always fail validation
        var malformed = ValidEvent(uploadId: Guid.Empty);

        // Act
        await _harness.Bus.Publish(malformed);
        await Task.Delay(500);

        // Assert — MassTransit publishes Fault<T> after exhausting retries
        var faults = _harness.Published.Select<Fault<FileUploadedEvent>>().ToList();
        faults.Should().NotBeEmpty("MassTransit must publish a Fault<FileUploadedEvent> to the error topic");
    }
}
