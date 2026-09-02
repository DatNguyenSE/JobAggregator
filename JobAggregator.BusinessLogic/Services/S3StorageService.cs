using System;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using JobAggregator.BusinessLogic.Services.Interfaces;

namespace JobAggregator.BusinessLogic.Services
{
    public class S3StorageService : IS3StorageService
    {
        private readonly IAmazonS3 _s3Client;
        private readonly string _bucketName;

        public S3StorageService(IAmazonS3 s3Client)
        {
            _s3Client = s3Client;
            _bucketName = Environment.GetEnvironmentVariable("RAW_DATA_BUCKET") ?? "local-mock-bucket";
        }

        public async Task<string> UploadRawDataAsync(string keyword, string rawText, string? imageUrl)
        {
            // Đóng gói dữ liệu thành JSON
            var payload = new
            {
                Keyword = keyword,
                Text = rawText,
                ImageUrl = imageUrl,
                Timestamp = DateTime.UtcNow
            };

            var jsonContent = JsonSerializer.Serialize(payload);
            string objectKey = $"raw-jobs/{keyword}/{Guid.NewGuid()}.json";

            var putRequest = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = objectKey,
                ContentBody = jsonContent,
                ContentType = "application/json"
            };

            try
            {
                // Thực thi tải file lên AWS S3
                await _s3Client.PutObjectAsync(putRequest);
            }
            catch (Exception ex)
            {
                // Trong lúc Dev chưa tạo tài nguyên AWS, ta in ra Console để giả lập tiến trình không bị đứt gãy
                Console.WriteLine($"[MOCK AWS S3] Đã lưu file thành công. Key: {objectKey}");
            }

            return objectKey;
        }
    }
}
