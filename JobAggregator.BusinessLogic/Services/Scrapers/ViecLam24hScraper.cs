using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Linq;
using JobAggregator.BusinessLogic.DTOs;

namespace JobAggregator.BusinessLogic.Services.Scrapers
{
    public class ViecLam24hScraper : IJobScraperStrategy
    {
        // Mapping: tên tỉnh/thành phố (lowercase, no accent) → slug URL
        private static readonly Dictionary<string, string> ProvinceSlugMap = new(StringComparer.OrdinalIgnoreCase)
        {
            {"ha noi", "ha-noi"}, {"hà nội", "ha-noi"}, {"hanoi", "ha-noi"},
            {"tp hcm", "tp-hcm"}, {"hcm", "tp-hcm"}, {"ho chi minh", "tp-hcm"}, {"hồ chí minh", "tp-hcm"}, {"tp.hcm", "tp-hcm"}, {"tp ho chi minh", "tp-hcm"}, {"sai gon", "tp-hcm"}, {"sài gòn", "tp-hcm"},
            {"an giang", "an-giang"},
            {"ba ria vung tau", "ba-ria-vung-tau"}, {"bà rịa vũng tàu", "ba-ria-vung-tau"}, {"vung tau", "ba-ria-vung-tau"}, {"vũng tàu", "ba-ria-vung-tau"},
            {"bac giang", "bac-giang"}, {"bắc giang", "bac-giang"},
            {"bac kan", "bac-kan"}, {"bắc kạn", "bac-kan"},
            {"bac lieu", "bac-lieu"}, {"bạc liêu", "bac-lieu"},
            {"bac ninh", "bac-ninh"}, {"bắc ninh", "bac-ninh"},
            {"ben tre", "ben-tre"}, {"bến tre", "ben-tre"},
            {"binh duong", "binh-duong"}, {"bình dương", "binh-duong"},
            {"binh phuoc", "binh-phuoc"}, {"bình phước", "binh-phuoc"},
            {"binh thuan", "binh-thuan"}, {"bình thuận", "binh-thuan"},
            {"binh dinh", "binh-dinh"}, {"bình định", "binh-dinh"},
            {"ca mau", "ca-mau"}, {"cà mau", "ca-mau"},
            {"can tho", "can-tho"}, {"cần thơ", "can-tho"},
            {"cao bang", "cao-bang"}, {"cao bằng", "cao-bang"},
            {"gia lai", "gia-lai"},
            {"ha giang", "ha-giang"}, {"hà giang", "ha-giang"},
            {"ha nam", "ha-nam"}, {"hà nam", "ha-nam"},
            {"ha tinh", "ha-tinh"}, {"hà tĩnh", "ha-tinh"},
            {"hai duong", "hai-duong"}, {"hải dương", "hai-duong"},
            {"hai phong", "hai-phong"}, {"hải phòng", "hai-phong"},
            {"hau giang", "hau-giang"}, {"hậu giang", "hau-giang"},
            {"hoa binh", "hoa-binh"}, {"hòa bình", "hoa-binh"},
            {"hung yen", "hung-yen"}, {"hưng yên", "hung-yen"},
            {"khanh hoa", "khanh-hoa"}, {"khánh hòa", "khanh-hoa"},
            {"kien giang", "kien-giang"}, {"kiên giang", "kien-giang"},
            {"kon tum", "kon-tum"},
            {"lai chau", "lai-chau"}, {"lai châu", "lai-chau"},
            {"lam dong", "lam-dong"}, {"lâm đồng", "lam-dong"},
            {"lang son", "lang-son"}, {"lạng sơn", "lang-son"},
            {"lao cai", "lao-cai"}, {"lào cai", "lao-cai"},
            {"long an", "long-an"},
            {"nam dinh", "nam-dinh"}, {"nam định", "nam-dinh"},
            {"nghe an", "nghe-an"}, {"nghệ an", "nghe-an"},
            {"ninh binh", "ninh-binh"}, {"ninh bình", "ninh-binh"},
            {"ninh thuan", "ninh-thuan"}, {"ninh thuận", "ninh-thuan"},
            {"phu tho", "phu-tho"}, {"phú thọ", "phu-tho"},
            {"phu yen", "phu-yen"}, {"phú yên", "phu-yen"},
            {"quang binh", "quang-binh"}, {"quảng bình", "quang-binh"},
            {"quang nam", "quang-nam"}, {"quảng nam", "quang-nam"},
            {"quang ngai", "quang-ngai"}, {"quảng ngãi", "quang-ngai"},
            {"quang ninh", "quang-ninh"}, {"quảng ninh", "quang-ninh"},
            {"quang tri", "quang-tri"}, {"quảng trị", "quang-tri"},
            {"soc trang", "soc-trang"}, {"sóc trăng", "soc-trang"},
            {"son la", "son-la"}, {"sơn la", "son-la"},
            {"tay ninh", "tay-ninh"}, {"tây ninh", "tay-ninh"},
            {"thai binh", "thai-binh"}, {"thái bình", "thai-binh"},
            {"thai nguyen", "thai-nguyen"}, {"thái nguyên", "thai-nguyen"},
            {"thanh hoa", "thanh-hoa"}, {"thanh hóa", "thanh-hoa"},
            {"thua thien hue", "thua-thien-hue"}, {"thừa thiên huế", "thua-thien-hue"}, {"hue", "thua-thien-hue"}, {"huế", "thua-thien-hue"},
            {"tien giang", "tien-giang"}, {"tiền giang", "tien-giang"},
            {"tra vinh", "tra-vinh"}, {"trà vinh", "tra-vinh"},
            {"tuyen quang", "tuyen-quang"}, {"tuyên quang", "tuyen-quang"},
            {"vinh long", "vinh-long"}, {"vĩnh long", "vinh-long"},
            {"vinh phuc", "vinh-phuc"}, {"vĩnh phúc", "vinh-phuc"},
            {"yen bai", "yen-bai"}, {"yên bái", "yen-bai"},
            {"da nang", "da-nang"}, {"đà nẵng", "da-nang"},
            {"dak lak", "dak-lak"}, {"đắk lắk", "dak-lak"}, {"dac lac", "dak-lak"},
            {"dak nong", "dak-nong"}, {"đắk nông", "dak-nong"},
            {"dien bien", "dien-bien"}, {"điện biên", "dien-bien"},
            {"dong nai", "dong-nai"}, {"đồng nai", "dong-nai"},
            {"dong thap", "dong-thap"}, {"đồng tháp", "dong-thap"},
        };

