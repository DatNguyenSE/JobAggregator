using System;

namespace JobAggregator.BusinessLogic.DTOs
{
    public class SearchCriteriaDto
    {
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
