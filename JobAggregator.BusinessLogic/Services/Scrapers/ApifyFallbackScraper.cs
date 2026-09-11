using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using JobAggregator.BusinessLogic.DTOs;

namespace JobAggregator.BusinessLogic.Services.Scrapers
{
    public class ApifyFallbackScraper : IJobScraperStrategy
    {
        private readonly string _source;
        private readonly string _apifyApiKey;
        
        public ApifyFallbackScraper(string source, string apifyApiKey)
        {
            _source = source;
            _apifyApiKey = apifyApiKey;
        }

        public async Task<string> ScrapeJobsAsJsonAsync(SearchCriteriaDto criteria, int maxJobs)
        {
            var keyword = criteria.Keyword ?? "";
            Console.WriteLine($"[ApifyFallbackScraper] Bắt đầu cào {_source} cho từ khóa: {keyword}");
            
            try
            {
                string targetUrl = "";
                if (_source == "chotot")
                {
                    targetUrl = $"https://www.chotot.com/tags/toan-quoc/viec-lam-{keyword.Replace(" ", "-")}";
                }
                else if (_source == "facebook")
                {
                    targetUrl = "https://www.facebook.com/groups/tuyendungvieclamparttime"; 
                }

                if (string.IsNullOrEmpty(targetUrl)) return "[]";

                object[] crawlGlobs = new object[] { new { glob = "http*://**" } }; 
                string linkSelector = "";
                
                if (_source == "chotot") {
                    crawlGlobs = new[] { new { glob = "https://www.chotot.com/**/*.htm*" } };
                    linkSelector = "a[href*='.htm']";
                } else if (_source == "facebook") {
                    crawlGlobs = new[] { new { glob = "https://www.facebook.com/groups/*/posts/*" }, new { glob = "https://www.facebook.com/groups/*/permalink/*" } };
                    linkSelector = "a[href*='/posts/'], a[href*='/permalink/']";
                }

                var requestBody = new
                {
                    startUrls = new[] { new { url = targetUrl } },
                    maxCrawlPages = maxJobs + 1, 
                    maxCrawlDepth = 1, 
                    saveHtml = false,
                    globs = crawlGlobs,
                    linkSelector = string.IsNullOrEmpty(linkSelector) ? null : linkSelector,
                    dynamicContentWaitSecs = 3 // Cho thời gian tải trang động
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), System.Text.Encoding.UTF8, "application/json");
                
                using var localClient = new HttpClient();
                localClient.Timeout = TimeSpan.FromMinutes(10); 
                
                string actorId = "apify~website-content-crawler"; 
                var runResponse = await localClient.PostAsync($"https://api.apify.com/v2/acts/{actorId}/runs?token={_apifyApiKey}", content);
                
                if (runResponse.IsSuccessStatusCode)
                {
                    var runResponseString = await runResponse.Content.ReadAsStringAsync();
                    using var runDoc = JsonDocument.Parse(runResponseString);
                    
                    if (runDoc.RootElement.TryGetProperty("data", out var dataElement) && 
                        dataElement.TryGetProperty("id", out var runIdElement) &&
                        dataElement.TryGetProperty("defaultDatasetId", out var datasetIdElement))
                    {
                        string runId = runIdElement.GetString();
                        string datasetId = datasetIdElement.GetString();
                        Console.WriteLine($"[Apify] Đã khởi tạo Run ID: {runId}. Bắt đầu chờ kết quả...");

                        bool isFinished = false;
                        int retryCount = 0;
                        int maxRetries = 60; 

                        while (!isFinished && retryCount < maxRetries)
                        {
                            await Task.Delay(5000); 
                            retryCount++;

                            var statusResponse = await localClient.GetAsync($"https://api.apify.com/v2/actor-runs/{runId}?token={_apifyApiKey}");
                            if (statusResponse.IsSuccessStatusCode)
                            {
                                var statusString = await statusResponse.Content.ReadAsStringAsync();
                                using var statusDoc = JsonDocument.Parse(statusString);
                                
                                if (statusDoc.RootElement.TryGetProperty("data", out var statusData) &&
                                    statusData.TryGetProperty("status", out var statusElement))
                                {
                                    string status = statusElement.GetString();
                                    Console.WriteLine($"[Apify] Run {runId} Status: {status} (Attempt {retryCount}/{maxRetries})");
                                    
                                    if (status == "SUCCEEDED")
                                    {
                                        isFinished = true;
                                        var datasetResponse = await localClient.GetAsync($"https://api.apify.com/v2/datasets/{datasetId}/items?token={_apifyApiKey}");
                                        if (datasetResponse.IsSuccessStatusCode)
                                        {
                                            string json = await datasetResponse.Content.ReadAsStringAsync();
                                            Console.WriteLine($"[Apify] Đã lấy thành công dataset {datasetId}");
                                            return json;
                                        }
                                    }
                                    else if (status == "FAILED" || status == "ABORTED" || status == "TIMED-OUT")
                                    {
                                        Console.WriteLine($"[Apify Lỗi] Actor chạy thất bại với trạng thái: {status}");
                                        isFinished = true;
                                    }
                                }
                            }
                        }
                    }
                }
                else 
                {
                    Console.WriteLine($"[Apify Lỗi] HTTP {runResponse.StatusCode} - {await runResponse.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ApifyFallbackScraper] Lỗi: {ex.Message}");
            }
            return "[]";
        }
    }
}
