using EnterpriseLink.Ingestion.Messaging;
using EnterpriseLink.Shared.Contracts.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EnterpriseLink.Ingestion.Tests;

/// <summary>
/// Unit tests for <see cref="MassTransitEventPublisher"/>.
///
/// <para>
/// All tests mock <see cref="IPublishEndpoint"/> — no real RabbitMQ connection is
/// required. Tests verify that the publisher delegates correctly to MassTransit and
/// handles failures without swallowing exceptions.
/// </para>
/// </summary>
public sealed class MassTransitEventPublisherTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MassTransitEventPublisher BuildPublisher(IPublishEndpoint endpoint)
        => new(endpoint, NullLogger<MassTransitEventPublisher>.Instance);

    private static FileUploadedEvent BuildEvent(Guid? uploadId = null, Guid? tenantId = null)
        => new()
        {
            UploadId = uploadId ?? Guid.NewGuid(),
            TenantId = tenantId ?? Guid.NewGuid(),
            StoragePath = "tenant-id/upload-id/data.csv",
            FileName = "data.csv",
            FileSizeBytes = 4096,
            DataRowCount = 100,
            SourceSystem = "Salesforce",
            UploadedAt = DateTimeOffset.UtcNow,
        };

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>PublishAsync delegates to IPublishEndpoint.Publish exactly once.</summary>
    [Fact]
    public async Task PublishAsync_calls_publish_endpoint_once()
    {
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = BuildPublisher(endpointMock.Object);
        var @event = BuildEvent();

        await publisher.PublishAsync(@event);

        endpointMock.Verify(
            e => e.Publish(It.Is<FileUploadedEvent>(ev => ev.UploadId == @event.UploadId),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "the publisher must forward the event to IPublishEndpoint exactly once");
    }

    /// <summary>PublishAsync completes without throwing when the broker call succeeds.</summary>
    [Fact]
    public async Task PublishAsync_succeeds_when_broker_accepts_message()
    {
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = BuildPublisher(endpointMock.Object);

        var act = () => publisher.PublishAsync(BuildEvent());

        await act.Should().NotThrowAsync("a successful broker call must not throw");
    }

    /// <summary>PublishAsync passes the correct event payload to the broker.</summary>
    [Fact]
    public async Task PublishAsync_passes_correct_event_payload()
    {
        FileUploadedEvent? captured = null;
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileUploadedEvent, CancellationToken>((ev, _) => captured = ev)
            .Returns(Task.CompletedTask);

        var publisher = BuildPublisher(endpointMock.Object);
        var uploadId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var @event = BuildEvent(uploadId, tenantId);

        await publisher.PublishAsync(@event);

        captured.Should().NotBeNull();
        captured!.UploadId.Should().Be(uploadId);
        captured.TenantId.Should().Be(tenantId);
        captured.SourceSystem.Should().Be("Salesforce");
        captured.StoragePath.Should().Be("tenant-id/upload-id/data.csv");
    }

    /// <summary>PublishAsync respects the CancellationToken and passes it to the endpoint.</summary>
    [Fact]
    public async Task PublishAsync_passes_cancellation_token_to_endpoint()
    {
        CancellationToken capturedToken = default;
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<FileUploadedEvent, CancellationToken>((_, ct) => capturedToken = ct)
            .Returns(Task.CompletedTask);

        var publisher = BuildPublisher(endpointMock.Object);
        using var cts = new CancellationTokenSource();

        await publisher.PublishAsync(BuildEvent(), cts.Token);

        capturedToken.Should().Be(cts.Token,
            "the cancellation token must be forwarded to the broker call");
    }

    // ── Failure / retry surface ───────────────────────────────────────────────

    /// <summary>
    /// When the broker call throws, PublishAsync re-throws — allowing MassTransit's
    /// bus-level retry policy (configured in <c>MessagingServiceExtensions</c>)
    /// to handle transient failures. The publisher must not swallow exceptions.
    /// </summary>
    [Fact]
    public async Task PublishAsync_rethrows_when_broker_throws()
    {
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Broker unavailable"));

        var publisher = BuildPublisher(endpointMock.Object);

        var act = () => publisher.PublishAsync(BuildEvent());

        await act.Should().ThrowAsync<InvalidOperationException>(
            "exceptions from the broker must propagate so the retry policy can act");
    }

    /// <summary>
    /// A broker that fails on the first call but succeeds on the second simulates
    /// the behaviour after a MassTransit retry. The publisher itself is stateless —
    /// calling PublishAsync a second time with the same event succeeds.
    /// </summary>
    [Fact]
    public async Task PublishAsync_succeeds_on_second_attempt_after_transient_failure()
    {
        var callCount = 0;
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<FileUploadedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("Transient broker error");
                return Task.CompletedTask;
            });

        var publisher = BuildPublisher(endpointMock.Object);
        var @event = BuildEvent();

        // First call fails — simulates what the retry policy will observe.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(@event));

        // Second call succeeds — simulates the retry attempt.
        var act = () => publisher.PublishAsync(@event);
        await act.Should().NotThrowAsync(
            "the publisher is stateless; retrying succeeds when the broker recovers");
    }

    // ── Generic type support ──────────────────────────────────────────────────

    /// <summary>
    /// PublishAsync is generic — verify it works with any event type,
    /// not just FileUploadedEvent.
    /// </summary>
    [Fact]
    public async Task PublishAsync_supports_arbitrary_event_types()
    {
        var endpointMock = new Mock<IPublishEndpoint>();
        endpointMock
            .Setup(e => e.Publish(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = BuildPublisher(endpointMock.Object);

        // A simple anonymous-style test event.
        var act = () => publisher.PublishAsync(new { Id = Guid.NewGuid(), Name = "test" });

        await act.Should().NotThrowAsync(
            "IEventPublisher.PublishAsync is generic and must work for any reference type");
    }
}
