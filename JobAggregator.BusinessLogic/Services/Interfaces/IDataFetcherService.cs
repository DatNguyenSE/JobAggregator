using System.Threading.Tasks;

namespace JobAggregator.BusinessLogic.Services.Interfaces
{
    public interface IDataFetcherService
    {
        // Hàm bắt đầu tiến trình cào dữ liệu HTML và xử lý
        Task ProcessScrapingRequestAsync(string rawMessageBody);
    }
}
