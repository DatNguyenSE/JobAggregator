using System;
using Microsoft.EntityFrameworkCore;


using System.Text.Json;
using System.Threading.Tasks;
using Amazon.ApiGatewayManagementApi;
using Amazon.ApiGatewayManagementApi.Model;
using JobAggregator.BusinessLogic.DTOs;
using JobAggregator.BusinessLogic.Mappings;
using JobAggregator.DataAccess.Repositories;



using JobAggregator.DataAccess.Entities;

namespace JobAggregator.BusinessLogic.Services
{
    public interface IAiJobProcessorService
    {
        Task ProcessJobDataAsync(string rawJsonData, SearchCriteriaDto criteria, DateTime? scrapedAt = null);
    }

    public class AiJobProcessorService : IAiJobProcessorService
    {
        private readonly IJobRepository _jobRepository;
        private readonly JobAggregator.DataAccess.Data.AppDbContext _db;

        private readonly AmazonApiGatewayManagementApiClient _apiGatewayClient;
        private readonly Amazon.BedrockRuntime.IAmazonBedrockRuntime _bedrockClient;

        public AiJobProcessorService(IJobRepository jobRepository, JobAggregator.DataAccess.Data.AppDbContext db)
        {
            _jobRepository = jobRepository;
            _db = db;

            
            var endpoint = Environment.GetEnvironmentVariable("WEBSOCKET_ENDPOINT") ?? "";
            var config = new AmazonApiGatewayManagementApiConfig { ServiceURL = endpoint };
            _apiGatewayClient = new AmazonApiGatewayManagementApiClient(config);
            _bedrockClient = new Amazon.BedrockRuntime.AmazonBedrockRuntimeClient(Amazon.RegionEndpoint.APSoutheast1);
        }

        // LUỒNG 3: Nhận kết quả của một nguồn, chuẩn hóa thành List<JobPostDto> rồi lưu từng bài.
        public async Task ProcessJobDataAsync(string rawJsonData, SearchCriteriaDto criteria, DateTime? scrapedAt = null)
        {
            Console.WriteLine($"[AiJobProcessor] Nhận được dữ liệu thô dài {rawJsonData?.Length} ký tự.");
            string processedJson = rawJsonData;
            
            // Nhận diện nhanh bằng hình dạng chuỗi và tên field, chưa validate đầy đủ schema/cú pháp.
            // JSON List<JobPostDto> từ Vieclam24h (kể cả []) đi thẳng, không gọi Bedrock.
            // Nếu chuỗi bị nhận diện nhầm nhưng JSON hỏng, Deserialize sẽ lỗi và SQS có thể retry.
            bool isJsonArray = rawJsonData.TrimStart().StartsWith("[");
            bool isValidJobArray = rawJsonData.Trim() == "[]" || isJsonArray && rawJsonData.Contains("\"Title\"") && (rawJsonData.Contains("\"Platform\"") || rawJsonData.Contains("\"SalaryInfo\""));
            

                // Dữ liệu chưa theo format chung: gọi Bedrock để trích xuất thành JSON List<JobPostDto>.
            using var sourceDocument = JsonDocument.Parse(rawJsonData.TrimStart().StartsWith("{") ? rawJsonData : "null");
            if (sourceDocument.RootElement.ValueKind == JsonValueKind.Object &&
                sourceDocument.RootElement.TryGetProperty("Kind", out var kind) && kind.GetString() == "facebook-group-posts")
            {
                var batch = JsonSerializer.Deserialize<FacebookPostBatch>(rawJsonData)!;
                await ProcessFacebookAsync(batch, criteria, scrapedAt ?? DateTime.UtcNow);
                return;
            }
            else if (!isValidJobArray)
            {
                int maxJobs = criteria.MaxJobs > 0 ? criteria.MaxJobs : 5;
                Console.WriteLine($"[Luồng 3] Dữ liệu thô không đúng chuẩn Job. Gọi AWS Bedrock (MaxJobs={maxJobs})...");
                processedJson = await CallBedrockTextOnlyAsync(rawJsonData, maxJobs);
            }

            // Clean up Bedrock response (extract only the JSON array)
            int startIndex = processedJson.IndexOf('[');
            int endIndex = processedJson.LastIndexOf(']');
            if (startIndex >= 0 && endIndex > startIndex)
            {
                processedJson = processedJson.Substring(startIndex, endIndex - startIndex + 1);
            }
            else
            {
                processedJson = "[]"; // Fallback nếu không có mảng JSON
            }

            // JSON chuẩn từ Vieclam24h bỏ qua Bedrock; tại đây đọc lại danh sách DTO để lưu từng bài.
            var jobDtos = JsonSerializer.Deserialize<List<JobPostDto>>(processedJson, new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            });
            
