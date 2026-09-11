using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JobAggregator.DataAccess.Entities;

namespace JobAggregator.DataAccess.Repositories
{
    public interface IJobRepository
    {
        Task<JobPost?> GetByIdAsync(Guid id);
        Task<IEnumerable<JobPost>> GetJobsByCriteriaAsync(string keyword, string location, string jobType, int limit = 20, DateTime? freshSince = null, string[]? sources = null);
        Task<SearchKeyword?> GetSearchKeywordAsync(string keyword, string location, string jobType);
        Task AddSearchKeywordAsync(SearchKeyword searchKeyword);
        Task AddJobPostAsync(JobPost jobPost);
        Task UpdateSearchKeywordAsync(SearchKeyword searchKeyword);
        Task SaveChangesAsync();
        
        Task<JobSearchRequest?> GetRequestAsync(Guid id);
        Task AddRequestAsync(JobSearchRequest request);
        // WebSocket Connections
        Task AddConnectionAsync(string connectionId);
        Task RemoveConnectionAsync(string connectionId);
        Task<IEnumerable<string>> GetAllConnectionIdsAsync();
    }
}
