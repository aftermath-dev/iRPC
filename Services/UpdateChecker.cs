using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace iRPC;

public static class UpdateChecker
{
    private static readonly HttpClient _http = new();
    private const string ApiUrl     = "https://api.github.com/repos/Mathues-Studios/iRPC/releases/latest";
    private const string ReleasesUrl = "https://github.com/Mathues-Studios/iRPC/releases/latest";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<UpdateResult> CheckAsync()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("iRPC");
        string json = await _http.GetStringAsync(ApiUrl);
        using var doc = JsonDocument.Parse(json);

        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = doc.RootElement.GetProperty("html_url").GetString() ?? ReleasesUrl;

        string versionPart = tag.TrimStart('v').Split('-')[0];
        if (!Version.TryParse(versionPart, out var latest))
            return new UpdateResult(false, tag, url);

        return new UpdateResult(latest > CurrentVersion, tag, url);
    }
}

public record UpdateResult(bool HasUpdate, string LatestTag, string ReleaseUrl);
