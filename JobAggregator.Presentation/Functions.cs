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
        /// Handler 1: Bắt tin nhắn từ ScrapingQueue để cào dữ liệu thô
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
                    context.Logger.LogLine($"Lỗi cào dữ liệu: {ex.Message}");
                    throw; // Quăng lỗi để SQS tự động đẩy vào Dead Letter Queue (nếu có cấu hình)
                }
            }
        }

        /// <summary>
        /// Handler 2: Bắt tin nhắn từ AIProcessingQueue để nhờ Claude 3 đọc HTML
        /// </summary>
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
                    var payload = JsonSerializer.Deserialize<TinyFishPayload>(record.Body, options);
                    if (payload != null && !string.IsNullOrEmpty(payload.RawJsonData) && payload.Criteria != null)
                    {
                        await aiProcessorService.ProcessTinyFishDataAsync(payload.RawJsonData, payload.Criteria);
                    }
                }
                catch (Exception ex)
                {
                    context.Logger.LogLine($"Lỗi xử lý Data: {ex.Message}");
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

    public class TinyFishPayload
    {
        public string RawJsonData { get; set; } = string.Empty;
        public JobAggregator.BusinessLogic.DTOs.SearchCriteriaDto Criteria { get; set; } = new JobAggregator.BusinessLogic.DTOs.SearchCriteriaDto();
    }
}
