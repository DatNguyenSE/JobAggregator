using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Services;
using JobAggregator.DataAccess.Data;
using JobAggregator.DataAccess.Entities;
using JobAggregator.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;

static class CatalogChecks
{
    public static async Task RunAsync()
    {
        // Dedicated local database only; never use application's configured connection string.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=127.0.0.1;Port=55439;Database=postgres;Username=catalog_test").Options;
        await using var db = new AppDbContext(options);
        await db.Database.MigrateAsync();
        var sqs = new FakeSqs();
        var catalog = new JobCatalogService(db, sqs);
        var now = DateTime.UtcNow;
        var g1 = new FacebookGroup { Id = Guid.NewGuid(), Name = "test-one", CanonicalUrl = "https://www.facebook.com/groups/" + Guid.NewGuid() + "/", CreatedAt = now };
        var g2 = new FacebookGroup { Id = Guid.NewGuid(), Name = "test-two", CanonicalUrl = "https://www.facebook.com/groups/" + Guid.NewGuid() + "/", CreatedAt = now };
        var key = new SearchKeyword { Id = Guid.NewGuid(), Keyword = "", Location = "", JobType = "", LastScrapedAt = now };
        db.AddRange(g1, g2, key);
        foreach (var pair in new[] { (g1, "Phục vụ"), (g1, "Bếp"), (g2, "Bếp khác nhóm") })
            db.JobPosts.Add(new JobPost { Id = Guid.NewGuid(), SearchKeywordId = key.Id, FacebookGroupId = pair.Item1.Id,
                Title = pair.Item2, Roles = new[] { "phụ bếp" }, SourceUrl = "https://www.facebook.com/groups/test/posts/1/",
                Platform = "facebook", ExternalId = Guid.NewGuid().ToString(), CreatedAt = now.AddDays(-20), PostedDate = now.AddDays(-20) });
        await db.SaveChangesAsync();
        var criteria = new SearchCriteriaDto { Sources = new[] { "facebook" }, FacebookGroupId = g1.Id, PageSize = 1 };
        var first = await catalog.ReadAsync(criteria);
        Check(first.Jobs.Count == 1 && first.HasMore && first.Jobs[0].FacebookGroupId == g1.Id, "Group isolation, old posts visible and bounded pagination");
        criteria.Page = 2; var second = await catalog.ReadAsync(criteria);
        Check(second.Jobs.Count == 1 && second.Jobs[0].Id != first.Jobs[0].Id && !second.HasMore, "Second page is distinct");
        criteria.Page = 1; criteria.Keyword = "phụ bếp";
        Check((await catalog.ReadAsync(criteria)).Jobs.Count == 1, "Role array translates to PostgreSQL search");
        Check(sqs.Sends == 0, "Reading never schedules scraping");
        Environment.SetEnvironmentVariable("SCRAPING_QUEUE_URL", "https://example.invalid/test");
        Environment.SetEnvironmentVariable("AI_PROCESSING_QUEUE_URL", "https://example.invalid/ai-test");
        criteria.MaxJobs = 3; var token = Guid.NewGuid().ToString();
        var started = await catalog.StartAsync(criteria, token);
        var repeated = await catalog.StartAsync(criteria, token);
        Check(started.RequestId == repeated.RequestId && sqs.Sends == 1, "Idempotent paid scheduling");
        var stored = await db.JobSearchRequests.FindAsync(started.RequestId);
        Check(stored != null && stored.FacebookGroupId == g1.Id && stored.CriteriaJson.Contains("\"Keyword\":\"\""), "Feed crawl clears search keyword");
        stored!.Status = "partial"; stored.RawDataKey = "test/raw.json"; stored.InsertedCount = 1;
        await db.SaveChangesAsync();
        await catalog.RetryAsync(stored.Id);
        Check(sqs.Sends == 2 && sqs.LastQueue!.Contains("ai-test"), "Retry schedules only AI queue");
        var service = new JobSearchService(new JobRepository(db), sqs, catalog, db);
        stored.FailedCount = 0; await service.FinishAsync(stored.Id);
        Check(g1.LastSuccessfulScrapedAt.HasValue, "Successful run updates group timestamp");
        var post = new FacebookRawPost { Text = "hello  world", ImageUrls = new() { "https://scontent.xx.fbcdn.net/a.jpg?token=1" } };
        var hash = FacebookContentHash.Compute(post); post.Text = "hello world"; post.ImageUrls[0] = "https://scontent.xx.fbcdn.net/a.jpg?token=2";
        Check(hash == FacebookContentHash.Compute(post), "Whitespace and expiring image token do not invalidate content");
        post.Text = "changed"; Check(hash != FacebookContentHash.Compute(post), "Changed content invalidates hash");
        Console.WriteLine("PASS: 10 catalog integration checks against isolated PostgreSQL.");
    }
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    private sealed class FakeSqs() : AmazonSQSClient(new AnonymousAWSCredentials(), RegionEndpoint.APSoutheast1)
    {
        public int Sends; public string? LastQueue;
        public override Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
        { Sends++; LastQueue = request.QueueUrl; return Task.FromResult(new SendMessageResponse()); }
    }
}
