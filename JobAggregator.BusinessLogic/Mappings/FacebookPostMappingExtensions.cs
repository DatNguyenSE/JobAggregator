using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using JobAggregator.BusinessLogic.DTOs;

namespace JobAggregator.BusinessLogic.Mappings;

public static class FacebookPostMappingExtensions
{
    public static FacebookRawPost? ToFacebookPost(this JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid Facebook dataset item.");
        if (item.TryGetProperty("error", out _))
            throw new InvalidOperationException("Apify could not read the public Facebook group.");
        var url = Read(item, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !(uri.Host == "www.facebook.com" || uri.Host == "facebook.com")) return null;
        var match = Regex.Match(uri.AbsolutePath, @"/groups/[^/]+/(?:posts|permalink)/(\d+)");
        if (!match.Success) return null; // Never ingest group landing pages or comments as jobs.
        var id = Read(item, "legacyId");
        if (string.IsNullOrWhiteSpace(id)) id = match.Groups[1].Value;
        var images = new List<string>();
        // Inspect only post attachments, never author avatars or comment images.
        foreach (var key in new[] { "media", "attachments", "images" })
            if (item.TryGetProperty(key, out var media)) CollectImages(media, images);
        var time = Read(item, "time");
        if (!DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var posted))
            throw new JsonException("Facebook post is missing a valid posting time.");
        return new FacebookRawPost
        {
            ExternalId = id,
            SourceUrl = uri.GetLeftPart(UriPartial.Path),
            Text = Read(item, "text").Trim(),
            PostedDate = posted.UtcDateTime.ToString("O"),
            ImageUrls = images.Distinct().ToList()
        };
    }

    public static bool IsFacebookImageUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".fbsbx.com", StringComparison.OrdinalIgnoreCase));

    private static string Read(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String or JsonValueKind.Number
            ? value.ToString() : "";

    private static void CollectImages(JsonElement node, List<string> images)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in node.EnumerateArray()) CollectImages(child, images);
        }
        else if (node.ValueKind == JsonValueKind.Object)
        {
            if (Read(node, "__typename").Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                Read(node, "type").Contains("video", StringComparison.OrdinalIgnoreCase)) return;
            foreach (var property in node.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Name is "uri" or "url" or "imageUrl" or "thumbnail" &&
                    IsFacebookImageUrl(property.Value.GetString()!)) images.Add(property.Value.GetString()!);
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    CollectImages(property.Value, images);
            }
        }
        else if (node.ValueKind == JsonValueKind.String && IsFacebookImageUrl(node.GetString()!))
            images.Add(node.GetString()!);
    }
}
