namespace JobAggregator.DataAccess.Entities;

public class JobSearchRequest
{
    public Guid Id { get; set; }
    public string CriteriaJson { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
