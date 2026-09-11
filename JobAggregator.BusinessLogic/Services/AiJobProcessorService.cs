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
using JobAggregator.DataAccess.Entities;

namespace JobAggregator.BusinessLogic.Services
{
    public interface IAiJobProcessorService
    {
        Task ProcessJobDataAsync(string rawJsonData, SearchCriteriaDto criteria, DateTime? scrapedAt = null);
    }

    public class AiJobProcessorService : IAiJobProcessorService
    {
        private readonly IJobRepository _jobRepository;
        private readonly HttpClient _httpClient;
        private readonly AmazonApiGatewayManagementApiClient _apiGatewayClient;
        private readonly Amazon.BedrockRuntime.IAmazonBedrockRuntime _bedrockClient;

        public AiJobProcessorService(IJobRepository jobRepository)
        {
            _jobRepository = jobRepository;
            _httpClient = new HttpClient();
            
            var endpoint = Environment.GetEnvironmentVariable("WEBSOCKET_ENDPOINT") ?? "";
            var config = new AmazonApiGatewayManagementApiConfig { ServiceURL = endpoint };
            _apiGatewayClient = new AmazonApiGatewayManagementApiClient(config);
            _bedrockClient = new Amazon.BedrockRuntime.AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.APSoutheast1);
        }

        // LUỒNG 3: Nhận kết quả của một nguồn, chuẩn hóa thành List<JobPostDto> rồi lưu từng bài.
        public async Task ProcessJobDataAsync(string rawJsonData, SearchCriteriaDto criteria, DateTime? scrapedAt = null)
        {
            Console.WriteLine($"[AiJobProcessor] Nhận được dữ liệu thô dài {rawJsonData?.Length} ký tự.");
            string processedJson = rawJsonData;
            
            // Nhận diện nhanh bằng hình dạng chuỗi và tên field, chưa validate đầy đủ schema/cú pháp.
            // JSON List<JobPostDto> từ Vieclam24h (kể cả []) đi thẳng, không gọi Bedrock.
            // Nếu chuỗi bị nhận diện nhầm nhưng JSON hỏng, Deserialize sẽ lỗi và SQS có thể retry.
            bool isJsonArray = rawJsonData.TrimStart().StartsWith("[");
            bool isValidJobArray = rawJsonData.Trim() == "[]" || isJsonArray && rawJsonData.Contains("\"Title\"") && (rawJsonData.Contains("\"Platform\"") || rawJsonData.Contains("\"SalaryInfo\""));
            

                // Dữ liệu chưa theo format chung: gọi Bedrock để trích xuất thành JSON List<JobPostDto>.
            if (!isValidJobArray)
            {
                int maxJobs = criteria.MaxJobs > 0 ? criteria.MaxJobs : 5;
                Console.WriteLine($"[Luồng 3] Dữ liệu thô không đúng chuẩn Job. Gọi AWS Bedrock (MaxJobs={maxJobs})...");
                processedJson = await CallBedrockTextOnlyAsync(rawJsonData, maxJobs);
            }

            // Clean up Bedrock response (extract only the JSON array)
            int startIndex = processedJson.IndexOf('[');
            int endIndex = processedJson.LastIndexOf(']');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                processedJson = processedJson.Substring(startIndex, endIndex - startIndex + 1);
            }
            else
            {
                processedJson = "[]"; // Fallback nếu không có mảng JSON
            }

