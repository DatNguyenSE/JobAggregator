using JobAggregator.DataAccess.Entities;
using JobAggregator.BusinessLogic.DTOs;
using System;

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
                Title = entity.Title,
                SalaryInfo = entity.SalaryInfo,
                Requirements = entity.Requirements,
                Location = entity.Location,
                EmployerName = entity.EmployerName,
                ContactPhone = entity.ContactPhone,
                SpecificAddress = entity.SpecificAddress,
                WorkingHours = entity.WorkingHours,
                WorkingDays = entity.WorkingDays,
                HolidayBonuses = entity.HolidayBonuses,
                OtherBenefits = entity.OtherBenefits,
                SourceUrl = entity.SourceUrl,
                Platform = entity.Platform,
                ExternalId = entity.ExternalId,
                PostedDate = entity.PostedDate
            };
        }

        public static JobPost ToEntity(this JobPostDto dto, Guid searchKeywordId)
        {
            if (dto == null) return null!;

            return new JobPost
            {
                Id = dto.Id != Guid.Empty ? dto.Id : Guid.NewGuid(),
                SearchKeywordId = searchKeywordId,
                Title = dto.Title,
                SalaryInfo = dto.SalaryInfo,
                Requirements = dto.Requirements,
                Location = dto.Location,
                EmployerName = dto.EmployerName,
                ContactPhone = dto.ContactPhone,
                SpecificAddress = dto.SpecificAddress,
                WorkingHours = dto.WorkingHours,
                WorkingDays = dto.WorkingDays,
                HolidayBonuses = dto.HolidayBonuses,
                OtherBenefits = dto.OtherBenefits,
                SourceUrl = dto.SourceUrl,
                Platform = dto.Platform,
                ExternalId = dto.ExternalId,
                PostedDate = dto.PostedDate,
                CreatedAt = DateTime.UtcNow
            };
        }
    }
}
