using System.Threading.Tasks;
using JobAggregator.BusinessLogic.DTOs;

namespace JobAggregator.BusinessLogic.Services.Scrapers
{
    public interface IJobScraperStrategy
    {
        Task<string> ScrapeJobsAsJsonAsync(SearchCriteriaDto criteria, int maxJobs);
    }
}
