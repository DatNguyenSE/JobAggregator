using System;
using System.Collections.Generic;

namespace JobAggregator.DataAccess.Entities
{
    public class SearchKeyword
    {
        public Guid Id { get; set; }
        public string Keyword { get; set; } = null!;
        public string Location { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
        public DateTime LastScrapedAt { get; set; }
        public int TotalJobsFound { get; set; } 
        public ICollection<JobPost> JobPosts { get; set; } = new List<JobPost>();
    }
}
