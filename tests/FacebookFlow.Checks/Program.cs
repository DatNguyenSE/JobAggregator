using System.Net;
using System.Text;
using System.Text.Json;
using Amazon;
using Amazon.Runtime;
using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;
using JobAggregator.BusinessLogic.Services;
using JobAggregator.BusinessLogic.Services.Scrapers;

if (args.Contains("--catalog")) { await CatalogChecks.RunAsync(); return; }
if (args.Length == 2 && args[0] == "--replay")
{

var raw = JsonSerializer.Deserialize<FacebookPostBatch>(await File.ReadAllTextAsync(args[1]))!;
    using var realBedrock = new AmazonBedrockRuntimeClient(RegionEndpoint.APSoutheast1);
    var extracted = await new FacebookJobExtractor(realBedrock).ExtractAsync(raw,
        new() { Keyword = "Nhân viên phụ bếp", MaxJobs = 5 });
    Console.WriteLine($"Replay: {raw.Posts.Count} posts -> {extracted.Count} listings. No database writes.");
    foreach (var item in extracted) Console.WriteLine($"Listing: {item.Title}");
    return;
}

if (args.Contains("--live"))
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    var result = await new FacebookGroupScraper(http, Environment.GetEnvironmentVariable("APIFY_API_KEY") ?? "")
        .ScrapeJobsAsJsonAsync(new SearchCriteriaDto { Keyword = "phụ bếp" }, 3);
    var batch = JsonSerializer.Deserialize<FacebookPostBatch>(result)!;
    Console.WriteLine($"Live Apify: {batch.Posts.Count} posts; {batch.Posts.Count(x => x.ImageUrls.Count > 0)} with images.");
    if (batch.Posts.Count == 0) Console.WriteLine("No search results; no group-feed fallback was attempted.");
    return;
}

int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks++; }
async Task Fails(Func<Task> work, string name)
{
    try { await work(); } catch { checks++; return; }
    throw new Exception("Expected failure: " + name);
}
const string fixture = """
[{"url":"https://www.facebook.com/groups/350588362041550/posts/123/?tracking=1","legacyId":"123","time":"2026-09-11T10:00:00Z","text":"Tuyển nhân viên","media":[{"__typename":"Photo","image":{"uri":"https://scontent.xx.fbcdn.net/banner.jpg"}}],"user":{"profilePicture":"https://scontent.xx.fbcdn.net/avatar.jpg"}},
 {"url":"https://www.facebook.com/groups/350588362041550/posts/123/","time":"2026-09-11T10:00:00Z","text":"duplicate"},
 {"url":"https://www.facebook.com/groups/350588362041550/posts/124/","time":"2026-09-11T10:00:00Z","text":""}]
""";
var handler = new ApifyHandler(fixture);
using var client = new HttpClient(handler);
var scraper = new FacebookGroupScraper(client, "test-token");
var batchResult = JsonSerializer.Deserialize<FacebookPostBatch>(await scraper.ScrapeJobsAsJsonAsync(new() { Keyword = "phụ bếp" }, 3))!;
Check(batchResult.Posts.Count == 1, "Drop duplicates and empty posts");
using var customClient = new HttpClient(new ApifyHandler(fixture, expectedUrl: "https://www.facebook.com/groups/custom-group/"));
await new FacebookGroupScraper(customClient, "test-token").ScrapeJobsAsJsonAsync(
    new() { Keyword = "  phụ bếp  ", FacebookGroupUrl = " https://m.facebook.com/groups/custom-group/?ref=share " }, 3);
Check(true, "Custom group URL reaches Apify with tracking removed");
foreach (var invalid in new[] { "", "https://facebook.com.evil.com/groups/test/", "https://www.facebook.com/groups/test/posts/123/", "http://facebook.com/groups/test/" })
    await Fails(() => scraper.ScrapeJobsAsJsonAsync(new() { Keyword = "  phụ bếp  ", FacebookGroupUrl = invalid }, 3), "Reject invalid group URL");
