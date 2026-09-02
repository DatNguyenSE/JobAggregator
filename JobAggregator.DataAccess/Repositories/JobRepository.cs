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

        public async Task<IEnumerable<JobPost>> GetJobsByCriteriaAsync(string keyword, string location, string jobType)
        {
            return await _context.JobPosts
                .Include(j => j.SearchKeyword)
                .Where(j => 
                    j.SearchKeyword.Keyword.ToLower() == keyword.ToLower() &&
                    (string.IsNullOrEmpty(location) || j.SearchKeyword.Location.ToLower() == location.ToLower()) &&
                    (string.IsNullOrEmpty(jobType) || j.SearchKeyword.JobType.ToLower() == jobType.ToLower())
                )
                .OrderByDescending(j => j.PostedDate)
                .ToListAsync();
        }

        public async Task<SearchKeyword?> GetSearchKeywordAsync(string keyword, string location, string jobType)
        {
            return await _context.SearchKeywords
                .FirstOrDefaultAsync(k => 
                    k.Keyword.ToLower() == keyword.ToLower() &&
                    k.Location.ToLower() == (location ?? "").ToLower() &&
                    k.JobType.ToLower() == (jobType ?? "").ToLower()
                );
        }

        public async Task AddSearchKeywordAsync(SearchKeyword searchKeyword)
        {
            await _context.SearchKeywords.AddAsync(searchKeyword);
        }

        public async Task AddJobPostAsync(JobPost jobPost)
        {
            // Kiểm tra chống trùng lặp dữ liệu trước khi Insert dựa vào Unique Constraint
            // Tránh văng Exception DB nếu cào trúng bài cũ
            var exists = await _context.JobPosts.AnyAsync(j => 
                j.Platform == jobPost.Platform && 
                j.ExternalId == jobPost.ExternalId);

            if (!exists)
            {
                await _context.JobPosts.AddAsync(jobPost);
            }
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
