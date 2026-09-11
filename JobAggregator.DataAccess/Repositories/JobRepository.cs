using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JobAggregator.DataAccess.Data;
using JobAggregator.DataAccess.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobAggregator.DataAccess.Repositories
{
    public class JobRepository : IJobRepository
    {
        private readonly AppDbContext _context;

        public JobRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<JobPost?> GetByIdAsync(Guid id)
        {
            return await _context.JobPosts
                .Include(j => j.SearchKeyword)
                .FirstOrDefaultAsync(j => j.Id == id);
        }

        public async Task<IEnumerable<JobPost>> GetJobsByCriteriaAsync(string keyword, string location, string jobType,
            int limit = 20, DateTime? freshSince = null, string[]? sources = null)
        {
            var query = JobSearchQuery.Apply(_context.JobPosts.AsNoTracking(), keyword, location, jobType, freshSince, sources);
            return await query.Include(j => j.Locations)
                .OrderByDescending(j => j.PostedDate).ThenByDescending(j => j.LastScrapedAt ?? j.CreatedAt)
                .ThenBy(j => j.Id).Take(limit).ToListAsync();
        }

        public Task<SearchKeyword?> GetSearchKeywordAsync(string keyword, string location, string jobType)
        {
            var key = keyword.Trim().ToLowerInvariant();
            var area = (location ?? "").Trim().ToLowerInvariant();
            var type = (jobType ?? "").Trim().ToLowerInvariant();
            return _context.SearchKeywords.FirstOrDefaultAsync(k => k.Keyword.ToLower() == key
                && k.Location.ToLower() == area && k.JobType.ToLower() == type);
        }

        public Task<JobSearchRequest?> GetRequestAsync(Guid id) => _context.JobSearchRequests.FirstOrDefaultAsync(r => r.Id == id);
        public async Task AddRequestAsync(JobSearchRequest request) => await _context.JobSearchRequests.AddAsync(request);

        public async Task AddSearchKeywordAsync(SearchKeyword searchKeyword)
        {
            await _context.SearchKeywords.AddAsync(searchKeyword);
        }

        // BƯỚC 7: Upsert theo Platform + ExternalId: thêm nếu chưa có, cập nhật nếu đã có. Transaction bảo vệ khi nhiều Lambda cùng lưu một bài.
        public async Task AddJobPostAsync(JobPost jobPost)
        {
            // PostgreSQL transaction lock also serializes concurrent Lambda upserts of the same job.
            await using var transaction = await _context.Database.BeginTransactionAsync();
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({jobPost.Platform + ":" + jobPost.ExternalId}, 0))");
            var existing = await _context.JobPosts.Include(j => j.Locations)
                .FirstOrDefaultAsync(j => j.Platform == jobPost.Platform && j.ExternalId == jobPost.ExternalId);
            if (existing == null)
            {
                await _context.JobPosts.AddAsync(jobPost);
            }
            else
            {
                // Bỏ qua kết quả cào cũ đến muộn để SQS retry không ghi đè dữ liệu mới hơn.
                if ((existing.LastScrapedAt ?? existing.CreatedAt) > (jobPost.LastScrapedAt ?? jobPost.CreatedAt))
                {
                    await transaction.CommitAsync();
                    return;
                }
                jobPost.Id = existing.Id;
                jobPost.CreatedAt = existing.CreatedAt;
                // Keep original search provenance. Retrieval now uses the job's own locations.
                jobPost.SearchKeywordId = existing.SearchKeywordId;
                _context.Entry(existing).CurrentValues.SetValues(jobPost);
                if (jobPost.Locations.Count > 0)
                {
                    _context.JobLocations.RemoveRange(existing.Locations);
                    existing.Locations = jobPost.Locations;
                    foreach (var location in existing.Locations)
                    {
                        location.JobPostId = existing.Id;
                        _context.Entry(location).State = EntityState.Added;
                    }
                }
            }
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public Task UpdateSearchKeywordAsync(SearchKeyword searchKeyword)
        {
            _context.SearchKeywords.Update(searchKeyword);
            return Task.CompletedTask;
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
        }

        public async Task AddConnectionAsync(string connectionId)
        {
            await _context.WebSocketConnections.AddAsync(new WebSocketConnection 
            { 
                ConnectionId = connectionId, 
                ConnectedAt = DateTime.UtcNow 
            });
            await _context.SaveChangesAsync();
        }

        public async Task RemoveConnectionAsync(string connectionId)
        {
            var conn = await _context.WebSocketConnections.FindAsync(connectionId);
            if (conn != null)
            {
                _context.WebSocketConnections.Remove(conn);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<string>> GetAllConnectionIdsAsync()
        {
            return await _context.WebSocketConnections
                .Select(w => w.ConnectionId)
                .ToListAsync();
        }
    }
}
