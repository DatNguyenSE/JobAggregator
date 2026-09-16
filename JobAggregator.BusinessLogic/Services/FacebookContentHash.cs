using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobAggregator.BusinessLogic.DTOs;
namespace JobAggregator.BusinessLogic.Services;
public static class FacebookContentHash
{
    public static string Compute(FacebookRawPost post)
    {
        // URL paths identify the attachments; CDN signing/tracking query parameters change independently.
        var images = post.ImageUrls.Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath : value).Distinct().OrderBy(x => x).ToArray();
        var text = Regex.Replace(post.Text.Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Version = FacebookJobExtractor.ExtractionVersion, Text = text, Images = images }))));
    }
}
