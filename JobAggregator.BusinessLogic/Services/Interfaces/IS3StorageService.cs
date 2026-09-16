using System.Threading.Tasks;

namespace JobAggregator.BusinessLogic.Services.Interfaces
{
    public interface IS3StorageService
    {
        // Hàm lưu dữ liệu thô lên Amazon S3 và trả về Object Key (địa chỉ file)
        Task<string> UploadJsonAsync(string json);
        Task<string> DownloadJsonAsync(string key);
        Task<string> UploadRawDataAsync(string keyword, string rawText, string? imageUrl);
    }
}
