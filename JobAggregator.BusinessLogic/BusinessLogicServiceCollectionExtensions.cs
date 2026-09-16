using Amazon.SQS;
using JobAggregator.BusinessLogic.Services;
using JobAggregator.BusinessLogic.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace JobAggregator.BusinessLogic
{
    public static class BusinessLogicServiceCollectionExtensions
    {
        public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
        {
            // Đăng ký các Interface - Service nội bộ
            services.AddScoped<IJobSearchService, JobSearchService>();
            services.AddScoped<JobCatalogService>();
            services.AddScoped<IAiJobProcessorService, AiJobProcessorService>();
            services.AddScoped<IDataFetcherService, DataFetcherService>();
            services.AddScoped<IS3StorageService, S3StorageService>();
            
            // Đăng ký AWS Clients (Tự động lấy thông tin xác thực từ AWS IAM Role của Lambda)
            services.AddSingleton<IAmazonSQS>(new AmazonSQSClient());
            services.AddSingleton<Amazon.S3.IAmazonS3>(new Amazon.S3.AmazonS3Client());

            return services;
        }
    }
}
