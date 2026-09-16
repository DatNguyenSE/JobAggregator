using Microsoft.EntityFrameworkCore;
using JobAggregator.DataAccess.Entities;

namespace JobAggregator.DataAccess.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<FacebookGroup> FacebookGroups { get; set; }
        public DbSet<SearchKeyword> SearchKeywords { get; set; }
        public DbSet<JobSearchRequest> JobSearchRequests { get; set; }
        public DbSet<JobPost> JobPosts { get; set; }
        public DbSet<JobLocation> JobLocations { get; set; }
        public DbSet<WebSocketConnection> WebSocketConnections { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Retain the existing request table for a non-destructive upgrade; it now holds shared scrape runs.
            modelBuilder.Entity<FacebookGroup>().HasIndex(g => g.CanonicalUrl).IsUnique();
            modelBuilder.Entity<FacebookGroup>().HasIndex(g => g.ExternalGroupId).IsUnique();
            modelBuilder.Entity<JobPost>().HasOne(j => j.FacebookGroup).WithMany()
                .HasForeignKey(j => j.FacebookGroupId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<JobPost>().HasIndex(j => new { j.FacebookGroupId, j.PostedDate, j.Id });
            modelBuilder.Entity<JobPost>().HasIndex(j => new { j.Platform, j.PostedDate, j.Id });
            modelBuilder.Entity<JobSearchRequest>().HasOne(r => r.FacebookGroup).WithMany()
                .HasForeignKey(r => r.FacebookGroupId).OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<JobSearchRequest>().HasIndex(r => new { r.OwnerId, r.CreatedAt });
            modelBuilder.Entity<JobSearchRequest>().HasIndex(r => new { r.OwnerId, r.IdempotencyKey }).IsUnique();
            modelBuilder.Entity<JobSearchRequest>().HasIndex(r => new { r.ScopeKey, r.CreatedAt });

            // Cấu hình bảng WebSocketConnections
            modelBuilder.Entity<WebSocketConnection>()
                .HasKey(w => w.ConnectionId); // Dùng ConnectionId làm khóa chính luôn cho nhanh

            modelBuilder.Entity<JobPost>()
                .HasOne(j => j.SearchKeyword)
                .WithMany(k => k.JobPosts)
                .HasForeignKey(j => j.SearchKeywordId)
                .OnDelete(DeleteBehavior.Cascade);
                
            modelBuilder.Entity<JobLocation>()
                .HasOne(l => l.JobPost)
                .WithMany(j => j.Locations)
                .HasForeignKey(l => l.JobPostId)
                .OnDelete(DeleteBehavior.Cascade);
                
            modelBuilder.Entity<JobLocation>()
                .HasIndex(l => new { l.Province, l.District });

            // Cấu hình Unique Constraint để chống trùng lặp dựa vào Platform và ExternalId
            modelBuilder.Entity<JobPost>()
                .HasIndex(j => new { j.Platform, j.ExternalId })
                .IsUnique();
        }
    }
}
