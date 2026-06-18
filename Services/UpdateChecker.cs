using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace iRPC;

public static class UpdateChecker
{
    private static readonly HttpClient _http = new();
    private const string ApiUrl     = "https://api.github.com/repos/aftermath-dev/iRPC/releases/latest";
    private const string ReleasesUrl = "https://github.com/aftermath-dev/iRPC/releases/latest";

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public static async Task<UpdateResult> CheckAsync()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("iRPC");
        string json = await _http.GetStringAsync(ApiUrl);
        using var doc = JsonDocument.Parse(json);

        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        string url = doc.RootElement.GetProperty("html_url").GetString() ?? ReleasesUrl;

        string? assetUrl = null, assetName = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("iRPC-", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name;
                    assetUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }

        string versionPart = tag.TrimStart('v').Split('-')[0];
        if (!Version.TryParse(versionPart, out var latest))
            return new UpdateResult(false, tag, url, assetUrl, assetName);

        return new UpdateResult(latest > CurrentVersion, tag, url, assetUrl, assetName);
    }

    // Downloads the release exe and writes a helper script that waits for this process
    // to exit, swaps it into place, and relaunches it — Windows won't let us overwrite
    // our own running executable directly.
    public static async Task DownloadAndPrepareRestartAsync(UpdateResult result)
    {
        if (result.AssetUrl is null || result.AssetName is null)
            throw new InvalidOperationException("No downloadable asset found for this release.");

        string currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable path.");

        string tempDir = Path.Combine(Path.GetTempPath(), "iRPC-update");
        Directory.CreateDirectory(tempDir);
        string newExePath = Path.Combine(tempDir, result.AssetName);

        using (var response = await _http.GetAsync(result.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var fs = File.Create(newExePath);
            await response.Content.CopyToAsync(fs);
        }

        string scriptPath = Path.Combine(tempDir, "update.bat");
        string script =
            $"""
            @echo off
            :waitloop
            tasklist /FI "PID eq {Environment.ProcessId}" | find "{Environment.ProcessId}" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto waitloop
            )
            move /Y "{newExePath}" "{currentExePath}"
            start "" "{currentExePath}"
            (goto) 2>nul & del "%~f0"
            """;
        await File.WriteAllTextAsync(scriptPath, script);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
        });
    }
}

public record UpdateResult(bool HasUpdate, string LatestTag, string ReleaseUrl, string? AssetUrl, string? AssetName);