var feedInput = FacebookGroupScraper.BuildInput(new() { Keyword = "   ", FacebookSearchYear = 2026 }, 3);
Check(!feedInput.ContainsKey("searchGroupKeyword"), "Feed crawl omits keyword");
Check(!feedInput.ContainsKey("searchGroupYear"), "Feed crawl omits search year");
var emptyHandler = new ApifyHandler("[]");
using var emptyClient = new HttpClient(emptyHandler);
var emptyBatch = JsonSerializer.Deserialize<FacebookPostBatch>(
    await new FacebookGroupScraper(emptyClient, "test-token").ScrapeJobsAsJsonAsync(new() { Keyword = "phụ bếp" }, 3))!;
Check(emptyBatch.Posts.Count == 0 && emptyHandler.Runs == 1, "Empty keyword search does not fall back to group feed");
var post = batchResult.Posts[0];
Check(post.ExternalId == "123" && !post.SourceUrl.Contains('?'), "Stable ID and canonical URL");
Check(post.ImageUrls.Count == 1 && post.ImageUrls[0].EndsWith("banner.jpg"), "Only post media, no avatars");
Check(FacebookJobExtractor.NeedsVision(post), "Short text with image uses Vision");
Check(FacebookJobExtractor.NeedsVision(new() { ImageUrls = post.ImageUrls }), "Image-only uses Vision");
Check(!FacebookJobExtractor.NeedsVision(new() { Text = new string('x', 151) + " lương", ImageUrls = post.ImageUrls }), "Rich text skips Vision");
Check(!FacebookJobExtractor.NeedsVision(new() { Text = "Tuyển gấp" }), "No images uses text");
Check(!FacebookPostMappingExtensions.IsFacebookImageUrl("https://scontent.xx.fbcdn.net.attacker.com/a"), "Block misleading CDN host");
Check(!FacebookPostMappingExtensions.IsFacebookImageUrl("http://127.0.0.1/a"), "Block private image URL");
var excluded = JsonSerializer.Deserialize<FacebookPostBatch>(await scraper.ScrapeJobsAsJsonAsync(new() { Keyword = "phụ bếp", ExcludedExternalIds = new[] { "123" } }, 3))!;
Check(excluded.Posts.Count == 0, "Exclude previously found post IDs");
using var failed = new HttpClient(new ApifyHandler(fixture, "FAILED"));
await Fails(() => new FacebookGroupScraper(failed, "test-token").ScrapeJobsAsJsonAsync(new() { Keyword = "phụ bếp" }, 3), "Actor failure propagated");
using var bad = new HttpClient(new ApifyHandler("[{\"error\":\"private group\"}]"));
await Fails(() => new FacebookGroupScraper(bad, "test-token").ScrapeJobsAsJsonAsync(new() { Keyword = "phụ bếp" }, 3), "Dataset error propagated");
var inputWithFilters = JsonSerializer.SerializeToElement(FacebookGroupScraper.BuildInput(new()
{
    Keyword = " game ", FacebookGroupUrl = "https://www.facebook.com/groups/558952182482387/",
    FacebookViewOption = "CHRONOLOGICAL_LISTINGS", FacebookSearchYear = 2026,
    FacebookOnlyPostsNewerThan = "2026-09-01"
}, 10));
Check(inputWithFilters.GetProperty("searchGroupKeyword").GetString() == "game", "Preserve exact keyword");
Check(inputWithFilters.GetProperty("searchGroupYear").GetString() == "2026", "Actor year is a string");
Check(inputWithFilters.GetProperty("onlyPostsNewerThan").GetString() == "2026-09-01" &&
    inputWithFilters.GetProperty("viewOption").GetString() == "CHRONOLOGICAL_LISTINGS", "Date and sorting reach actor");
var defaultInput = FacebookGroupScraper.BuildInput(new() { Keyword = "game" }, 5);
Check(!defaultInput.ContainsKey("searchGroupYear") && !defaultInput.ContainsKey("onlyPostsNewerThan"), "Omit empty optional filters");
foreach (var invalidCriteria in new SearchCriteriaDto[]
{
    new() { Keyword = "game", FacebookViewOption = "MARKETPLACE" },
    new() { Keyword = "game", FacebookSearchYear = 1990 },
    new() { Keyword = "game", FacebookOnlyPostsNewerThan = "2026-02-30" }
})
    await Fails(() => { FacebookGroupScraper.BuildInput(invalidCriteria, 5); return Task.CompletedTask; }, "Reject invalid actor filters");
