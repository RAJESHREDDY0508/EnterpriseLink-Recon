namespace EnterpriseLink.Worker;

/// <summary>
/// Background worker that processes transactions from the message queue.
/// Phase 7 (Distributed Processing) will wire this to RabbitMQ / MassTransit.
/// Currently runs as a heartbeat to verify the worker host is alive.
/// </summary>
public class TransactionWorker : BackgroundService
{
    private readonly ILogger<TransactionWorker> _logger;

    public TransactionWorker(ILogger<TransactionWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TransactionWorker started — waiting for messages");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Placeholder: Phase 7 will replace this with message consumer
            _logger.LogInformation("TransactionWorker heartbeat at {Time}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("TransactionWorker stopped");
    }
}
