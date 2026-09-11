using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Services.Interfaces;
using JobAggregator.BusinessLogic.Services.Scrapers;

namespace JobAggregator.BusinessLogic.Services
{
    public class DataFetcherService : IDataFetcherService
    {
        private readonly IS3StorageService _s3StorageService;
        private readonly IAmazonSQS _sqsClient;
        private readonly string _aiQueueUrl;

        public DataFetcherService(IS3StorageService s3StorageService, IAmazonSQS sqsClient)
        {
            _s3StorageService = s3StorageService;
            _sqsClient = sqsClient;
            _aiQueueUrl = Environment.GetEnvironmentVariable("AI_PROCESSING_QUEUE_URL") ?? "";
        }

        public class ScrapingPayload
        {
            public SearchCriteriaDto Criteria { get; set; } = new();
        }

        // LUỒNG 2 - CÀO DỮ LIỆU:
        // DataFetcherFunction gọi hàm này khi nhận message từ ScrapingQueue.
        // JobSearchService đã tra DB và tính số lượng thiếu trước khi message đến đây.
        // Hàm chọn scraper, cào đúng số lượng thiếu rồi chuyển kết quả sang AIProcessingQueue.
        public async Task ProcessScrapingRequestAsync(string rawMessageBody)
        {
            // rawMessageBody là JSON do JobSearchService đóng gói, ví dụ:
            // {"Criteria":{"Keyword":"developer","MaxJobs":10,"Sources":["vieclam24h"]}}
            // Cho phép tên thuộc tính JSON khác chữ hoa/thường với class C#.
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // Deserialize chuỗi JSON thành đối tượng C# để đọc Criteria qua các property.
            var sqsPayload = JsonSerializer.Deserialize<ScrapingPayload>(rawMessageBody, options);
            
            if (sqsPayload == null || sqsPayload.Criteria == null) return;

            var criteria = sqsPayload.Criteria;
            int maxJobs = criteria.MaxJobs > 0 ? criteria.MaxJobs : 5;
            // Cho phép nhiều nguồn; mặc định Vieclam24h nếu danh sách rỗng.
            var sources = criteria.Sources?.ToList() ?? new System.Collections.Generic.List<string>();
            if (sources.Count == 0) sources.Add("vieclam24h");

            string apifyApiKey = Environment.GetEnvironmentVariable("APIFY_API_KEY") ?? throw new InvalidOperationException("APIFY_API_KEY environment variable is not set.");

            var scrapedAt = DateTime.UtcNow;
            var results = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            // results chứa một chuỗi JSON cho mỗi nguồn; errors gom lỗi theo nguồn.
            // scrapedAt đi tiếp tới DB để đánh dấu độ mới của cả lô dữ liệu.

            foreach (var source in sources)
            {
                try
                {
                    // Strategy cho phép gọi chung một interface dù cách cào mỗi nguồn khác nhau.
                    IJobScraperStrategy scraper = source switch
                    {
                        // Nếu nguồn là vieclam24h, dùng ViecLam24hScraper, còn lại dùng ApifyFallbackScraper.
                        "vieclam24h" => new ViecLam24hScraper(),
                        _ => new ApifyFallbackScraper(source, apifyApiKey)
                    };

                    // maxJobs là phần còn thiếu do JobSearchService tính, không luôn là tổng user yêu cầu.
                    var scrapedData = await scraper.ScrapeJobsAsJsonAsync(criteria, maxJobs);
                    results.Add(scrapedData);
                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Scraping Error] Source={source}, Request={criteria.RequestId}: {ex}");
                    errors.Add($"Không thể lấy dữ liệu từ {source}. Vui lòng thử lại sau.");
                }
            }
            // Gửi một envelope sang AIProcessingQueue, kể cả khi kết quả rỗng hoặc có lỗi.
            // Queue này kích hoạt AIProcessorFunction để chuẩn hóa/lưu DB, không quyết định cào bù lần nữa.
            await _sqsClient.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = _aiQueueUrl,
                MessageBody = JsonSerializer.Serialize(new { Criteria = criteria, SourceResults = results, Errors = errors, ScrapedAt = scrapedAt })
            });
        }
    }
}
