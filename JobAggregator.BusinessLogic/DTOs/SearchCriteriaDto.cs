using System;

namespace JobAggregator.BusinessLogic.DTOs
{
    public class SearchCriteriaDto
    {
        public string Keyword { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
        public string[] Sources { get; set; } = Array.Empty<string>();
    }
}
