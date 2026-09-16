using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;
using JobAggregator.BusinessLogic.Services.Scrapers;
using JobAggregator.DataAccess.Data;
using JobAggregator.DataAccess.Entities;
using JobAggregator.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace JobAggregator.BusinessLogic.Services;

public sealed class JobCatalogService(AppDbContext db, IAmazonSQS sqs)
{
    public async Task<object> GroupsAsync(int page = 1)
    {
        page = Math.Clamp(page, 1, 10000);
        var rows = await db.FacebookGroups.AsNoTracking().OrderBy(g => g.Name).ThenBy(g => g.Id)
            .Skip((page - 1) * 20).Take(21).Select(g => new {
                g.Id, g.Name, g.CanonicalUrl, g.LastSuccessfulScrapedAt,
                JobCount = db.JobPosts.Count(j => j.FacebookGroupId == g.Id),
                LastFetchedCount = db.JobSearchRequests.Where(r => r.FacebookGroupId == g.Id && r.Status == "complete")
                    .OrderByDescending(r => r.CompletedAt).Select(r => (int?)r.FetchedCount).FirstOrDefault()
            }).ToListAsync();
        return new { Groups = rows.Take(20), Page = page, HasMore = rows.Count > 20 };
    }

    public async Task<JobSearchResponse> ReadAsync(SearchCriteriaDto criteria)
    {
        var source = Source(criteria);
        var page = Math.Clamp(criteria.Page, 1, 10000);
        var size = Math.Clamp(criteria.PageSize, 1, 50);
        var query = JobSearchQuery.Apply(db.JobPosts.AsNoTracking(), criteria.Keyword ?? "", criteria.Location ?? "",
            criteria.JobType ?? "", null, new[] { source });
        if (source == "facebook")
        {
            if (!criteria.FacebookGroupId.HasValue)
                return new JobSearchResponse { RequestedCount = size, Page = page };
            query = query.Where(j => j.FacebookGroupId == criteria.FacebookGroupId);
        }
        var rows = await query.Include(j => j.Locations).OrderByDescending(j => j.PostedDate).ThenBy(j => j.Id)
            .Skip((page - 1) * size).Take(size + 1).ToListAsync();
        return new JobSearchResponse { Jobs = rows.Take(size).Select(j => j.ToDto()).ToList(),
            RequestedCount = size, Page = page, HasMore = rows.Count > size, FacebookGroupId = criteria.FacebookGroupId };
    }

    public static string Source(SearchCriteriaDto criteria)
    {
        var sources = (criteria.Sources ?? Array.Empty<string>()).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (sources.Length != 1 || (sources[0] != "facebook" && sources[0] != "vieclam24h"))
            throw new ArgumentException("Chọn đúng một nguồn Facebook hoặc Vieclam24h.");
        return sources[0];
    }

