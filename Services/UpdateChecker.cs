using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
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
        string? notes = doc.RootElement.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() : null;

        string? assetUrl = null, assetName = null, assetDigest = null;
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
                    if (asset.TryGetProperty("digest", out var digestEl))
                    {
                        string? digest = digestEl.GetString();
                        if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                            assetDigest = digest["sha256:".Length..].ToLowerInvariant();
                    }
                    break;
                }
            }
        }

        string versionPart = tag.TrimStart('v').Split('-')[0];
        if (!Version.TryParse(versionPart, out var latest))
            return new UpdateResult(false, tag, url, assetUrl, assetName, assetDigest, notes);

        return new UpdateResult(latest > CurrentVersion, tag, url, assetUrl, assetName, assetDigest, notes);
    }

    // Stashes the new release's notes to disk before relaunching, since the new process
    // is a fresh instance and can't see the in-memory UpdateResult from the old one.
    private static readonly string PendingNotesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iRPC", "pending-update-notes.json");

    public static void SavePendingReleaseNotes(string version, string? notes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PendingNotesPath)!);
            File.WriteAllText(PendingNotesPath, JsonSerializer.Serialize(new PendingNotes(version, notes ?? "")));
        }
        catch { }
    }

    public static PendingNotes? ConsumePendingReleaseNotes()
    {
        try
        {
            if (!File.Exists(PendingNotesPath)) return null;
            var notes = JsonSerializer.Deserialize<PendingNotes>(File.ReadAllText(PendingNotesPath));
            File.Delete(PendingNotesPath);
            return notes;
        }
        catch { return null; }
    }

    // Downloads the release exe and writes a helper script that waits for this process
    // to exit, swaps it into place, and relaunches it — Windows won't let us overwrite
    // our own running executable directly.
    public static async Task DownloadAndPrepareRestartAsync(
        UpdateResult result,
        Action<long, long?>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (result.AssetUrl is null || result.AssetName is null)
            throw new InvalidOperationException("No downloadable asset found for this release.");

        string currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the running executable path.");

        string tempDir = Path.Combine(Path.GetTempPath(), "iRPC-update");
        Directory.CreateDirectory(tempDir);
        string newExePath = Path.Combine(tempDir, result.AssetName);

        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var response = await _http.GetAsync(result.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                long? totalBytes = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var dest = File.Create(newExePath);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;
                while ((read = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hasher.AppendData(buffer, 0, read);
                    totalRead += read;
                    onProgress?.Invoke(totalRead, totalBytes);
                }
            }

            if (result.AssetDigest is not null)
            {
                string computed = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
                if (!computed.Equals(result.AssetDigest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Downloaded file failed integrity check — checksum mismatch.");
            }
        }
        catch
        {
            try { File.Delete(newExePath); } catch { }
            throw;
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

public record UpdateResult(bool HasUpdate, string LatestTag, string ReleaseUrl, string? AssetUrl, string? AssetName, string? AssetDigest, string? ReleaseNotes);

public record PendingNotes(string Version, string Notes);
