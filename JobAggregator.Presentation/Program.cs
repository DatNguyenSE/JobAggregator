using JobAggregator.BusinessLogic;
using JobAggregator.BusinessLogic.Services.Interfaces;
using JobAggregator.DataAccess;
using JobAggregator.DataAccess.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);


        
// Tích hợp hosting cho AWS Lambda API Gateway
builder.Services.AddAWSLambdaHosting(LambdaEventSource.RestApi);


// Cấu hình CORS cho Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

// ĐĂNG KÝ DEPENDENCY INJECTION CHO TỪNG LAYER (Cấu trúc N-Tier)
builder.Services.AddDataAccess(builder.Configuration);
builder.Services.AddBusinessLogic();

var app = builder.Build();

app.UseCors("AllowAll");

// MINIMAL API ROUTE 1: Khởi tạo/Cập nhật Database (Dùng cho môi trường Dev hoặc khởi tạo nhanh)
app.MapPost("/api/jobs/init-db", async (JobAggregator.DataAccess.Data.AppDbContext dbContext) =>
{
    try
    {
        // Tự động áp dụng các bản Migration để tạo bảng trong PostgreSQL
        await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(dbContext.Database);
        return Results.Ok(new { Message = "Cấu trúc Database đã được khởi tạo/cập nhật thành công!" });
    }
    catch (System.Exception ex)
    {
        return Results.Problem(detail: ex.Message, title: "Lỗi khởi tạo DB");
    }
});

// MINIMAL API ROUTE 2: Nhận HTTP POST từ Frontend Angular với bộ lọc đa tiêu chí
app.MapPost("/api/jobs/search", async (JobAggregator.BusinessLogic.DTOs.SearchCriteriaDto criteria, IJobSearchService searchService) =>
{
    if (criteria == null || string.IsNullOrWhiteSpace(criteria.Keyword))
    {
        return Results.BadRequest("Keyword không được bỏ trống.");
    }

    // Gọi tầng Business Logic để xử lý
    var result = await searchService.SearchJobsAsync(criteria);

    if (result.IsFound)
    {
        // Có dữ liệu -> Trả về HTTP 200 (OK) và danh sách DTO Json ngay lập tức
        return Results.Ok(result.Jobs);
    }
    else
    {
        // Không có dữ liệu -> Trả về HTTP 202 (Accepted) để Frontend hiển thị màn hình chờ (Polling)
        return Results.Accepted(value: new 
        { 
            Message = "Hệ thống đang thu thập dữ liệu mới từ mạng xã hội. Vui lòng làm mới trang sau ít phút." 
        });
    }
});

app.Run();