        private static (int provinceId, string districtUrlPath) FindLocationInfo(string location)
        {
            if (string.IsNullOrWhiteSpace(location)) return (0, "");

            // location có thể là:
            //   "An Giang"               -> chỉ tỉnh -> dùng /viec-lam-an-giang-p129.html
            //   "Châu Đốc, An Giang"     -> quận, tỉnh -> dùng /viec-lam-thi-xa-chau-doc-an-giang-d2.html

            var parts = location.Split(',');
            string provincePart = parts.Length >= 2 ? parts[1].Trim() : parts[0].Trim();
            string districtPart = parts.Length >= 2 ? parts[0].Trim() : "";

            // Tìm province ID
            int provinceId = 0;
            if (LocationIdLookup.ProvinceNameToId.TryGetValue(provincePart, out var pid))
                provinceId = pid;
            else
            {
                foreach (var kv in LocationIdLookup.ProvinceNameToId)
                {
                    if (provincePart.Contains(kv.Key, System.StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Contains(provincePart, System.StringComparison.OrdinalIgnoreCase))
                    { provinceId = kv.Value; break; }
                }
            }

            // A district name can belong to several provinces. Match both parts.
            string districtUrlPath = "";
            if (!string.IsNullOrEmpty(districtPart))
            {
                var provinceSlug = GetProvinceSlug(provinceId);
                var districtName = Regex.Replace(districtPart, @"^(?:Thành phố|Thị xã|Huyện|Quận)\s+", "", RegexOptions.IgnoreCase);
                districtUrlPath = LocationIdLookup.DistrictNameToUrlPath[districtPart]
                    .Concat(LocationIdLookup.DistrictNameToUrlPath[districtName])
                    .FirstOrDefault(path => Regex.IsMatch(path, $@"-{Regex.Escape(provinceSlug)}-d\d+\.html$")) ?? "";
                if (string.IsNullOrEmpty(districtUrlPath))
                    throw new ArgumentException($"Không xác định được quận/huyện '{districtPart}' trong tỉnh '{provincePart}'.");
            }
            if (provinceId == 0)
                throw new ArgumentException($"Không xác định được tỉnh/thành phố '{provincePart}'.");
            return (provinceId, districtUrlPath);
        }

        private static string GetProvinceSlug(int provinceId)
        {
            var name = LocationIdLookup.ProvinceNameToId.First(entry => entry.Value == provinceId).Key;
            if (ProvinceSlugMap.TryGetValue(name, out var slug)) return slug;
            var normalized = name.Normalize(System.Text.NormalizationForm.FormD);
            var plain = new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
            return Regex.Replace(plain.ToLowerInvariant().Replace('đ', 'd'), @"[^a-z0-9]+", "-").Trim('-');
        }
        // BƯỚC 1: Tải HTML và đọc mảng items trong __NEXT_DATA__; yield từng bài cho hàm cào.
        private static async IAsyncEnumerable<JsonElement> ReadSearchItemsAsync(HttpClient client, string targetUrl)
        {
            var seen = new HashSet<string>();
            // Stop on empty/repeated pages; cap work to fit the Lambda execution budget.
            for (int page = 1; page <= 5; page++)
            {
                var html = await client.GetStringAsync(page == 1 ? targetUrl : $"{targetUrl}&page={page}");
                var match = Regex.Match(html, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.+?)</script>", RegexOptions.Singleline);
                if (!match.Success) throw new InvalidOperationException("ViecLam24h thiếu __NEXT_DATA__.");
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                if (!doc.RootElement.TryGetProperty("props", out var props) ||
                    !props.TryGetProperty("initialState", out var state) || !state.TryGetProperty("api", out var api) ||
                    !api.TryGetProperty("getJobList", out var list) || !list.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("ViecLam24h thay đổi cấu trúc danh sách công việc.");
                var added = 0;
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("id", out var id) || !seen.Add(id.ToString())) continue;
                    added++;
                    yield return item.Clone();
                }
                if (added == 0) yield break;
            }
        }

