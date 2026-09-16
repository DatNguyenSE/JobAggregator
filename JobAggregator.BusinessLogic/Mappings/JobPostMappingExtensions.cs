using JobAggregator.DataAccess.Entities;
using JobAggregator.BusinessLogic.DTOs;
using System;
using System.Linq;

namespace JobAggregator.BusinessLogic.Mappings
{
    public static class JobPostMappingExtensions
    {
        public static JobPostDto ToDto(this JobPost entity)
        {
            if (entity == null) return null!;

            return new JobPostDto
            {
                Id = entity.Id,
                FacebookGroupId = entity.FacebookGroupId,
                Roles = entity.Roles,
                Title = entity.Title,
                SalaryInfo = entity.SalaryInfo,
                Requirements = entity.Requirements,
                Location = entity.Location,
                EmployerName = entity.EmployerName,
                ContactPhone = entity.ContactPhone,
                Locations = entity.Locations?.Select(l => new JobLocationDto 
                {
                    Province = l.Province,
                    District = l.District,
                    ExactAddress = l.ExactAddress
                }).ToList() ?? new List<JobLocationDto>(),
                WorkingHours = entity.WorkingHours,
                WorkingDays = entity.WorkingDays,
                HolidayBonuses = entity.HolidayBonuses,
                OtherBenefits = entity.OtherBenefits,
                SourceUrl = entity.SourceUrl,
                Platform = entity.Platform,
                ExternalId = entity.ExternalId,
                PostedDate = entity.PostedDate.ToString("yyyy-MM-dd"),
                JobInfo = entity.JobInfo ?? "",
                Gender = entity.Gender ?? "",
                Vacancies = entity.Vacancies,
                WorkingFormat = entity.WorkingFormat ?? "",
                AgeRequirement = entity.AgeRequirement ?? "",
                Experience = entity.Experience ?? ""
            };
        }

        // Chuyển DTO thành JobPost và các JobLocation tương ứng; chỉ tạo đối tượng, chưa ghi database.
        public static JobPost ToEntity(this JobPostDto dto, Guid searchKeywordId)
        {
            if (dto == null) return null!;

            DateTime parsedDate = DateTime.UtcNow;
            if (DateTime.TryParse(dto.PostedDate, out var tempDate))
            {
                parsedDate = DateTime.SpecifyKind(tempDate, DateTimeKind.Utc);
            }

            return new JobPost
            {
                Id = dto.Id != Guid.Empty ? dto.Id : Guid.NewGuid(),
                SearchKeywordId = searchKeywordId,
                FacebookGroupId = dto.FacebookGroupId,
                Roles = dto.Roles ?? Array.Empty<string>(),
                Title = string.IsNullOrWhiteSpace(dto.Title) ? "Không có tiêu đề" : dto.Title,
                SalaryInfo = dto.SalaryInfo,
                Requirements = dto.Requirements,
                Location = dto.Location,
                EmployerName = dto.EmployerName,
                ContactPhone = dto.ContactPhone,
                Locations = (dto.Locations?.Count > 0 ? dto.Locations :
                    string.IsNullOrWhiteSpace(dto.SpecificAddress) ? new List<JobLocationDto>() :
                    new List<JobLocationDto> { new() { ExactAddress = dto.SpecificAddress } }).Select(l => new JobLocation 
                {
                    Province = l.Province,
                    District = l.District,
                    ExactAddress = l.ExactAddress
                }).ToList() ?? new List<JobLocation>(),
                WorkingHours = dto.WorkingHours,
                WorkingDays = dto.WorkingDays,
                HolidayBonuses = dto.HolidayBonuses,
                OtherBenefits = dto.OtherBenefits,
                SourceUrl = string.IsNullOrWhiteSpace(dto.SourceUrl) ? "#" : dto.SourceUrl,
                Platform = string.IsNullOrWhiteSpace(dto.Platform) ? "Unknown" : dto.Platform,
                ExternalId = string.IsNullOrWhiteSpace(dto.ExternalId) ? Guid.NewGuid().ToString() : dto.ExternalId,
                PostedDate = parsedDate,
                CreatedAt = DateTime.UtcNow,
                LastScrapedAt = DateTime.UtcNow,
                JobInfo = dto.JobInfo,
                Gender = dto.Gender,
                Vacancies = dto.Vacancies,
                WorkingFormat = dto.WorkingFormat,
                AgeRequirement = dto.AgeRequirement,
                Experience = dto.Experience
            };
        }
    }
}
