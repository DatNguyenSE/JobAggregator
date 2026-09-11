using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;
using JobAggregator.BusinessLogic.Services.Interfaces;
using JobAggregator.DataAccess.Entities;
using JobAggregator.DataAccess.Repositories;

namespace JobAggregator.BusinessLogic.Services;

public class JobSearchService : IJobSearchService
{
    private readonly IJobRepository _repository;
    private readonly IAmazonSQS _sqs;
    public JobSearchService(IJobRepository repository, IAmazonSQS sqs) { _repository = repository; _sqs = sqs; }

    public async Task<JobSearchResponse> SearchJobsAsync(SearchCriteriaDto criteria)
    {
        // LUỒNG 1 - DB FIRST: chuẩn hóa input để truy vấn DB và scraper dùng cùng tiêu chí.
        criteria.Keyword = criteria.Keyword.Trim();
        criteria.MaxJobs = Math.Clamp(criteria.MaxJobs > 0 ? criteria.MaxJobs : 5, 1, 20);
        criteria.Province = (criteria.Province ?? "").Trim();
        criteria.District = (criteria.District ?? "").Trim();
        if (criteria.Province.Length > 0)
            criteria.Location = criteria.District.Length > 0 ? $"{criteria.District}, {criteria.Province}" : criteria.Province;
        criteria.Location = (criteria.Location ?? "").Trim();
        criteria.JobType = (criteria.JobType ?? "").Trim();
        criteria.Sources = (criteria.Sources ?? Array.Empty<string>()).Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Distinct().ToArray();
        if (criteria.Sources.Length == 0) criteria.Sources = new[] { "vieclam24h" };
        // Trả ngay job đúng điều kiện và còn mới trong 24 giờ; response vẫn giữ các job này nếu phải cào bù.
        var response = await ReadJobsAsync(criteria);
        // Đã đủ số lượng: kết thúc tại đây, không tạo request và không gửi SQS.
        if (response.MissingCount == 0) return response;

        // Còn thiếu: lưu request để frontend theo dõi; loại các ID đã hiển thị khỏi lần cào bù.
        // Polling về sau chỉ đọc request này và không tạo thêm lượt cào.
        criteria.RequestId = Guid.NewGuid();
        criteria.ExcludedExternalIds = response.Jobs.Select(j => j.ExternalId).ToArray();
        var request = new JobSearchRequest { Id = criteria.RequestId.Value, CreatedAt = DateTime.UtcNow, CriteriaJson = JsonSerializer.Serialize(criteria) };
        await _repository.AddRequestAsync(request);
        await _repository.SaveChangesAsync();
        response.RequestId = request.Id;
        response.Status = "pending";
        try
        {
            var queueUrl = Environment.GetEnvironmentVariable("SCRAPING_QUEUE_URL");
            if (string.IsNullOrWhiteSpace(queueUrl)) throw new InvalidOperationException("SCRAPING_QUEUE_URL is missing");
            // Request lưu MaxJobs gốc; bản gửi scraper chỉ đổi MaxJobs thành số bài còn thiếu.
            var scrapingCriteria = JsonSerializer.Deserialize<SearchCriteriaDto>(request.CriteriaJson)!;
            scrapingCriteria.MaxJobs = response.MissingCount;
            await _sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queueUrl, MessageBody = JsonSerializer.Serialize(new { Criteria = scrapingCriteria }) });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Queue Error] Request={request.Id}: {ex}");
            request.Status = response.Status = "failed";
            request.CompletedAt = DateTime.UtcNow;
            request.Message = response.Message = "Chưa thể cào bổ sung. Các việc làm đã có vẫn được hiển thị; vui lòng thử lại.";
            await _repository.SaveChangesAsync();
        }
        return response;
    }

    public async Task<JobSearchResponse?> GetStatusAsync(Guid requestId)
    {
        // Endpoint polling dùng hàm này để đọc DB theo tiêu chí cũ; không gửi message cào mới.
        var request = await _repository.GetRequestAsync(requestId);
        if (request == null) return null;
        var criteria = JsonSerializer.Deserialize<SearchCriteriaDto>(request.CriteriaJson)!;
        var response = await ReadJobsAsync(criteria);
        response.RequestId = requestId;
        response.Status = response.MissingCount == 0 ? "complete" : request.Status;
        response.Message = request.Message;
        if (response.IsScraping && request.CreatedAt < DateTime.UtcNow.AddMinutes(-10))
        {
            response.Status = "failed";
            response.Message = "Cào bổ sung mất quá nhiều thời gian. Bạn có thể tìm lại; kết quả đã có vẫn được giữ.";
        }
        return response;
    }

    public async Task FinishAsync(Guid? requestId, string? error = null)
    {
        // AIProcessorFunction gọi khi đã xử lý xong mọi nguồn để frontend dừng trạng thái chờ.
        if (!requestId.HasValue) return;
        var request = await _repository.GetRequestAsync(requestId.Value);
        if (request == null) return;
        request.Status = error == null ? "complete" : "failed";
        request.Message = error;
        request.CompletedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync();
    }

    private async Task<JobSearchResponse> ReadJobsAsync(SearchCriteriaDto criteria)
    {
        // freshSince loại các record chưa được xác nhận/cào lại trong hơn 24 giờ khỏi kết quả hiển thị.
        var jobs = await _repository.GetJobsByCriteriaAsync(criteria.Keyword, criteria.Location, criteria.JobType,
            criteria.MaxJobs, DateTime.UtcNow.AddHours(-24), criteria.Sources);
        return new JobSearchResponse { Jobs = jobs.Select(j => j.ToDto()).ToList(), RequestedCount = criteria.MaxJobs };
    }
}
