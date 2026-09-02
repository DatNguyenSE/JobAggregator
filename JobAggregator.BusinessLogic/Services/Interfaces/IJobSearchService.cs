using System.Collections.Generic;
using System.Threading.Tasks;
using JobAggregator.BusinessLogic.DTOs;

namespace JobAggregator.BusinessLogic.Services.Interfaces
{
    public interface IJobSearchService
    {
        // Trả về tuple: (IsFound: có dữ liệu hay không, Jobs: danh sách công việc)
        Task<(bool IsFound, IEnumerable<JobPostDto> Jobs)> SearchJobsAsync(SearchCriteriaDto criteria);
    }
}
