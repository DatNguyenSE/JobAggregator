using Microsoft.EntityFrameworkCore;
using JobAggregator.DataAccess.Entities;

namespace JobAggregator.DataAccess.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<SearchKeyword> SearchKeywords { get; set; }
        public DbSet<JobPost> JobPosts { get; set; }
        public DbSet<WebSocketConnection> WebSocketConnections { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình bảng WebSocketConnections
            modelBuilder.Entity<WebSocketConnection>()
                .HasKey(w => w.ConnectionId); // Dùng ConnectionId làm khóa chính luôn cho nhanh

            modelBuilder.Entity<JobPost>()
                .HasOne(j => j.SearchKeyword)
                .WithMany(k => k.JobPosts)
                .HasForeignKey(j => j.SearchKeywordId)
                .OnDelete(DeleteBehavior.Cascade);

            // Cấu hình Unique Constraint để chống trùng lặp dựa vào Platform và ExternalId
            modelBuilder.Entity<JobPost>()
                .HasIndex(j => new { j.Platform, j.ExternalId })
                .IsUnique();
        }
    }
}
