using System;

namespace JobAggregator.BusinessLogic.DTOs
{
    public class SearchCriteriaDto
    {
        public Guid? FacebookGroupId { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? FacebookGroupUrl { get; set; }
        public string? FacebookViewOption { get; set; }
        public int? FacebookSearchYear { get; set; }
        public string? FacebookOnlyPostsNewerThan { get; set; }
        public string Keyword { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
        public string[] Sources { get; set; } = Array.Empty<string>();
        public string Province { get; set; } = "";
        public string District { get; set; } = "";
        public Guid? RequestId { get; set; }
        public string[] ExcludedExternalIds { get; set; } = Array.Empty<string>();
        public int MaxJobs { get; set; } = 5;
    }
}
