namespace JobAggregator.DataAccess.Entities;

public class JobSearchRequest
{
    public Guid Id { get; set; }
    public string Platform { get; set; } = "";
    public Guid? FacebookGroupId { get; set; }
    public FacebookGroup? FacebookGroup { get; set; }
    public string? OwnerId { get; set; }
    public string ScopeKey { get; set; } = "";
    public string? IdempotencyKey { get; set; }
    public string? RawDataKey { get; set; }
    public string? ApifyRunId { get; set; }
    public DateTime? StartedAt { get; set; }
    public int RequestedLimit { get; set; }
    public int FetchedCount { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int RejectedCount { get; set; }
    public int FailedCount { get; set; }
    // Per-post receipts make processing retries idempotent, including rejected posts.
    public string ProcessingResultsJson { get; set; } = "{}";
    public string CriteriaJson { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
