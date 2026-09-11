using JobAggregator.DataAccess.Entities;
using System.Text.RegularExpressions;

namespace JobAggregator.DataAccess.Repositories;

public static class JobSearchQuery
{
    // Gom các cách viết phổ biến của tỉnh/thành để input khớp dữ liệu nguồn.
    public static string[] ProvinceAliases(string province)
    {
        var name = province.Trim().ToLowerInvariant();
        if (new[] { "tp.hcm", "tp hcm", "hcm", "hồ chí minh", "tp hồ chí minh", "thành phố hồ chí minh", "ho chi minh" }.Contains(name))
            return new[] { "tp.hcm", "tp hcm", "hcm", "hồ chí minh", "tp hồ chí minh", "thành phố hồ chí minh", "ho chi minh" };
        return new[] { name, "tỉnh " + name, "thành phố " + name };
    }

    public static IQueryable<JobPost> Apply(IQueryable<JobPost> jobs, string keyword, string location,
        string jobType, DateTime? freshSince, string[]? sources)
    {
        // Dùng chung cho truy vấn DB và kiểm tra candidate vừa được scraper tạo.
        var key = keyword.Trim().ToLowerInvariant();
        // Search terms stored with a job preserve source search semantics (e.g. sale / kinh doanh).
        jobs = jobs.Where(j => j.Title.ToLower().Contains(key) || j.SearchKeyword.Keyword.ToLower() == key);
        // LastScrapedAt xác định độ mới; CreatedAt hỗ trợ record cũ chưa có LastScrapedAt.
        if (freshSince.HasValue) jobs = jobs.Where(j => (j.LastScrapedAt ?? j.CreatedAt) >= freshSince.Value);
        if (sources?.Length > 0)
        {
            var platforms = sources.Select(s => s.ToLowerInvariant()).ToArray();
            jobs = jobs.Where(j => platforms.Contains(j.Platform.ToLower()));
        }
        var parts = (location ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 0 && !parts[^1].Equals("Toàn quốc", StringComparison.OrdinalIgnoreCase))
        {
            var provinces = ProvinceAliases(parts[^1]);
            var district = parts.Length > 1 ? Regex.Replace(parts[0].ToLowerInvariant(), @"^(thành phố|thị xã|huyện|quận)\s+", "") : "";
            var districts = new[] { district, "quận " + district, "huyện " + district, "thị xã " + district, "thành phố " + district };
            // Tỉnh và quận phải khớp trên CÙNG một JobLocation, tránh ghép hai địa chỉ khác nhau.
            jobs = jobs.Where(j => j.Locations.Any(l =>
                provinces.Contains((l.Province ?? "").ToLower()) &&
                (district == "" || districts.Contains((l.District ?? "").ToLower()))) ||
                // Fallback dưới đây chỉ dành cho dữ liệu cũ chưa có Province/District có cấu trúc.
                (!j.Locations.Any(l => l.Province != null && l.Province != "") &&
                 (j.Locations.Any(l => provinces.Any(p => (l.ExactAddress ?? "").ToLower().Contains(p)) &&
                     (district == "" || districts.Skip(1).Any(d => ((l.ExactAddress ?? "").ToLower() + ",").Contains(d + ",") || ((l.ExactAddress ?? "").ToLower() + " ").Contains(d + " ")))) ||
                  provinces.Any(p => (j.Location ?? "").ToLower().Contains(p)) &&
                     (district == "" || districts.Any(d => (j.Location ?? "").ToLower().StartsWith(d + ","))))));
        }
        if (!string.IsNullOrWhiteSpace(jobType))
        {
            var type = jobType.ToLowerInvariant();
            var localized = type == "part-time" ? "bán thời gian" : type == "full-time" ? "toàn thời gian" : type;
            jobs = jobs.Where(j => (j.WorkingFormat ?? "").ToLower() == type || (j.WorkingFormat ?? "").ToLower() == localized);
        }
        return jobs;
    }
}
