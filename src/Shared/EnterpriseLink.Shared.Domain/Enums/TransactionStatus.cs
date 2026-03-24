namespace EnterpriseLink.Shared.Domain.Enums;

/// <summary>
/// Lifecycle states of a transaction as it moves through the reconciliation pipeline.
/// </summary>
public enum TransactionStatus
{
    /// <summary>Received and queued, awaiting processing.</summary>
    Pending = 1,

    /// <summary>Currently being processed by the worker.</summary>
    Processing = 2,

    /// <summary>Successfully reconciled.</summary>
    Completed = 3,

    /// <summary>Processing failed — see ValidationErrors for detail.</summary>
    Failed = 4,

    /// <summary>Flagged for manual review by an Auditor or Operator.</summary>
    UnderReview = 5,

    /// <summary>Rejected after review — will not be reprocessed.</summary>
    Rejected = 6
}
