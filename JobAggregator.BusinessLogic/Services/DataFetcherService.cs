using System;
using Microsoft.EntityFrameworkCore;
using JobAggregator.DataAccess.Data;
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
        private readonly AppDbContext _db;
        private readonly IAmazonSQS _sqsClient;
        private readonly string _aiQueueUrl;

        public DataFetcherService(IS3StorageService s3StorageService, IAmazonSQS sqsClient, AppDbContext db)
        {
            _s3StorageService = s3StorageService;
            _db = db;
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
            if (!criteria.RequestId.HasValue) throw new InvalidOperationException("Missing persisted run ID.");
            var run = await _db.JobSearchRequests.FindAsync(criteria.RequestId.Value)
                ?? throw new InvalidOperationException("Unknown scrape run.");
            // A recorded raw artifact lets queue retries resume processing without paying for another scrape.
            if (run.RawDataKey != null)
            {
                if (run.Status is "complete" or "partial" or "failed") return;
                await _sqsClient.SendMessageAsync(new SendMessageRequest { QueueUrl = _aiQueueUrl,
                    MessageBody = JsonSerializer.Serialize(new { Criteria = criteria,
                        SourceResults = new[] { JsonSerializer.Serialize(new { Kind = "s3-source", Key = run.RawDataKey }) },
                        Errors = Array.Empty<string>(), ScrapedAt = run.StartedAt }) });
                return;
            }
            var claimed = await _db.JobSearchRequests.Where(r => r.Id == run.Id && r.Status == "pending")
                .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, "scraping").SetProperty(r => r.StartedAt, DateTime.UtcNow));
            if (claimed == 0) return; // Never silently start a second paid crawl on redelivery.
            await _db.Entry(run).ReloadAsync();
            int maxJobs = criteria.MaxJobs > 0 ? criteria.MaxJobs : 5;
            // Cho phép nhiều nguồn; mặc định Vieclam24h nếu danh sách rỗng.
            var sources = (criteria.Sources ?? Array.Empty<string>()).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
            if (sources.Count == 0) sources.Add("vieclam24h");

            string apifyApiKey = Environment.GetEnvironmentVariable("APIFY_API_KEY") ?? "";
            using var apifyClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(30) };

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
                        // Vieclam24h và Facebook có strategy riêng; nguồn website khác dùng fallback.
                        "vieclam24h" => new ViecLam24hScraper(),
                        "facebook" => new FacebookGroupScraper(apifyClient, apifyApiKey, async id => { run.ApifyRunId = id; await _db.SaveChangesAsync(); }),
                        _ => new ApifyFallbackScraper(source, apifyApiKey)
                    };

                    Console.WriteLine($"[ScraperSelected] Request={criteria.RequestId}, Source={source}, Strategy={scraper.GetType().Name}");
                    // maxJobs là phần còn thiếu do JobSearchService tính, không luôn là tổng user yêu cầu.
                    var scrapedData = await scraper.ScrapeJobsAsJsonAsync(criteria, maxJobs);
                    var key = await _s3StorageService.UploadJsonAsync(scrapedData);
                    run.RawDataKey = key;
                    run.Status = "processing";
                    await _db.SaveChangesAsync();
                    results.Add(JsonSerializer.Serialize(new { Kind = "s3-source", Key = key }));
                    
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Scraping Error] Source={source}, Request={criteria.RequestId}: {ex}");
                    errors.Add(ex is FacebookSearchException facebookError
                        ? facebookError.UserMessage
                        : $"Không thể lấy dữ liệu từ {source}. Vui lòng thử lại sau.");
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