var blockedHandler = new ApifyHandler("[{\"url\":\"https://www.facebook.com/api/graphql/\",\"error\":\"no_items\",\"errorDescription\":\"Empty or private data for provided input\"}]");
using var blockedClient = new HttpClient(blockedHandler);
var reportedBlocked = false;
try
{
    await new FacebookGroupScraper(blockedClient, "test-token").ScrapeJobsAsJsonAsync(new() { Keyword = "phụ bếp" }, 3);
}
catch (FacebookSearchException ex)
{
    reportedBlocked = ex.UserMessage.Contains("no_items") && ex.Message.Contains("run1");
}
Check(reportedBlocked && blockedHandler.Runs == 1, "Succeeded run with no_items stays a diagnostic failure, no feed fallback");
using var bedrock = new FakeBedrock();
var extractor = new FacebookJobExtractor(bedrock);
post.ImageUrls.Clear();
var jobs = await extractor.ExtractAsync(new() { Posts = new() { post, post, new() } }, new());
Check(jobs.Count == 1 && bedrock.Calls == 1, "Extract once, skip duplicate and empty posts");
Check(jobs[0].Platform == "facebook" && jobs[0].ExternalId == "123" && jobs[0].PostedDate == post.PostedDate && jobs[0].SourceUrl == post.SourceUrl, "Override model-generated metadata");
bedrock.Output = "[]";
Check((await extractor.ExtractAsync(new() { Posts = new() { post } }, new())).Count == 0, "Non-job output ignored");
bedrock.Output = "Based on the post, here is the result:\n" + "[{\"Title\":\"Phục vụ [part-time]\",\"Locations\":[{\"Province\":\"HCM\"}]}]" + "\nEnd of result.";
Check((await extractor.ExtractAsync(new() { Posts = new() { post } }, new()))[0].Title == "Phục vụ [part-time]", "Prose-wrapped JSON preserves nested arrays and brackets in text");
bedrock.Output = "Based on the post, no matching job.\n[]";
Check((await extractor.ExtractAsync(new() { Posts = new() { post } }, new())).Count == 0, "Prose-wrapped empty result");
bedrock.Output = new string((char)96, 3) + "json\n[{\"Title\":\"Phục vụ\"}]\n" + new string((char)96, 3);
Check((await extractor.ExtractAsync(new() { Posts = new() { post } }, new())).Count == 1, "Markdown JSON");
foreach (var invalidOutput in new[] { "[{\"Title\":\"unfinished", "[null]", "[] []" })
{
    bedrock.Output = invalidOutput;
    await Fails(() => extractor.ExtractAsync(new() { Posts = new() { post } }, new()), "Reject malformed or ambiguous model output");
}
bedrock.Output = "[{\"Title\":\"Phụ bếp\",\"SalaryInfo\":\"7 triệu\",\"Vacancies\":2,\"Locations\":[{\"Province\":\"HCM\"}]},{\"Title\":\"Phục vụ\",\"SalaryInfo\":\"8 triệu\",\"Vacancies\":3,\"Locations\":[{\"Province\":\"HCM\"}]}]";
var combined = await extractor.ExtractAsync(new() { Posts = new() { post } }, new());
Check(combined.Count == 1 && combined[0].Title == "Phụ bếp / Phục vụ", "Multiple roles become one listing per source post");
Check(combined[0].SalaryInfo!.Contains("Phụ bếp: 7 triệu") && combined[0].SalaryInfo.Contains("Phục vụ: 8 triệu"), "Preserve role-specific salaries");
Check(combined[0].Locations.Count == 1 && combined[0].JobInfo.Contains("Phụ bếp: 2"), "Deduplicate addresses and preserve role headcounts");
Check(combined[0].ExternalId == post.ExternalId && combined[0].SourceUrl == post.SourceUrl, "Merged roles retain authoritative source identity");
bedrock.Output = "invalid JSON";
await Fails(() => extractor.ExtractAsync(new() { Posts = new() { post } }, new()), "Invalid model output propagated");
using var imageHttp = new HttpClient(new ImageHandler());
using var visionBedrock = new FakeBedrock { ExpectImage = true };
var vision = new FacebookJobExtractor(visionBedrock, imageHttp);
var imagePost = new FacebookRawPost { ExternalId = "image", SourceUrl = post.SourceUrl,
    PostedDate = post.PostedDate, ImageUrls = new() { "https://scontent.xx.fbcdn.net/banner.jpg" } };
