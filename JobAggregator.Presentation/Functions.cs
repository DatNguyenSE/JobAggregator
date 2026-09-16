using System;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using JobAggregator.BusinessLogic;
using JobAggregator.BusinessLogic.Services;
using JobAggregator.BusinessLogic.Services.Interfaces;
using JobAggregator.DataAccess;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Đăng ký bộ dịch JSON cho toàn bộ Lambda trong Assembly này
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace JobAggregator.Presentation
{
    public class Functions
    {
        private readonly IServiceProvider _serviceProvider;

        public Functions()
        {
            // Do hàm chạy ngầm (SQS Handler) không đi qua Program.cs nên ta phải tự nạp Dependency Injection
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var services = new ServiceCollection();
            
            // Đăng ký các tầng kiến trúc y hệt như Program.cs
            services.AddDataAccess(configuration);
            services.AddBusinessLogic();

            _serviceProvider = services.BuildServiceProvider();
        }

        /// <summary>
        /// LUỒNG 2: ScrapingQueue kích hoạt DataFetcherFunction để cào số job còn thiếu.
        /// </summary>
        public async Task DataFetcherHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            using var scope = _serviceProvider.CreateScope();
            var dataFetcherService = scope.ServiceProvider.GetRequiredService<IDataFetcherService>();

            foreach (var record in sqsEvent.Records)
            {
                try
                {
                    context.Logger.LogLine($"[Luồng 2 - Bắt đầu cào dữ liệu]: {record.Body}");
                    
                    if (!string.IsNullOrEmpty(record.Body))
                    {
                        await dataFetcherService.ProcessScrapingRequestAsync(record.Body);
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Lỗi cào dữ liệu: {ex}");
                    throw; // Quăng lỗi để SQS tự động đẩy vào Dead Letter Queue (nếu có cấu hình)
                }
            }
        }

        /// <summary>
        /// LUỒNG 3: AIProcessingQueue kích hoạt AIProcessorFunction để chuẩn hóa và lưu dữ liệu.
        /// </summary>
        // BƯỚC 4: Lambda nhận kết quả SQS rồi gọi ProcessJobDataAsync; không phải lúc nào cũng dùng AI.
        public async Task AIProcessorHandler(SQSEvent sqsEvent, ILambdaContext context)
        {
            using var scope = _serviceProvider.CreateScope();
            var aiProcessorService = scope.ServiceProvider.GetRequiredService<IAiJobProcessorService>();

            foreach (var record in sqsEvent.Records)
            {
                try
                {
                    context.Logger.LogLine($"[Luồng 3 - Bắt đầu lưu Database]: {record.Body}");

                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var payload = JsonSerializer.Deserialize<ScrapingResultPayload>(record.Body, options);
                    if (payload?.Criteria != null)
                    {
                        var db = scope.ServiceProvider.GetRequiredService<JobAggregator.DataAccess.Data.AppDbContext>();
                        if (!payload.Criteria.RequestId.HasValue) throw new InvalidOperationException("Missing run ID");
                        await using var processingLock = await RunProcessingLock.AcquireAsync(db, payload.Criteria.RequestId.Value);
                        db.ChangeTracker.Clear();
                        var savedRun = await db.JobSearchRequests.FindAsync(payload.Criteria.RequestId.Value);
                        if (savedRun == null || savedRun.Status is "complete" or "partial") continue;
                        var searchService = scope.ServiceProvider.GetRequiredService<IJobSearchService>();
                        try
                        {
                            var results = payload.SourceResults ?? new System.Collections.Generic.List<string>();
                            if (!string.IsNullOrWhiteSpace(payload.RawJsonData)) results.Add(payload.RawJsonData);
                            var scrapedAt = payload.ScrapedAt;
                            if (!scrapedAt.HasValue && record.Attributes != null &&
                                record.Attributes.TryGetValue("SentTimestamp", out var sent) && long.TryParse(sent, out var milliseconds))
                                scrapedAt = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
                            // List<JobPostDto> chuẩn đi thẳng; dữ liệu thô mới cần nhánh Bedrock.
                            foreach (var raw in results)
                                {
                                    var sourceJson = raw;
                                    using var source = JsonDocument.Parse(raw);
                                    if (source.RootElement.ValueKind == JsonValueKind.Object &&
                                        source.RootElement.TryGetProperty("Kind", out var kind) && kind.GetString() == "s3-source")
                                        sourceJson = await scope.ServiceProvider.GetRequiredService<IS3StorageService>()
                                            .DownloadJsonAsync(source.RootElement.GetProperty("Key").GetString()!);
                                    await aiProcessorService.ProcessJobDataAsync(sourceJson, payload.Criteria, scrapedAt);
                                }
                            // Đánh dấu request kết thúc để lần polling tiếp theo dừng trạng thái đang cào.
                            await searchService.FinishAsync(payload.Criteria.RequestId,
                                payload.Errors?.Count > 0 ? string.Join(" ", payload.Errors) : null);
                        }
                        catch (Exception ex)
                        {
                            context.Logger.LogLine($"[Processing Error] Request={payload.Criteria.RequestId}: {ex}");
                            // Use a fresh scope because the failed DbContext may still contain pending entities.
                            using var errorScope = _serviceProvider.CreateScope();
                            await errorScope.ServiceProvider.GetRequiredService<IJobSearchService>().FinishAsync(
                                payload.Criteria.RequestId, "Không thể lưu kết quả cào. Vui lòng thử lại sau.");
                            throw;
                        }
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Lỗi xử lý Data: {ex}");
                    throw;
                }
            }
        }
    }

    // Các class mồi (Payload) để bóc tách JSON từ SQS Message
    public class KeywordPayload
    {
        public string Keyword { get; set; } = string.Empty;
    }

    public class ScrapingResultPayload
    {
        public JobAggregator.BusinessLogic.DTOs.SearchCriteriaDto Criteria { get; set; } = new();
        public System.Collections.Generic.List<string>? SourceResults { get; set; }
        public System.Collections.Generic.List<string>? Errors { get; set; }
        public DateTime? ScrapedAt { get; set; }
        public string RawJsonData { get; set; } = string.Empty;
    }
}
