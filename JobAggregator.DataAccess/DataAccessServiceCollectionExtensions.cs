using JobAggregator.DataAccess.Data;
using JobAggregator.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace JobAggregator.DataAccess
{
    public static class DataAccessServiceCollectionExtensions
    {
        public static IServiceCollection AddDataAccess(this IServiceCollection services, IConfiguration configuration)
        {
            // Lấy chuỗi kết nối từ cấu hình (Appsettings.json) hoặc Biến môi trường (AWS Lambda)
            var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING")
                                   ?? configuration.GetConnectionString("DefaultConnection");

            services.AddDbContext<AppDbContext>(options =>
                options.UseNpgsql(connectionString));

            // Đăng ký DI cho Repository
            services.AddScoped<IJobRepository, JobRepository>();

            return services;
        }
    }
}
