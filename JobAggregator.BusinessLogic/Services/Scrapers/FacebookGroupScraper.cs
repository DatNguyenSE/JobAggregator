using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;

namespace JobAggregator.BusinessLogic.Services.Scrapers;

public sealed class FacebookGroupScraper : IJobScraperStrategy
{
    public const string GroupUrl = "https://www.facebook.com/groups/sinhvientimvieclamthem/";
    public const string ActorId = "apify~facebook-groups-scraper";
    private readonly HttpClient _client;
    private readonly string _token;
    private readonly Func<string, Task>? _onRunStarted;

    public FacebookGroupScraper(HttpClient client, string token, Func<string, Task>? onRunStarted = null)
    {
        _client = client;
        _onRunStarted = onRunStarted;
        _token = !string.IsNullOrWhiteSpace(token) ? token :
            throw new InvalidOperationException("APIFY_API_KEY is required for Facebook scraping.");
    }

    public async Task<string> ScrapeJobsAsJsonAsync(SearchCriteriaDto criteria, int maxJobs)
    {
        if (maxJobs <= 0) throw new ArgumentOutOfRangeException(nameof(maxJobs));
        var input = BuildInput(criteria, maxJobs);
        var limit = Math.Min(maxJobs, 500);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(230));
        var cancellation = deadline.Token;
        using var started = await SendAsync(HttpMethod.Post,
            $"acts/{ActorId}/runs?timeout=180", input, cancellation);
        var run = started.RootElement.GetProperty("data");
        var runId = run.GetProperty("id").GetString() ?? throw new JsonException("Missing Apify run ID.");
        Console.WriteLine($"[FacebookRun] Request={criteria.RequestId}, Run={runId}, Mode={(string.IsNullOrWhiteSpace(criteria.Keyword) ? "group-feed" : "keyword-search")}");
        if (_onRunStarted != null) await _onRunStarted(runId);
        var status = run.GetProperty("status").GetString();
        var datasetId = run.GetProperty("defaultDatasetId").GetString();
        while (status != "SUCCEEDED")
        {
            if (status is "FAILED" or "ABORTED" or "TIMED-OUT")
                throw new InvalidOperationException($"Facebook Apify run {runId} ended with {status}.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellation);
            using var polled = await SendAsync(HttpMethod.Get,
                $"actor-runs/{Uri.EscapeDataString(runId)}?waitForFinish=20", null, cancellation);
            var data = polled.RootElement.GetProperty("data");
            status = data.GetProperty("status").GetString();
            datasetId = data.GetProperty("defaultDatasetId").GetString();
        }
        if (string.IsNullOrWhiteSpace(datasetId)) throw new JsonException("Missing Apify dataset ID.");
        var batch = new FacebookPostBatch();
        var seen = new HashSet<string>(criteria.ExcludedExternalIds ?? Array.Empty<string>());
        // Read pagination explicitly, including Apify error records (do not use clean=true).
        for (var offset = 0; offset < limit;)
        {
            using var dataset = await SendAsync(HttpMethod.Get,
                $"datasets/{Uri.EscapeDataString(datasetId)}/items?format=json&offset={offset}&limit={limit - offset}",
                null, cancellation);
            if (dataset.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid Apify dataset.");
            var count = dataset.RootElement.GetArrayLength();
            if (count == 0) break;
            foreach (var item in dataset.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("error", out var error))
                {
                    var code = error.ValueKind == JsonValueKind.String ? error.GetString() : "";
                    // Do not echo arbitrary upstream descriptions, URLs or credentials into the UI.
                    throw new FacebookSearchException(runId, code == "no_items" ? "no_items" : "source_error");
                }
                var post = item.ToFacebookPost();
                if (post != null && seen.Add(post.ExternalId) &&
                    (!string.IsNullOrWhiteSpace(post.Text) || post.ImageUrls.Count > 0)) batch.Posts.Add(post);
            }
            offset += count;
        }
        return JsonSerializer.Serialize(batch);
    }

    public static Dictionary<string, object> BuildInput(SearchCriteriaDto criteria, int maxJobs)
    {
        if (maxJobs <= 0) throw new ArgumentOutOfRangeException(nameof(maxJobs));
        var keyword = (criteria.Keyword ?? "").Trim();

        var view = criteria.FacebookViewOption ?? "CHRONOLOGICAL";
        if (!new[] { "CHRONOLOGICAL", "RECENT_ACTIVITY", "TOP_POSTS", "CHRONOLOGICAL_LISTINGS" }.Contains(view))
            throw new ArgumentException("Thứ tự sắp xếp bài Facebook không hợp lệ.");
        var input = new Dictionary<string, object>
        {
            ["startUrls"] = new[] { new { url = NormalizeGroupUrl(criteria.FacebookGroupUrl) } },

            ["resultsLimit"] = Math.Min(maxJobs, 500),
            ["viewOption"] = view
        };
        if (keyword.Length > 0) input["searchGroupKeyword"] = keyword;
        if (keyword.Length > 0 && criteria.FacebookSearchYear.HasValue)
        {
            if (criteria.FacebookSearchYear < 2004 || criteria.FacebookSearchYear > DateTime.UtcNow.Year)
                throw new ArgumentException("Năm tìm kiếm Facebook không hợp lệ.");
            input["searchGroupYear"] = criteria.FacebookSearchYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (!string.IsNullOrWhiteSpace(criteria.FacebookOnlyPostsNewerThan))
        {
            if (!DateOnly.TryParseExact(criteria.FacebookOnlyPostsNewerThan, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date))
                throw new ArgumentException("Ngày lọc Facebook phải có dạng yyyy-MM-dd.");
            input["onlyPostsNewerThan"] = date.ToString("yyyy-MM-dd");
        }
        return input;
    }

    public static string NormalizeGroupUrl(string? value)
    {
        if (value == null) return GroupUrl;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 ||
            !(uri.Host == "facebook.com" || uri.Host == "www.facebook.com" || uri.Host == "m.facebook.com") ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/groups/[A-Za-z0-9._-]+/?$"))
            throw new ArgumentException("Vui lòng nhập URL nhóm Facebook hợp lệ (https://www.facebook.com/groups/ten-nhom/).");
        return "https://www.facebook.com" + uri.AbsolutePath.TrimEnd('/') + "/";
    }

    private async Task<JsonDocument> SendAsync(HttpMethod method, string path, object? body,
        CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(method, "https://api.apify.com/v2/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await _client.SendAsync(request, cancellation);
        // Do not log response bodies or credentials. Failures must reach the request error state.
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
}

public sealed class FacebookSearchException : InvalidOperationException
{
    public string UserMessage { get; }
    public FacebookSearchException(string runId, string code)
        : base($"Facebook keyword search failed: Run={runId}, Code={code}. Check the Apify run log.")
    {
        UserMessage = code == "no_items"
            ? "Apify không đọc được kết quả tìm kiếm trong nhóm Facebook (no_items). Lỗi này có thể do Facebook chặn tìm kiếm hoặc giới hạn truy cập; không xác nhận rằng nhóm không có bài phù hợp."
            : "Apify báo lỗi khi đọc kết quả tìm kiếm Facebook. Hãy kiểm tra Log của lượt chạy trên Apify.";
    }
}
