namespace JobAggregator.DataAccess.Entities;

public class FacebookGroup
{
    public Guid Id { get; set; }
    public string? ExternalGroupId { get; set; }
    public string Name { get; set; } = "";
    public string CanonicalUrl { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSuccessfulScrapedAt { get; set; }
}
