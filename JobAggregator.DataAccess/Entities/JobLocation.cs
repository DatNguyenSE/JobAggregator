using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace JobAggregator.DataAccess.Entities
{
    public class JobLocation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        
        [ForeignKey("JobPost")]
        public Guid JobPostId { get; set; }
        
        [MaxLength(100)]
        public string? Province { get; set; }
        
        [MaxLength(100)]
        public string? District { get; set; }
        
        public string? ExactAddress { get; set; }
        
        public virtual JobPost? JobPost { get; set; }
    }
}
