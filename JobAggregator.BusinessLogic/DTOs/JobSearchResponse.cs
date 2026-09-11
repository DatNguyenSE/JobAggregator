namespace JobAggregator.BusinessLogic.DTOs;

public class JobSearchResponse
{
    public Guid? RequestId { get; set; }
    public List<JobPostDto> Jobs { get; set; } = new();
    public int RequestedCount { get; set; }
    public int MissingCount => Math.Max(0, RequestedCount - Jobs.Count);
    public string Status { get; set; } = "complete";
    public bool IsScraping => Status == "pending";
    public string? Message { get; set; }
}
