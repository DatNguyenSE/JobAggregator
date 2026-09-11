using JobAggregator.BusinessLogic.DTOs;
namespace JobAggregator.BusinessLogic.Services.Interfaces;
public interface IJobSearchService
{
    Task<JobSearchResponse> SearchJobsAsync(SearchCriteriaDto criteria);
    Task<JobSearchResponse?> GetStatusAsync(Guid requestId);
    Task FinishAsync(Guid? requestId, string? error = null);
}
