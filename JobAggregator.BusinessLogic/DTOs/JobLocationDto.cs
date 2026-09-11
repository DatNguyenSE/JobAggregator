using System;

namespace JobAggregator.BusinessLogic.DTOs
{
    public class JobLocationDto
    {
        public string? Province { get; set; }
        public string? District { get; set; }
        public string? ExactAddress { get; set; }
    }
}
