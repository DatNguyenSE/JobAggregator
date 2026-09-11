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

        public List<JobLocationDto> Locations { get; set; } = new List<JobLocationDto>();

        // Thông tin thời gian làm việc
        public string? WorkingHours { get; set; }  
        public string? WorkingDays { get; set; }  
        public string? HolidayBonuses { get; set; } 
        public string OtherBenefits { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty; 
        public string Platform { get; set; } = string.Empty; 
        public string ExternalId { get; set; } = string.Empty; 
        
        public string JobInfo { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public int? Vacancies { get; set; }
        public string WorkingFormat { get; set; } = string.Empty;
        public string AgeRequirement { get; set; } = string.Empty;
        public string Experience { get; set; } = string.Empty;

        public string PostedDate { get; set; } = string.Empty;
    }
}
