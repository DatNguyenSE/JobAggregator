using System;

namespace JobAggregator.DataAccess.Entities
{
    public class JobPost
    {
        public Guid Id { get; set; }
        public Guid SearchKeywordId { get; set; }
        public SearchKeyword SearchKeyword { get; set; } = null!;

        // Thông tin cốt lõi
        public string Title { get; set; } = null!;
        public string? SalaryInfo { get; set; } 
        public string? Requirements { get; set; }
        public string? Location { get; set; }
        
        // Thông tin nhà tuyển dụng & liên hệ
        public string? EmployerName { get; set; }
        public string? ContactPhone { get; set; }
        public string? SpecificAddress { get; set; }

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
    }
}
