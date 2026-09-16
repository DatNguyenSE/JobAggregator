namespace JobAggregator.BusinessLogic.DTOs;

public sealed class FacebookPostBatch
{
    public string Kind { get; set; } = "facebook-group-posts";
    public List<FacebookRawPost> Posts { get; set; } = new();
}

public sealed class FacebookRawPost
{
    public string ExternalId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string Text { get; set; } = "";
    public string PostedDate { get; set; } = "";
    public List<string> ImageUrls { get; set; } = new();
}