            if (jobDtos != null)
            {
                var run = criteria.RequestId.HasValue ? await _db.JobSearchRequests.FindAsync(criteria.RequestId.Value) : null;
                var receipts = run == null ? new Dictionary<string, string>() :
                    JsonSerializer.Deserialize<Dictionary<string, string>>(run.ProcessingResultsJson) ?? new();
                if (run != null) { run.Status = "processing"; run.FetchedCount = jobDtos.Select(j => j.ExternalId).Distinct().Count(); }
                var ids = jobDtos.Select(j => j.ExternalId).ToArray();
                var existingIds = (await _db.JobPosts.AsNoTracking().Where(j => j.Platform == "vieclam24h" && ids.Contains(j.ExternalId))
                    .Select(j => j.ExternalId).ToListAsync()).ToHashSet();
                // SearchKeyword lưu dấu vết tiêu chí sinh ra lô job; lọc địa điểm thực tế dựa trên JobLocations.
                // Nếu chưa có thì tạo mới; nếu có thì cập nhật metadata sau khi lưu các JobPost.
                var keywordEntity = await _jobRepository.GetSearchKeywordAsync(criteria.Keyword, criteria.Location, criteria.JobType);
                // Nếu chưa có SearchKeyword, tạo mới để liên kết với các JobPost sắp lưu.
                if (keywordEntity == null)
                {
                    keywordEntity = new SearchKeyword
                    {
                        Id = Guid.NewGuid(),
                        Keyword = criteria.Keyword,
                        Location = criteria.Location ?? "",
                        JobType = criteria.JobType ?? "", 
                        LastScrapedAt = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                    };
                    await _jobRepository.AddSearchKeywordAsync(keywordEntity);
                    await _jobRepository.SaveChangesAsync();
                }

                if (jobDtos.Count > 0)
                {
                    // BƯỚC 6: Gộp trùng theo Platform + ExternalId, chuyển mỗi DTO thành entity rồi gọi repository lưu DB.
                    foreach (var jobDto in jobDtos.GroupBy(j => (j.Platform, j.ExternalId)).Select(g => g.Last()))
                    {
                        if (receipts.ContainsKey(jobDto.ExternalId)) continue;
                        var jobEntity = jobDto.ToEntity(keywordEntity.Id);
                        jobEntity.LastScrapedAt = scrapedAt ?? DateTime.UtcNow;
                        await _jobRepository.AddJobPostAsync(jobEntity);
                        receipts[jobDto.ExternalId] = existingIds.Contains(jobDto.ExternalId) ? "updated" : "inserted";
                        if (run != null)
                        {
                            run.ProcessingResultsJson = JsonSerializer.Serialize(receipts);
                            run.InsertedCount = receipts.Count(x => x.Value == "inserted");
                            run.UpdatedCount = receipts.Count(x => x.Value == "updated");
                            await _db.SaveChangesAsync();
                        }
                    }
                    
                    keywordEntity.LastScrapedAt = DateTime.UtcNow;
                    keywordEntity.TotalJobsFound = await JobSearchQuery.Apply(_db.JobPosts, criteria.Keyword,
                        criteria.Location, criteria.JobType, null, criteria.Sources).CountAsync();
                    await _jobRepository.UpdateSearchKeywordAsync(keywordEntity);
                    
                    await _jobRepository.SaveChangesAsync();
                    Console.WriteLine($"[DB Saved] Đã lưu {jobDtos.Count} jobs cho tiêu chí '{criteria.Keyword}' vào DB.");
                }
                else
                {
                    Console.WriteLine($"[Scraping] Không có job nào được tìm thấy cho '{criteria.Keyword}'.");
                }
                
                await _db.SaveChangesAsync();
                var connectionIds = await _jobRepository.GetAllConnectionIdsAsync();
                
                var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                var broadcastMessage = JsonSerializer.Serialize(new
                {
                    Type = "SEARCH_UPDATED",
                    RequestId = criteria.RequestId
                }, options);

                var messageBytes = System.Text.Encoding.UTF8.GetBytes(broadcastMessage);

                foreach (var connId in connectionIds)
                {
                    try
                    {
                        var request = new PostToConnectionRequest
                        {
                            ConnectionId = connId,
                            Data = new System.IO.MemoryStream(messageBytes)
                        };
                        await _apiGatewayClient.PostToConnectionAsync(request);
                    }
                    catch (Amazon.ApiGatewayManagementApi.Model.GoneException)
                    {
                        // User đã tắt trình duyệt nhưng chưa kịp gọi disconnect, xóa rác
                        await _jobRepository.RemoveConnectionAsync(connId);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Broadcast Error] Lỗi khi gửi cho {connId}: {ex.Message}");
                    }
                }
            }
        }

        private async Task ProcessFacebookAsync(FacebookPostBatch batch, SearchCriteriaDto criteria, DateTime observedAt)
        {
            var run = criteria.RequestId.HasValue ? await _db.JobSearchRequests.FindAsync(criteria.RequestId.Value) : null;
            if (run == null) throw new InvalidOperationException("Facebook processing requires a persisted scrape run.");
            var receipts = JsonSerializer.Deserialize<Dictionary<string, string>>(run.ProcessingResultsJson) ?? new();
            var posts = batch.Posts.GroupBy(p => p.ExternalId).Select(g => g.Last()).ToList();
            var ids = posts.Select(p => p.ExternalId).ToArray();
            // Indexed, bounded lookup; never load all historical jobs into memory.
            var existing = await _db.JobPosts.AsNoTracking().Where(j => j.Platform == "facebook" && ids.Contains(j.ExternalId))
                .Select(j => new { j.ExternalId, j.ContentHash, j.LastScrapedAt }).ToDictionaryAsync(j => j.ExternalId);
            var keyword = await _jobRepository.GetSearchKeywordAsync("", "", "");
            if (keyword == null)
            {
                keyword = new SearchKeyword { Id = Guid.NewGuid(), Keyword = "", Location = "", JobType = "", LastScrapedAt = observedAt };
                await _jobRepository.AddSearchKeywordAsync(keyword);
                await _jobRepository.SaveChangesAsync();
            }
            run.Status = "processing"; run.FetchedCount = posts.Count;
            await _db.SaveChangesAsync();
            foreach (var post in posts)
            {
                if (receipts.TryGetValue(post.ExternalId, out var receipt) && receipt != "failed") continue;
                var hash = FacebookContentHash.Compute(post);
                existing.TryGetValue(post.ExternalId, out var old);
                if (old != null && (old.ContentHash == hash || old.LastScrapedAt > observedAt))
                {
                    await _db.JobPosts.Where(j => j.Platform == "facebook" && j.ExternalId == post.ExternalId &&
                        (j.LastScrapedAt == null || j.LastScrapedAt <= observedAt))
                        .ExecuteUpdateAsync(update => update.SetProperty(j => j.LastScrapedAt, observedAt)
                            .SetProperty(j => j.FacebookGroupId, criteria.FacebookGroupId));
                    receipts[post.ExternalId] = "unchanged";
                }
                else
                {
                    List<JobPostDto>? extracted = null;
                    try
                    {
                        extracted = await new FacebookJobExtractor(_bedrockClient).ExtractAsync(
                            new FacebookPostBatch { Posts = new() { post } }, new SearchCriteriaDto { MaxJobs = 1 });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[FacebookExtractionFailed] Run={run.Id}, Post={post.ExternalId}, Type={ex.GetType().Name}");
                        receipts[post.ExternalId] = "failed";
                    }
                    if (extracted != null)
                    {
                        if (extracted.Count == 0) receipts[post.ExternalId] = "rejected";
                        else
                        {
                            var entity = extracted[0].ToEntity(keyword.Id);
                            entity.FacebookGroupId = criteria.FacebookGroupId;
                            entity.ContentHash = hash; entity.LastScrapedAt = observedAt;
                            await _jobRepository.AddJobPostAsync(entity);
                            receipts[post.ExternalId] = old == null ? "inserted" : "updated";
                        }
                    }
                }
                run.ProcessingResultsJson = JsonSerializer.Serialize(receipts);
                run.InsertedCount = receipts.Count(x => x.Value == "inserted");
                run.UpdatedCount = receipts.Count(x => x.Value == "updated");
                run.UnchangedCount = receipts.Count(x => x.Value == "unchanged");
                run.RejectedCount = receipts.Count(x => x.Value == "rejected");
                run.FailedCount = receipts.Count(x => x.Value == "failed");
                await _db.SaveChangesAsync();
            }
        }

        // ------------------ CÁC HÀM TIỆN ÍCH NỘI BỘ ------------------

        // Nhánh Bedrock trích xuất dữ liệu chưa theo cấu trúc công việc chuẩn; Vieclam24h bình thường không gọi hàm này.
        private async Task<string> CallBedrockTextOnlyAsync(string rawData, int maxJobs)
        {
            Console.WriteLine("[Bedrock] Bắt đầu phân tích văn bản...");
            
            try 
            {
                var systemPrompt = $"You are an AI that extracts job information from text. Extract a MAXIMUM of {maxJobs} jobs. Return ONLY a JSON array with objects matching exactly this schema: {{ \"Title\": string, \"SalaryInfo\": string, \"Requirements\": string, \"OtherBenefits\": string, \"Location\": string, \"EmployerName\": string, \"ContactPhone\": string, \"SpecificAddress\": string, \"Platform\": string, \"ExternalId\": string, \"SourceUrl\": string, \"PostedDate\": string }}. IMPORTANT INSTRUCTIONS: 1. For 'SpecificAddress', scan the ENTIRE text and extract ALL DETAILED street addresses available (if there are multiple branches, list them ALL separated by semicolons ';'). DO NOT just extract the city if full street addresses exist. 2. For 'PostedDate', extract the exact posting date and format it STRICTLY as ISO 8601 'YYYY-MM-DD' (e.g. '2026-08-28'). 3. Combine 'Mô tả công việc' and 'Yêu cầu' into 'Requirements'. COPY THE EXACT BULLET POINTS. 4. Extract 'Quyền lợi' into 'OtherBenefits'. COPY EXACT BULLET POINTS. 5. IF NO JOBS FOUND, RETURN []. DO NOT HALLUCINATE.";
                
                System.Text.StringBuilder combinedText = new System.Text.StringBuilder();
                try
                {
                    using var apifyDoc = JsonDocument.Parse(rawData);
                    if (apifyDoc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in apifyDoc.RootElement.EnumerateArray())
                        {
                            if (element.TryGetProperty("text", out var textProp))
                            {
                                string url = element.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";
                                
                                // Bỏ qua các trang danh sách tìm kiếm để tránh nhiễu AI
                                if (url.Contains("tim-kiem") || url.Contains("tags")) continue;

                                string pageText = textProp.GetString() ?? "";
                                // Giới hạn mỗi trang chi tiết khoảng 15,000 ký tự (loại bỏ text rác ở cuối trang)
                                if (pageText.Length > 15000) pageText = pageText.Substring(0, 15000);
                                
                                combinedText.AppendLine($"--- SOURCE URL: {url} ---");
                                combinedText.AppendLine(pageText);
                                combinedText.AppendLine();
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback nếu không phải mảng JSON hợp lệ
                }

                string text = combinedText.Length > 0 ? combinedText.ToString() : rawData;
                
                // Truncate combined text if it's still too large for Claude 3 Haiku context limit
                if (text.Length > 150000) text = text.Substring(0, 150000);


                var requestBody = new
                {
                    anthropic_version = "bedrock-2023-05-31",
                    max_tokens = 2000,
                    system = systemPrompt,
                    messages = new[]
                    {
                        new { role = "user", content = "Extract jobs from the following text:\n\n" + text }
                    },
                    temperature = 0.1
                };

                var request = new Amazon.BedrockRuntime.Model.InvokeModelRequest
                {
                    ModelId = "anthropic.claude-3-haiku-20240307-v1:0",
                    ContentType = "application/json",
                    Accept = "application/json",
                    Body = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(requestBody)))
                };

                var response = await _bedrockClient.InvokeModelAsync(request);
                using var reader = new System.IO.StreamReader(response.Body);
                var responseBody = await reader.ReadToEndAsync();

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("content", out var contentArray) && contentArray.GetArrayLength() > 0)
                {
                    string extractedText = contentArray[0].GetProperty("text").GetString() ?? "[]";
                    Console.WriteLine($"[Bedrock Raw Output] {extractedText}");
                    Console.WriteLine("[Bedrock] Đã phân tích xong!");
                    return extractedText;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bedrock Lỗi] {ex.Message}");
            }
            
            return "[]";
        }

    }
}