    public async Task<JobSearchResponse> StartAsync(SearchCriteriaDto criteria, string idempotencyKey)
    {
        if (!Guid.TryParse(idempotencyKey, out _)) throw new ArgumentException("Thiếu mã chống gửi lặp hợp lệ.");
        var source = Source(criteria);
        criteria.Keyword = (criteria.Keyword ?? "").Trim();
        if (source == "vieclam24h" && criteria.Keyword.Length == 0) throw new ArgumentException("Nhập từ khóa việc làm.");
        var cap = int.TryParse(Environment.GetEnvironmentVariable("SCRAPE_MAX_POSTS"), out var max) ? Math.Clamp(max, 1, 500) : 20;
        if (criteria.MaxJobs < 1 || criteria.MaxJobs > cap) throw new ArgumentException($"Giới hạn hiện tại: 1–{cap} bài/lượt.");
        criteria.Page = 1;
        criteria.ExcludedExternalIds = Array.Empty<string>();
        criteria.Location = string.IsNullOrWhiteSpace(criteria.Province) ? (criteria.Location ?? "").Trim()
            : string.IsNullOrWhiteSpace(criteria.District) ? criteria.Province.Trim() : $"{criteria.District.Trim()}, {criteria.Province.Trim()}";
        criteria.JobType = (criteria.JobType ?? "").Trim();
        criteria.Sources = new[] { source };
        // Serialize admin scheduling across API instances; quota checks and insert are atomic.
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(763412901)");
        var previous = await db.JobSearchRequests.AsNoTracking().FirstOrDefaultAsync(r => r.OwnerId == "admin" && r.IdempotencyKey == idempotencyKey);
        if (previous != null) { await tx.CommitAsync(); return await StatusAsync(previous.Id) ?? throw new InvalidOperationException(); }
        var now = DateTime.UtcNow;
        // Expire abandoned paid runs instead of holding the test UI forever. Never auto-recrawl.
        await db.JobSearchRequests.Where(r => r.OwnerId == "admin" && (r.Status == "pending" || r.Status == "scraping" || r.Status == "processing")
            && (r.StartedAt ?? r.CreatedAt) < now.AddMinutes(-30))
            .ExecuteUpdateAsync(update => update.SetProperty(r => r.Status, "failed")
                .SetProperty(r => r.CompletedAt, now).SetProperty(r => r.Message, "Lượt hết thời gian. Kiểm tra dữ liệu gốc hoặc Apify trước khi cào lại."));
        if (await db.JobSearchRequests.AnyAsync(r => r.OwnerId == "admin" && (r.Status == "pending" || r.Status == "scraping" || r.Status == "processing")))
            throw new InvalidOperationException("Đang có lượt cào/xử lý. Chờ lượt hiện tại kết thúc trước khi cào tiếp.");
        string scope;
        if (source == "facebook")
        {
            criteria.Keyword = "";
            criteria.FacebookSearchYear = null;
            FacebookGroup? group = null;
            if (criteria.FacebookGroupId.HasValue)
                group = await db.FacebookGroups.FindAsync(criteria.FacebookGroupId.Value)
                    ?? throw new ArgumentException("Nhóm không tồn tại.");
            if (group == null && string.IsNullOrWhiteSpace(criteria.FacebookGroupUrl)) throw new ArgumentException("Nhập URL nhóm Facebook.");
            var url = FacebookGroupScraper.NormalizeGroupUrl(group?.CanonicalUrl ?? criteria.FacebookGroupUrl);
            criteria.FacebookGroupUrl = url;
            _ = FacebookGroupScraper.BuildInput(criteria, criteria.MaxJobs);
            group ??= await db.FacebookGroups.FirstOrDefaultAsync(g => g.CanonicalUrl == url);
            if (group == null)
            {
                var slug = new Uri(url).Segments.Last().Trim('/');
                group = new FacebookGroup { Id = Guid.NewGuid(), Name = slug, CanonicalUrl = url,
                    ExternalGroupId = slug.All(char.IsDigit) ? slug : null, CreatedAt = now };
                db.FacebookGroups.Add(group);
            }
            criteria.FacebookGroupId = group.Id;
            scope = "facebook:" + group.Id;
        }
        else
        {
            criteria.FacebookGroupId = null;
            scope = "vieclam24h:" + criteria.Keyword.ToLowerInvariant() + ":" + criteria.Location.ToLowerInvariant() + ":" + criteria.JobType.ToLowerInvariant();
        }
        criteria.RequestId = Guid.NewGuid();
        var run = new JobSearchRequest { Id = criteria.RequestId.Value, Platform = source, FacebookGroupId = criteria.FacebookGroupId,
            OwnerId = "admin", ScopeKey = scope, IdempotencyKey = idempotencyKey, CreatedAt = now,
            RequestedLimit = criteria.MaxJobs, CriteriaJson = JsonSerializer.Serialize(criteria) };
        db.JobSearchRequests.Add(run);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        try
        {
            var queue = Environment.GetEnvironmentVariable("SCRAPING_QUEUE_URL");
            if (string.IsNullOrWhiteSpace(queue)) throw new InvalidOperationException("Missing queue configuration");
            await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = JsonSerializer.Serialize(new { Criteria = criteria }) });
        }
        catch
        {
            run.Status = "failed"; run.CompletedAt = DateTime.UtcNow;
            run.Message = "Không thể gửi yêu cầu cào. Chưa có kết quả mới.";
            await db.SaveChangesAsync();
        }
        return await StatusAsync(run.Id) ?? throw new InvalidOperationException();
    }

    public async Task<JobSearchResponse> RetryAsync(Guid id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(763412901)");
        var run = await db.JobSearchRequests.FindAsync(id) ?? throw new ArgumentException("Không tìm thấy lượt cào.");
        if (run.RawDataKey == null) throw new InvalidOperationException("Chưa có dữ liệu gốc để xử lý lại.");
        if (run.Status is not ("partial" or "failed")) throw new InvalidOperationException("Chỉ xử lý lại lượt đã kết thúc với lỗi.");
        if (await db.JobSearchRequests.AnyAsync(r => r.Status == "pending" || r.Status == "scraping" || r.Status == "processing"))
            throw new InvalidOperationException("Đang có lượt khác chạy.");
        run.Status = "processing"; run.CompletedAt = null; run.StartedAt = DateTime.UtcNow; run.Message = "Đang xử lý lại dữ liệu đã lưu; không gọi Apify.";
        await db.SaveChangesAsync(); await tx.CommitAsync();
        var criteria = JsonSerializer.Deserialize<SearchCriteriaDto>(run.CriteriaJson)!;
        try
        {
            var queue = Environment.GetEnvironmentVariable("AI_PROCESSING_QUEUE_URL");
            if (string.IsNullOrWhiteSpace(queue)) throw new InvalidOperationException();
            await sqs.SendMessageAsync(new SendMessageRequest { QueueUrl = queue, MessageBody = JsonSerializer.Serialize(new {
                Criteria = criteria, SourceResults = new[] { JsonSerializer.Serialize(new { Kind = "s3-source", Key = run.RawDataKey }) },
                Errors = Array.Empty<string>(), ScrapedAt = run.CreatedAt }) });
        }
        catch { run.Status = "failed"; run.CompletedAt = DateTime.UtcNow; run.Message = "Không thể gửi yêu cầu xử lý lại."; await db.SaveChangesAsync(); }
        return await StatusAsync(id) ?? throw new InvalidOperationException();
    }

    public async Task<JobSearchResponse?> StatusAsync(Guid id)
    {
        var run = await db.JobSearchRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (run == null) return null;
        var criteria = JsonSerializer.Deserialize<SearchCriteriaDto>(run.CriteriaJson)!;
        var response = await ReadAsync(criteria);
        response.RequestId = id; response.Status = run.Status; response.Message = run.Message;
        response.FetchedCount = run.FetchedCount; response.InsertedCount = run.InsertedCount;
        response.UpdatedCount = run.UpdatedCount; response.UnchangedCount = run.UnchangedCount;
        response.RejectedCount = run.RejectedCount; response.FailedCount = run.FailedCount;
        return response;
    }
}
