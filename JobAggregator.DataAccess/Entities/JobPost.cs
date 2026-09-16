using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace JobAggregator.DataAccess.Entities
{
    public class JobPost
    {
        public Guid Id { get; set; }
        public Guid? FacebookGroupId { get; set; }
        public FacebookGroup? FacebookGroup { get; set; }
        public string? ContentHash { get; set; }
        public string[] Roles { get; set; } = Array.Empty<string>();
        public Guid SearchKeywordId { get; set; }
        public SearchKeyword SearchKeyword { get; set; } = null!;

        // Thông tin cốt lõi
        public string Title { get; set; } = null!;
        public string? SalaryInfo { get; set; } 
        public string? Requirements { get; set; }
        public string? Location { get; set; }
        
        public virtual ICollection<JobLocation> Locations { get; set; } = new List<JobLocation>();
        
        public string? EmployerName { get; set; } 
        public string? ContactPhone { get; set; }

        // Thông tin thời gian làm việc
        public string? WorkingHours { get; set; } 
        public string? WorkingDays { get; set; }  

        // Đãi ngộ & Phúc lợi
        public string? HolidayBonuses { get; set; } 
        public string? OtherBenefits { get; set; }  

        // Meta data (Chống trùng lặp)
        public string SourceUrl { get; set; } = null!; 
        public string Platform { get; set; } = null!; 
        public string ExternalId { get; set; } = null!; 
        
        public DateTime PostedDate { get; set; } 
        public DateTime CreatedAt { get; set; }
        public DateTime? LastScrapedAt { get; set; } 

        public string? JobInfo { get; set; }
        public string? Gender { get; set; }
        public int? Vacancies { get; set; }
        public string? WorkingFormat { get; set; }
        public string? AgeRequirement { get; set; }
        public string? Experience { get; set; }
    }
}