            // JSON chuẩn từ Vieclam24h bỏ qua Bedrock; tại đây đọc lại danh sách DTO để lưu từng bài.
            var jobDtos = JsonSerializer.Deserialize<List<JobPostDto>>(processedJson, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (jobDtos != null)
            {
                // SearchKeyword lưu dấu vết tiêu chí sinh ra lô job; lọc địa điểm thực tế dựa trên JobLocations.
                // Nếu chưa có thì tạo mới; nếu có thì cập nhật metadata sau khi lưu các JobPost.
                var keywordEntity = await _jobRepository.GetSearchKeywordAsync(criteria.Keyword, criteria.Location, criteria.JobType);
                // Nếu chưa có SearchKeyword, tạo mới để liên kết với các JobPost sắp lưu.
                if (keywordEntity == null)
                {
                    keywordEntity = new SearchKeyword
                    {
                        Id = Guid.NewGuid(),
                        Keyword = criteria.Keyword,
                        Location = criteria.Location ?? "",
                        JobType = criteria.JobType ?? "", 
                        LastScrapedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                    };
                    await _jobRepository.AddSearchKeywordAsync(keywordEntity);
                    await _jobRepository.SaveChangesAsync();
                }

                if (jobDtos.Count > 0)
                {
                    // BƯỚC 6: Gộp trùng theo Platform + ExternalId, chuyển mỗi DTO thành entity rồi gọi repository lưu DB.
                    foreach (var jobDto in jobDtos.GroupBy(j => (j.Platform, j.ExternalId)).Select(g => g.Last()))
                    {
                        var jobEntity = jobDto.ToEntity(keywordEntity.Id);
                        jobEntity.LastScrapedAt = scrapedAt ?? DateTime.UtcNow;
                        await _jobRepository.AddJobPostAsync(jobEntity);
                    }
                    
                    keywordEntity.LastScrapedAt = DateTime.UtcNow;
                    keywordEntity.TotalJobsFound = (await _jobRepository.GetJobsByCriteriaAsync(criteria.Keyword,
                        criteria.Location, criteria.JobType, int.MaxValue)).Count();
                    await _jobRepository.UpdateSearchKeywordAsync(keywordEntity);
                    
                    await _jobRepository.SaveChangesAsync();
                    Console.WriteLine($"[DB Saved] Đã lưu {jobDtos.Count} jobs cho tiêu chí '{criteria.Keyword}' vào DB.");
                }
                else
                {
                    Console.WriteLine($"[Scraping] Không có job nào được tìm thấy cho '{criteria.Keyword}'.");
                }
                
                var connectionIds = await _jobRepository.GetAllConnectionIdsAsync();
                
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var broadcastMessage = JsonSerializer.Serialize(new
                {
                    Type = "SEARCH_UPDATED",
                    RequestId = criteria.RequestId
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

        // Nhánh Bedrock trích xuất dữ liệu chưa theo cấu trúc công việc chuẩn; Vieclam24h bình thường không gọi hàm này.
        private async Task<string> CallBedrockTextOnlyAsync(string rawData, int maxJobs)
        {
            Console.WriteLine("[Bedrock] Bắt đầu phân tích văn bản...");
            
            try 
            {
                var systemPrompt = $"You are an AI that extracts job information from text. Extract a MAXIMUM of {maxJobs} jobs. Return ONLY a JSON array with objects matching exactly this schema: {{ \"Title\": string, \"SalaryInfo\": string, \"Requirements\": string, \"OtherBenefits\": string, \"Location\": string, \"EmployerName\": string, \"ContactPhone\": string, \"SpecificAddress\": string, \"Platform\": string, \"ExternalId\": string, \"SourceUrl\": string, \"PostedDate\": string }}. IMPORTANT INSTRUCTIONS: 1. For 'SpecificAddress', scan the ENTIRE text and extract ALL DETAILED street addresses available (if there are multiple branches, list them ALL separated by semicolons ';'). DO NOT just extract the city if full street addresses exist. 2. For 'PostedDate', extract the exact posting date and format it STRICTLY as ISO 8601 'YYYY-MM-DD' (e.g. '2026-08-28'). 3. Combine 'Mô tả công việc' and 'Yêu cầu' into 'Requirements'. COPY THE EXACT BULLET POINTS. 4. Extract 'Quyền lợi' into 'OtherBenefits'. COPY EXACT BULLET POINTS. 5. IF NO JOBS FOUND, RETURN []. DO NOT HALLUCINATE.";
                
                System.Text.StringBuilder combinedText = new System.Text.StringBuilder();
                try
                {
                    using var apifyDoc = JsonDocument.Parse(rawData);
                    if (apifyDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in apifyDoc.RootElement.EnumerateArray())
                        {
                            if (element.TryGetProperty("text", out var textProp))
                            {
                                string url = element.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                                
                                // Bỏ qua các trang danh sách tìm kiếm để tránh nhiễu AI
                                if (url.Contains("tim-kiem") || url.Contains("tags")) continue;

                                string pageText = textProp.GetString() ?? "";
                                // Giới hạn mỗi trang chi tiết khoảng 15,000 ký tự (loại bỏ text rác ở cuối trang)
                                if (pageText.Length > 15000) pageText = pageText.Substring(0, 15000);
                                
                                combinedText.AppendLine($"--- SOURCE URL: {url} ---");
                                combinedText.AppendLine(pageText);
                                combinedText.AppendLine();
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback nếu không phải mảng JSON hợp lệ
                }

                string text = combinedText.Length > 0 ? combinedText.ToString() : rawData;
                
                // Truncate combined text if it's still too large for Claude 3 Haiku context limit
                if (text.Length > 150000) text = text.Substring(0, 150000);


                var requestBody = new
                {
                    anthropic_version = "bedrock-2023-05-31",
                    max_tokens = 2000,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = "Extract jobs from the following text:\n\n" + text }
                    },
                    temperature = 0.1
                };

                var request = new Amazon.BedrockRuntime.Model.InvokeModelRequest
                {
                    ModelId = "anthropic.claude-3-haiku-20240307-v1:0",
                    ContentType = "application/json",
                    Accept = "application/json",
                    Body = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestBody)))
                };

                var response = await _bedrockClient.InvokeModelAsync(request);
                using var reader = new System.IO.StreamReader(response.Body);
                var responseBody = await reader.ReadToEndAsync();

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                {
                    string extractedText = contentArray[0].GetProperty("text").GetString() ?? "[]";
                    Console.WriteLine($"[Bedrock Raw Output] {extractedText}");
                    Console.WriteLine("[Bedrock] Đã phân tích xong!");
                    return extractedText;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bedrock Lỗi] {ex.Message}");
            }
            
            return "[]";
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
