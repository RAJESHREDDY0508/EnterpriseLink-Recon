namespace EnterpriseLink.Worker.Idempotency;

/// <summary>
/// Guards against duplicate processing of the same file upload.
///
/// <para><b>Acceptance criterion: Duplicate processing avoided</b></para>
/// <c>FileUploadedEvent.UploadId</c> is the idempotency key. Before processing
/// begins, the consumer calls <see cref="TryBeginAsync"/> which atomically inserts a
/// <c>ProcessedUpload</c> row with the UploadId as the primary key. The database-level
/// primary key constraint means only one worker instance can succeed; concurrent
/// instances receive a PK violation and return <c>false</c>, skipping the upload.
///
/// <para><b>Calling protocol</b></para>
/// <code>
/// bool claimed = await guard.TryBeginAsync(uploadId, tenantId, sourceSystem, ct);
/// if (!claimed) return;   // already processed — MassTransit acks the message
///
/// try
/// {
///     int rows = await batchInserter.InsertAsync(...);
///     await guard.CompleteAsync(uploadId, rows, ct);
/// }
/// catch
/// {
///     await guard.FailAsync(uploadId, CancellationToken.None);
///     throw;  // re-throw so MassTransit applies retry / dead-letter policy
/// }
/// </code>
///
/// <para><b>Retry after failure</b>
/// When a consumer crashes after <see cref="TryBeginAsync"/> but before
/// <see cref="CompleteAsync"/>, the <c>ProcessedUpload</c> row stays in
/// <c>Processing</c> or transitions to <c>Failed</c> (depending on whether
/// <see cref="FailAsync"/> ran). On the next MassTransit retry delivery,
/// <see cref="TryBeginAsync"/> detects the non-<c>Completed</c> record and allows
/// the retry to proceed, resetting the status to <c>Processing</c>.
/// </para>
/// </summary>
public interface IUploadIdempotencyGuard
{
    /// <summary>
    /// Attempts to atomically claim the upload for processing.
    /// </summary>
    /// <param name="uploadId">Idempotency key — <c>FileUploadedEvent.UploadId</c>.</param>
    /// <param name="tenantId">Tenant identifier stored for audit.</param>
    /// <param name="sourceSystem">Source system stored for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <c>true</c> if the upload was successfully claimed and the caller must process it.
    /// <c>false</c> if the upload is already <c>Completed</c> or was concurrently claimed
    /// by another worker instance — the caller must skip processing.
    /// </returns>
    Task<bool> TryBeginAsync(
        Guid uploadId,
        Guid tenantId,
        string sourceSystem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the upload as successfully completed and records the final row count.
    /// </summary>
    /// <param name="uploadId">Idempotency key.</param>
    /// <param name="rowsInserted">Total rows committed across all batches.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CompleteAsync(
        Guid uploadId,
        int rowsInserted,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the upload as failed.
    /// The <c>ProcessedUpload</c> record is retained for diagnostics. A subsequent
    /// MassTransit retry will reset the status back to <c>Processing</c> and re-attempt.
    /// </summary>
    /// <param name="uploadId">Idempotency key.</param>
    /// <param name="cancellationToken">
    /// Should be <see cref="CancellationToken.None"/> in catch blocks — the consume
    /// context token may already be cancelled.
    /// </param>
    Task FailAsync(
        Guid uploadId,
        CancellationToken cancellationToken = default);
}
