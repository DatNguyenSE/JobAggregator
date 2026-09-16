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
    private readonly JobCatalogService _catalog;
    private readonly JobAggregator.DataAccess.Data.AppDbContext _db;
    public JobSearchService(IJobRepository repository, IAmazonSQS sqs, JobCatalogService catalog, JobAggregator.DataAccess.Data.AppDbContext db)
    { _repository = repository; _sqs = sqs; _catalog = catalog; _db = db; }

    public Task<JobSearchResponse> SearchJobsAsync(SearchCriteriaDto criteria) => _catalog.ReadAsync(criteria);
    public Task<JobSearchResponse?> GetStatusAsync(Guid requestId) => _catalog.StatusAsync(requestId);

    public async Task FinishAsync(Guid? requestId, string? error = null)
    {
        // AIProcessorFunction gọi khi đã xử lý xong mọi nguồn để frontend dừng trạng thái chờ.
        if (!requestId.HasValue) return;
        var request = await _repository.GetRequestAsync(requestId.Value);
        if (request == null) return;
        request.CompletedAt = DateTime.UtcNow;
        request.CompletedAt = DateTime.UtcNow;
        request.Status = error == null && request.FailedCount == 0 ? "complete"
            : request.InsertedCount + request.UpdatedCount + request.UnchangedCount + request.RejectedCount > 0 ? "partial" : "failed";
        request.Message = error ?? $"Lấy {request.FetchedCount} bài: {request.InsertedCount} mới, {request.UpdatedCount} cập nhật, " +
            $"{request.UnchangedCount} không đổi, {request.RejectedCount} không tuyển dụng, {request.FailedCount} lỗi.";
        if (request.FetchedCount >= request.RequestedLimit && request.RequestedLimit > 0)
            request.Message += " Đã chạm giới hạn; nguồn có thể còn bài chưa lấy.";
        if (request.Status == "complete" && request.FacebookGroupId.HasValue)
        {
            var group = await _db.FacebookGroups.FindAsync(request.FacebookGroupId.Value);
            if (group != null) group.LastSuccessfulScrapedAt = request.CompletedAt;
        }
        request.CompletedAt = DateTime.UtcNow;
        await _repository.SaveChangesAsync();
    }

}
