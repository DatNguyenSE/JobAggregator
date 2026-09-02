using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using HtmlAgilityPack;
using JobAggregator.BusinessLogic.Services.Interfaces;

namespace JobAggregator.BusinessLogic.Services
{
    public class DataFetcherService : IDataFetcherService
    {
        private readonly IS3StorageService _s3StorageService;
        private readonly IAmazonSQS _sqsClient;
        private readonly HttpClient _httpClient;
        private readonly string _aiQueueUrl;

        public DataFetcherService(IS3StorageService s3StorageService, IAmazonSQS sqsClient)
        {
            _s3StorageService = s3StorageService;
            _sqsClient = sqsClient;
            _httpClient = new HttpClient();
            _aiQueueUrl = Environment.GetEnvironmentVariable("AI_PROCESSING_QUEUE_URL") ?? "";
        }

        public class ScrapingPayload
        {
            public JobAggregator.BusinessLogic.DTOs.SearchCriteriaDto Criteria { get; set; } = new();
        }

        public async Task ProcessScrapingRequestAsync(string rawMessageBody)
        {
            // Deserialize an toàn, không phân biệt hoa thường
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var sqsPayload = JsonSerializer.Deserialize<ScrapingPayload>(rawMessageBody, options);
            
            if (sqsPayload == null || sqsPayload.Criteria == null) return;

            string keyword = sqsPayload.Criteria.Keyword ?? "";
            string location = sqsPayload.Criteria.Location ?? "";
            string jobType = sqsPayload.Criteria.JobType ?? "";
            
            var sources = sqsPayload.Criteria.Sources?.ToList() ?? new System.Collections.Generic.List<string>();
            if (sources.Count == 0) sources.Add("chotot"); // Default

            string tinyFishApiKey = Environment.GetEnvironmentVariable("TINYFISH_API_KEY") ?? "MOCK_API_KEY";

            var tasks = sources.Select(async source =>
            {
                string targetUrl = "";
                string platformName = "";

                if (source == "chotot")
                {
                    targetUrl = $"https://www.chotot.com/tags/toan-quoc/viec-lam-{keyword.Replace(" ", "-")}";
                    platformName = "Chợ Tốt";
                }
                else if (source == "facebook")
                {
                    targetUrl = $"https://www.facebook.com/search/groups/?q=viec%20lam%20parttime%20{keyword.Replace(" ", "%20")}";
                    platformName = "Facebook";
                }
                else if (source == "vieclam24h")
                {
                    targetUrl = $"https://vieclam24h.vn/tim-kiem-viec-lam-nhanh?q={keyword.Replace(" ", "+")}";
                    platformName = "ViecLam24h";
                }
                else if (source == "topcv")
                {
                    targetUrl = $"https://www.topcv.vn/tim-viec-lam-{keyword.Replace(" ", "-")}";
                    platformName = "TopCV";
                }

                if (string.IsNullOrEmpty(targetUrl)) return;

                // 1. CHUẨN BỊ LỆNH (PROMPT) SIÊU VIỆT CHO CON AI TINYFISH
                string goal = $"Go to {targetUrl}. Search for '{keyword}' jobs. Extract a MAXIMUM of 5 jobs. Stop immediately after extracting 5 jobs. ONLY extract jobs from the FIRST page. DO NOT click 'Next page' or navigate away. ONLY extract jobs located in '{location}' and job type '{jobType}'. Return a JSON array with 'Title', 'SalaryInfo', 'Location', 'EmployerName', 'ContactPhone', 'SpecificAddress', 'Platform' (value should be '{platformName}'), 'ExternalId' (a unique ID), and 'SourceUrl' (the direct link to the job post). Ignore completely mismatching jobs.";

                Console.WriteLine($"[TinyFish Agent] Bắt đầu cào {platformName} - URL: {targetUrl}");

                string aiJsonResponse;

                // 2. GỌI API CỦA TINYFISH
                if (tinyFishApiKey == "MOCK_API_KEY")
                {
                    await Task.Delay(2000); // Giả vờ mất 2 giây
                    string randomId = Guid.NewGuid().ToString().Substring(0, 8);
                    aiJsonResponse = $"[{{\"Title\": \"{jobType} {keyword} tại {location}\", \"SalaryInfo\": \"50k/giờ\", \"Location\": \"{location}\", \"Platform\": \"{platformName}\", \"ExternalId\": \"{platformName.ToLower()}_{randomId}\", \"SourceUrl\": \"{targetUrl}\", \"EmployerName\": \"Quán Cafe ABC\", \"ContactPhone\": \"0987654321\", \"SpecificAddress\": \"Số 1 Đường {location}\"}}]";
                }
                else
                {
                    var requestBody = new
                    {
                        url = targetUrl,
                        goal = goal,
                        output_schema = new {
                            type = "object",
                            properties = new {
                                jobs = new {
                                    type = "array",
                                    items = new {
                                        type = "object",
                                        properties = new {
                                            Title = new { type = "string" },
                                            SalaryInfo = new { type = "string" },
                                            Location = new { type = "string" },
                                            EmployerName = new { type = "string" },
                                            ContactPhone = new { type = "string" },
                                            SpecificAddress = new { type = "string" },
                                            Platform = new { type = "string" },
                                            ExternalId = new { type = "string" },
                                            SourceUrl = new { type = "string" }
                                        }
                                    }
                                }
                            },
                            required = new[] { "jobs" }
                        }
                    };

                    var content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
                    
                    // Tạo một HttpClient mới cho mỗi luồng song song để tránh xung đột
                    using var localClient = new HttpClient();
                    localClient.Timeout = TimeSpan.FromMinutes(5); // Ghi đè Timeout mặc định 100s của .NET
                    localClient.DefaultRequestHeaders.Add("X-API-Key", tinyFishApiKey);
                    
                    var response = await localClient.PostAsync("https://agent.tinyfish.ai/v1/automation/run", content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var responseString = await response.Content.ReadAsStringAsync();
                        using var document = JsonDocument.Parse(responseString);
                        Console.WriteLine($"[TinyFish Raw JSON] {responseString}");
                        
                        // Cố gắng tìm mảng jobs trong response
                        if (document.RootElement.TryGetProperty("result", out var resultElement) && 
                            resultElement.TryGetProperty("data", out var dataElement) &&
                            dataElement.TryGetProperty("jobs", out var jobsElement))
                        {
                            aiJsonResponse = jobsElement.GetRawText();
                        }
                        else if (document.RootElement.TryGetProperty("jobs", out var rootJobsElement))
                        {
                            // Nếu TinyFish trả thẳng cục data theo output_schema
                            aiJsonResponse = rootJobsElement.GetRawText();
                        }
                        else if (document.RootElement.TryGetProperty("output", out var outputElement) &&
                                 outputElement.TryGetProperty("jobs", out var outputJobsElement))
                        {
                            aiJsonResponse = outputJobsElement.GetRawText();
                        }
                        else if (document.RootElement.TryGetProperty("data", out var directDataElement) &&
                                 directDataElement.TryGetProperty("jobs", out var directDataJobsElement))
                        {
                            aiJsonResponse = directDataJobsElement.GetRawText();
                        }
                        else 
                        {
                            Console.WriteLine("[TinyFish Cảnh báo] Không tìm thấy mảng 'jobs' trong response!");
                            aiJsonResponse = "[]";
                        }
                    }
                    else 
                    {
                        Console.WriteLine($"[TinyFish Lỗi] HTTP {response.StatusCode} - {await response.Content.ReadAsStringAsync()}");
                        aiJsonResponse = "[]";
                    }
                }

                // 3. ĐẨY JSON TRỰC TIẾP SANG LUỒNG 3 (LƯU DB)
                var payload = new { 
                    Criteria = new JobAggregator.BusinessLogic.DTOs.SearchCriteriaDto 
                    { 
                        Keyword = keyword, 
                        Location = location, 
                        JobType = jobType 
                    }, 
                    RawJsonData = aiJsonResponse 
                };
                var message = new SendMessageRequest
                {
                    QueueUrl = _aiQueueUrl,
                    MessageBody = JsonSerializer.Serialize(payload)
                };

                await _sqsClient.SendMessageAsync(message);
            });

            await Task.WhenAll(tasks);
        }
    }
}