        // BƯỚC 2: Tạo URL theo tỉnh/quận, đọc từng bài và chuẩn hóa thành JobPostDto.
        public async Task<string> ScrapeJobsAsJsonAsync(SearchCriteriaDto criteria, int maxJobs)
        {
            var keyword = criteria.Keyword ?? "";
            var location = criteria.Location ?? "";
            Console.WriteLine($"[ViecLam24hScraper] Bắt đầu cào trực tiếp cho từ khóa: {keyword}, địa điểm: {location}");
            
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                
                // Xây URL đúng cấu trúc ViecLam24h
                string targetUrl;
                var (provinceId, districtUrlPath) = FindLocationInfo(location);

                if (!string.IsNullOrEmpty(districtUrlPath))
                {
                    // Có quận/huyện: /viec-lam-thi-xa-chau-doc-an-giang-d2.html?q=sale
                    targetUrl = $"https://vieclam24h.vn/{districtUrlPath}?q={Uri.EscapeDataString(keyword)}";
                    Console.WriteLine($"[ViecLam24hScraper] Cào theo quận/huyện: {targetUrl}");
                }
                else if (provinceId > 0)
                {
                    // Chỉ có tỉnh: /viec-lam-an-giang-p129.html?q=sale
                    targetUrl = $"https://vieclam24h.vn/viec-lam-{GetProvinceSlug(provinceId)}-p{provinceId}.html?q={Uri.EscapeDataString(keyword)}";
                    Console.WriteLine($"[ViecLam24hScraper] Cào theo tỉnh ID {provinceId}: {targetUrl}");
                }
                else
                {
                    // Không có địa điểm
                    targetUrl = $"https://vieclam24h.vn/tim-kiem-viec-lam-nhanh?q={Uri.EscapeDataString(keyword)}";
                    Console.WriteLine($"[ViecLam24hScraper] Cào toàn quốc: {targetUrl}");
                }

                        var jobs = new List<JobPostDto>();
                        int count = 0;
                        
                        await foreach (var item in ReadSearchItemsAsync(client, targetUrl))
                        {
                            if (count >= maxJobs) break;
                            var externalId = "vieclam24h_" + item.GetProperty("id").GetRawText();
                            if (criteria.ExcludedExternalIds.Contains(externalId)) continue;
                            
                            string title = item.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
                            string employer = item.TryGetProperty("employer_info", out var emp) && emp.TryGetProperty("name", out var empName) ? (empName.GetString() ?? "") : "";
                            
                            string minSalary = item.TryGetProperty("salary_min", out var min) ? min.GetRawText() : "";
                            string maxSalary = item.TryGetProperty("salary_max", out var max) ? max.GetRawText() : "";
                            string salaryInfo = $"{minSalary} - {maxSalary} VNĐ";
                            
                            string fallbackAddress = item.TryGetProperty("contact_address", out var addr) ? (addr.GetString() ?? "") : "";
                            var parsedLocations = new List<JobLocationDto>();
                            
                            // 1. Trích xuất danh sách địa điểm (nếu có nhiều nơi làm việc)
                            if (item.TryGetProperty("places", out var placesProp) && (placesProp.ValueKind == JsonValueKind.String || placesProp.ValueKind == JsonValueKind.Array))
                            {
                                try 
                                {
                                    using var placesDoc = JsonDocument.Parse(placesProp.ValueKind == JsonValueKind.String ? placesProp.GetString() ?? "[]" : placesProp.GetRawText());
                                    foreach (var place in placesDoc.RootElement.EnumerateArray())
                                    {
                                        string? provName = null;
                                        string? distName = null;
                                        string? exactAddr = null;
                                        
                                        if (place.TryGetProperty("province_id", out var provIdProp) && int.TryParse(provIdProp.ToString(), out int pId))
                                        {
                                            if (LocationDictionary.Provinces.TryGetValue(pId, out var pName)) provName = pName;
                                        }
                                        if (place.TryGetProperty("district_id", out var distIdProp) && int.TryParse(distIdProp.ToString(), out int dId))
                                        {
                                            if (LocationDictionary.Districts.TryGetValue(dId, out var dName)) distName = dName;
                                        }
                                        if (place.TryGetProperty("address", out var placeAddr))
                                        {
                                            exactAddr = placeAddr.GetString();
                                        }
                                        
                                        if (provName != null || exactAddr != null)
                                        {
                                            parsedLocations.Add(new JobLocationDto 
                                            {
                                                Province = provName,
                                                District = distName,
                                                ExactAddress = exactAddr
                                            });
                                        }
                                    }
                                } 
                                catch { }
                            }
                            
                            // Nếu API không trả về mảng places hợp lệ, dùng địa chỉ gộp
                            if (parsedLocations.Count == 0 && !string.IsNullOrWhiteSpace(fallbackAddress))
                            {
                                parsedLocations.Add(new JobLocationDto { ExactAddress = fallbackAddress });
                            }

                            // 2. Trích xuất Ngày Đăng (published_at, approved_at, hoặc refresh_at)
                            string postedDateStr = "";
                            if (item.TryGetProperty("published_at", out var publishedAt) && publishedAt.ValueKind == JsonValueKind.Number && publishedAt.TryGetInt64(out long unixTimePub))
                            {
                                var dt = DateTimeOffset.FromUnixTimeSeconds(unixTimePub).UtcDateTime;
                                postedDateStr = dt.ToString("yyyy-MM-dd");
                            }
                            else if (item.TryGetProperty("approved_at", out var approvedAt) && approvedAt.ValueKind == JsonValueKind.Number && approvedAt.TryGetInt64(out long unixTimeApp))
                            {
                                var dt = DateTimeOffset.FromUnixTimeSeconds(unixTimeApp).UtcDateTime;
                                postedDateStr = dt.ToString("yyyy-MM-dd");
                            }
                            else if (item.TryGetProperty("refresh_at", out var refreshAt) && refreshAt.ValueKind == JsonValueKind.Number && refreshAt.TryGetInt64(out long unixTime))
                            {
                                var dt = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime;
                                postedDateStr = dt.ToString("yyyy-MM-dd");
                            }

                            // 3. Trích xuất Yêu cầu công việc (từ other_requirement hoặc job_requirement)
                            string rawRequirement = "";
                            if (item.TryGetProperty("other_requirement", out var otherReq) && otherReq.ValueKind == JsonValueKind.String)
                            {
                                rawRequirement = otherReq.GetString() ?? "";
                            }
                            if (string.IsNullOrWhiteSpace(rawRequirement) && item.TryGetProperty("job_requirement", out var jobReq) && jobReq.ValueKind == JsonValueKind.String)
                            {
                                rawRequirement = jobReq.GetString() ?? "";
                            }
                            
                            // Loại bỏ thẻ HTML (nếu có)
                            string cleanRequirement = System.Text.RegularExpressions.Regex.Replace(rawRequirement, "<.*?>", string.Empty).Trim();
                            cleanRequirement = System.Text.RegularExpressions.Regex.Replace(cleanRequirement, "&nbsp;", " ");

                            string urlSlug = item.TryGetProperty("title_slug", out var slug) ? (slug.GetString() ?? "") : "";
                            string id = item.TryGetProperty("id", out var idProp) ? idProp.GetRawText() : "";
                            
                            string catId = "1";
                            if (item.TryGetProperty("occupation_ids_main", out var catArray) && catArray.GetArrayLength() > 0) {
                                catId = catArray[0].GetRawText();
                            }
                            string provId = "1";
                            if (item.TryGetProperty("province_ids", out var provArray) && provArray.GetArrayLength() > 0) {
                                provId = provArray[0].GetRawText();
                            }
                            
                            string sourceUrl = $"https://vieclam24h.vn/viec-lam/{urlSlug}-c{catId}p{provId}id{id}.html";
                            
                            // --- THÔNG TIN CHUNG ---
                            string genderStr = "Không yêu cầu";
                            if (item.TryGetProperty("gender", out var genderProp)) {
                                if (genderProp.GetRawText() == "1") genderStr = "Nam";
                                else if (genderProp.GetRawText() == "2") genderStr = "Nữ";
                            }

                            int? vacancyInt = null;
                            if (item.TryGetProperty("vacancy_quantity", out var vacProp) && vacProp.ValueKind == JsonValueKind.Number && vacProp.TryGetInt32(out int v)) {
                                vacancyInt = v;
                            }

                            string formatStr = "Toàn thời gian";
                            if (item.TryGetProperty("working_method", out var wmProp)) {
                                if (wmProp.GetRawText() == "2") formatStr = "Bán thời gian";
                                else if (wmProp.GetRawText() == "3") formatStr = "Thực tập";
                            }

                            string ageStr = "Không yêu cầu";
                            if (item.TryGetProperty("age_range", out var ageProp)) {
                                if (ageProp.GetRawText() != "0") ageStr = "Có giới hạn độ tuổi"; // Có thể fetch chi tiết để lấy đoạn "22 - 40 tuổi"
                            }

                            string expStr = "Không yêu cầu";
                            if (item.TryGetProperty("experience_range", out var expProp)) {
                                string expCode = expProp.GetRawText();
                                if (expCode == "1") expStr = "Chưa có kinh nghiệm";
                                else if (expCode == "2") expStr = "Dưới 1 năm";
                                else if (expCode == "3") expStr = "1 năm";
                                else if (expCode == "4") expStr = "2 năm";
                                else if (expCode == "5") expStr = "3 năm";
                                else if (expCode == "6") expStr = "4 năm";
                                else if (expCode == "7") expStr = "5 năm";
                            }

                            // -- FETCH DETAIL PAGE FOR DESCRIPTION AND FULL ADDRESS --
                            string jobInfoStr = "";
                            try
                            {
                                var detailHtml = await client.GetStringAsync(sourceUrl);
                                var detailMatch = System.Text.RegularExpressions.Regex.Match(detailHtml, @"<script id=""__NEXT_DATA__"" type=""application/json"">(.+?)</script>", System.Text.RegularExpressions.RegexOptions.Singleline);
                                if (detailMatch.Success)
                                {
                                    var detailJson = detailMatch.Groups[1].Value;
                                    using (var detailDoc = JsonDocument.Parse(detailJson))
                                    {
                                        if (detailDoc.RootElement.TryGetProperty("props", out var dProps) &&
                                            dProps.TryGetProperty("initialState", out var dInitialState) &&
                                            dInitialState.TryGetProperty("api", out var dApi))
                                        {
                                            JsonElement dData = default;
                                            bool found = false;
                                            
                                            // ViecLam24h stores job detail in either jobDetailHiddenContact or getJobDetail
                                            if (dApi.TryGetProperty("jobDetailHiddenContact", out var dJobHidden) && dJobHidden.TryGetProperty("data", out dData))
                                            {
                                                found = true;
                                            }
                                            else if (dApi.TryGetProperty("getJobDetail", out var dGetJob) && dGetJob.TryGetProperty("data", out dData))
                                            {
                                                found = true;
                                            }

                                            if (found)
                                            {
                                                if (dData.TryGetProperty("description", out var desc))
                                                {
                                                    jobInfoStr = System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(desc.GetString() ?? "", "<.*?>", String.Empty)).Trim();
                                                }
                                                // Override specific address if a fuller one is available in detail page
                                                if (dData.TryGetProperty("contact_address", out var detailAddress) && !string.IsNullOrEmpty(detailAddress.GetString()))
                                                {
                                                    if (parsedLocations.Count == 1 && parsedLocations[0].Province == null) 
                                                    {
                                                        parsedLocations[0].ExactAddress = detailAddress.GetString();
                                                    }
                                                }
                                                // Extract age_range if present
                                                if (dData.TryGetProperty("age_range", out var dAge) && dAge.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(dAge.GetString())) {
                                                    ageStr = dAge.GetString();
                                                }
                                            }
                                        }
                                    }
                                }
                            } catch { }

                            var job = new JobPostDto
                            {
                                Title = title,
                                EmployerName = employer,
                                SalaryInfo = salaryInfo,
                                Locations = parsedLocations,
                                Requirements = cleanRequirement,
                                Location = string.Join("; ", parsedLocations.Select(l => string.Join(", ", new[] { l.District, l.Province }.Where(v => !string.IsNullOrWhiteSpace(v))))),
                                Platform = "ViecLam24h",
                                ExternalId = $"vieclam24h_{id}",
                                SourceUrl = sourceUrl,
                                PostedDate = postedDateStr,
                                JobInfo = jobInfoStr,
                                Gender = genderStr,
                                Vacancies = vacancyInt,
                                WorkingFormat = formatStr,
                                AgeRequirement = ageStr,
                                Experience = expStr
                            };
                            var candidate = JobAggregator.BusinessLogic.Mappings.JobPostMappingExtensions.ToEntity(job, Guid.Empty);
                            candidate.SearchKeyword = new JobAggregator.DataAccess.Entities.SearchKeyword { Keyword = criteria.Keyword };
                            if (!JobAggregator.DataAccess.Repositories.JobSearchQuery.Apply(new[] { candidate }.AsQueryable(),
                                criteria.Keyword, criteria.Location, criteria.JobType, null, null).Any()) continue;
                            jobs.Add(job);

                            count++;
                        }
                        
                        // Đóng gói danh sách DTO thành chuỗi JSON để gửi qua SQS; chưa ghi database.
                        return JsonSerializer.Serialize(jobs);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ViecLam24hScraper] Lỗi: {ex}");
                throw;
            }

        }
    }
}
