using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;
using JobAggregator.DataAccess.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace JobAggregator.BusinessLogic.Services
{
    public interface IAiJobProcessorService
    {
        Task ProcessTinyFishDataAsync(string rawJsonData, SearchCriteriaDto criteria);
    }

    public class AiJobProcessorService : IAiJobProcessorService
    {
        private readonly IJobRepository _jobRepository;
        private readonly HttpClient _httpClient;
        private readonly AmazonApiGatewayManagementApiClient _apiGatewayClient;

        // Trong thực tế, bạn sẽ tiêm (Inject) S3Client và BedrockRuntimeClient ở đây.
        // public AiJobProcessorService(IJobRepository jobRepository, IAmazonS3 s3Client, IAmazonBedrockRuntime bedrockClient)

        public AiJobProcessorService(IJobRepository jobRepository)
        {
            _jobRepository = jobRepository;
            _httpClient = new HttpClient();
            
            // Lấy WebSocket URL từ biến môi trường
            var endpoint = Environment.GetEnvironmentVariable("WEBSOCKET_ENDPOINT") ?? "";
            var config = new AmazonApiGatewayManagementApiConfig { ServiceURL = endpoint };
            _apiGatewayClient = new AmazonApiGatewayManagementApiClient(config);
        }

        public async Task ProcessTinyFishDataAsync(string rawJsonData, SearchCriteriaDto criteria)
        {
            var jobDtos = JsonSerializer.Deserialize<JobPostDto[]>(rawJsonData, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (jobDtos != null && jobDtos.Length > 0)
            {
                var keywordEntity = await _jobRepository.GetSearchKeywordAsync(criteria.Keyword, criteria.Location, criteria.JobType);
                if (keywordEntity == null) return;

                foreach (var jobDto in jobDtos)
                {
                    var jobEntity = jobDto.ToEntity(keywordEntity.Id);
                    await _jobRepository.AddJobPostAsync(jobEntity);
                }
                
                keywordEntity.LastScrapedAt = DateTime.UtcNow;
                keywordEntity.TotalJobsFound += jobDtos.Length;
                await _jobRepository.UpdateSearchKeywordAsync(keywordEntity);
                
                await _jobRepository.SaveChangesAsync();
                Console.WriteLine($"[DB Saved] Đã lưu {jobDtos.Length} jobs cho tiêu chí '{criteria.Keyword}' vào DB.");
                
                var connectionIds = await _jobRepository.GetAllConnectionIdsAsync();
                
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var broadcastMessage = JsonSerializer.Serialize(new
                {
                    Type = "NEW_JOBS",
                    Criteria = criteria,
                    Jobs = jobDtos
                }, options);

                var messageBytes = System.Text.Encoding.UTF8.GetBytes(broadcastMessage);

                foreach (var connId in connectionIds)
                {
                    try
                    {
                        var request = new PostToConnectionRequest
                        {
                            ConnectionId = connId,
                            Data = new System.IO.MemoryStream(messageBytes)
                        };
                        await _apiGatewayClient.PostToConnectionAsync(request);
                    }
                    catch (Amazon.ApiGatewayManagementApi.Model.GoneException)
                    {
                        // User đã tắt trình duyệt nhưng chưa kịp gọi disconnect, xóa rác
                        await _jobRepository.RemoveConnectionAsync(connId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Broadcast Error] Lỗi khi gửi cho {connId}: {ex.Message}");
                    }
                }
            }
        }

        // ------------------ CÁC HÀM TIỆN ÍCH NỘI BỘ ------------------

        private async Task<string> CallBedrockTextOnlyAsync(string text)
        {
            // TODO: Gọi Amazon Bedrock API bằng thư viện AWSSDK.BedrockRuntime
            // System Prompt: "You are an AI that extracts job information to strictly format JSON: { Title, SalaryInfo, ... }"
            
            // Giả lập kết quả trả về từ Claude 3 Haiku:
            return "{\"Title\":\"Lập trình viên .NET\",\"SalaryInfo\":\"2000$\",\"Platform\":\"Facebook\",\"ExternalId\":\"FB_123\"}"; 
        }

        private async Task<string> CallBedrockMultimodalAsync(string text, string base64Image)
        {
            // TODO: Tạo JSON Payload định dạng Anthropic Claude 3 (Multimodal) truyền cả text và base64 image array.
            return "{\"Title\":\"Lập trình viên .NET\",\"SalaryInfo\":\"2000$\",\"Platform\":\"Facebook\",\"ExternalId\":\"FB_123\"}";
        }

        private async Task<string> DownloadAndCompressImageAsync(string imageUrl)
        {
            // Tải ảnh từ URL
            var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);

            // Xử lý bằng SixLabors.ImageSharp
            using var image = Image.Load(imageBytes);
            
            // Giảm kích thước ảnh xuống tối đa 1024x1024 để hạn chế tốn Token của Claude Vision
            int maxWidth = 1024;
            int maxHeight = 1024;
            
            if (image.Width > maxWidth || image.Height > maxHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(maxWidth, maxHeight)
                }));
            }

            // Nén ảnh sang JPEG chất lượng 80%
            using var ms = new MemoryStream();
            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = 80 });
            
            // Claude 3 API nhận dữ liệu ảnh dưới dạng chuỗi Base64
            return Convert.ToBase64String(ms.ToArray());
        }
    }
}
