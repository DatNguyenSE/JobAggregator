using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JobAggregator.DataAccess.Entities;

namespace JobAggregator.DataAccess.Repositories
{
    public interface IJobRepository
    {
        Task<JobPost?> GetByIdAsync(Guid id);
        Task<IEnumerable<JobPost>> GetJobsByCriteriaAsync(string keyword, string location, string jobType);
        Task<SearchKeyword?> GetSearchKeywordAsync(string keyword, string location, string jobType);
        Task AddSearchKeywordAsync(SearchKeyword searchKeyword);
        Task AddJobPostAsync(JobPost jobPost);
        Task UpdateSearchKeywordAsync(SearchKeyword searchKeyword);
        Task SaveChangesAsync();
        
        // WebSocket Connections
        Task AddConnectionAsync(string connectionId);
        Task RemoveConnectionAsync(string connectionId);
        Task<IEnumerable<string>> GetAllConnectionIdsAsync();
    }
}
