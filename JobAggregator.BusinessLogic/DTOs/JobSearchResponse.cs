namespace JobAggregator.BusinessLogic.DTOs;

public class JobSearchResponse
{
    public int Page { get; set; } = 1;
    public bool HasMore { get; set; }
    public int FetchedCount { get; set; }
    public int InsertedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int RejectedCount { get; set; }
    public int FailedCount { get; set; }
    public Guid? FacebookGroupId { get; set; }
    public Guid? RequestId { get; set; }
    public List<JobPostDto> Jobs { get; set; } = new();
    public int RequestedCount { get; set; }
    public int MissingCount => Math.Max(0, RequestedCount - Jobs.Count);
    public string Status { get; set; } = "complete";
    public bool IsScraping => Status is "pending" or "scraping" or "processing";
    public string? Message { get; set; }
}