Check((await vision.ExtractAsync(new() { Posts = new() { imagePost } }, new())).Count == 1, "Image-only JPEG compression and multimodal payload");
imagePost.Text = "Tuyển gấp, xem ảnh";
Check((await vision.ExtractAsync(new() { Posts = new() { imagePost } }, new())).Count == 1, "Short caption and banner payload");
imagePost.ImageUrls[0] = "https://localhost/private";
await Fails(() => vision.ExtractAsync(new() { Posts = new() { imagePost } }, new()), "Unsafe image URL fails before download");
Console.WriteLine($"PASS: {checks} Facebook flow checks.");

sealed class ApifyHandler(string dataset, string finalStatus = "SUCCEEDED", string expectedUrl = FacebookGroupScraper.GroupUrl) : HttpMessageHandler
{
    public int Requests;
    public int Runs;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Requests++;
        if (request.Headers.Authorization?.ToString() != "Bearer test-token" || request.RequestUri!.Query.Contains("token="))
            throw new Exception("Incorrect token transport");
        string json;
        if (request.Method == HttpMethod.Post)
        {
            Runs++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            if (!request.RequestUri.AbsolutePath.Contains(FacebookGroupScraper.ActorId) ||
                body.RootElement.GetProperty("startUrls")[0].GetProperty("url").GetString() != expectedUrl ||
                body.RootElement.GetProperty("searchGroupKeyword").GetString() != "phụ bếp" ||
                body.RootElement.GetProperty("resultsLimit").GetInt32() != 3 ||
                body.RootElement.GetProperty("viewOption").GetString() != "CHRONOLOGICAL") throw new Exception("Wrong actor input");
            json = "{\"data\":{\"id\":\"run1\",\"defaultDatasetId\":\"ds1\",\"status\":\"RUNNING\"}}";
        }
        else if (request.RequestUri.AbsolutePath.Contains("actor-runs"))
            json = JsonSerializer.Serialize(new { data = new { id = "run1", defaultDatasetId = "ds1", status = finalStatus } });
        else json = dataset;
        return new(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}

sealed class FakeBedrock() : AmazonBedrockRuntimeClient(new AnonymousAWSCredentials(), RegionEndpoint.APSoutheast1)
{
    public int Calls;
    public bool ExpectImage;
    public string Output = "[{\"Title\":\"Nhân viên\",\"Platform\":\"wrong\",\"ExternalId\":\"invented\"}]";
    public override Task<InvokeModelResponse> InvokeModelAsync(InvokeModelRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        using var doc = JsonDocument.Parse(request.Body);
        if (doc.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("type").GetString() != (ExpectImage ? "image" : "text"))
            throw new Exception("Expected text content");
        if (ExpectImage)
        {
            var block = doc.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("source");
            using var image = SixLabors.ImageSharp.Image.Load(Convert.FromBase64String(block.GetProperty("data").GetString()!));
            if (block.GetProperty("media_type").GetString() != "image/jpeg" || image.Width > 1024 || image.Height > 1024)
                throw new Exception("Invalid compressed image");
        }
        return Task.FromResult(new InvokeModelResponse { Body = new MemoryStream(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { stop_reason = "end_turn", content = new[] { new { type = "text", text = Output } } }))) });
    }
}

sealed class ImageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(1500, 1100);
        using var buffer = new MemoryStream();
        image.Save(buffer, new SixLabors.ImageSharp.Formats.Png.PngEncoder());
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) });
    }
}

