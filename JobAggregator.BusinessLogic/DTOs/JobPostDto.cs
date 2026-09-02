using System;

namespace JobAggregator.BusinessLogic.DTOs
{
    public class JobPostDto
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? SalaryInfo { get; set; } 
        public string? Requirements { get; set; }
        public string? Location { get; set; }
        public string? EmployerName { get; set; }
        public string? ContactPhone { get; set; }
        public string? SpecificAddress { get; set; }
        public string? WorkingHours { get; set; } 
        public string? WorkingDays { get; set; }  
        public string? HolidayBonuses { get; set; } 
        public string? OtherBenefits { get; set; }  
        public string SourceUrl { get; set; } = string.Empty; 
        public string Platform { get; set; } = string.Empty; 
        public string ExternalId { get; set; } = string.Empty; 
        public DateTime PostedDate { get; set; } 
    }
}
