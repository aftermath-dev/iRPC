using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace iRPC;

public class WidgetService
{
    private static readonly HttpClient _http = new();
    private DateTime _lastPush = DateTime.MinValue;
    private string? _lastPayload;

    private const int CooldownSeconds = 30;

    public async Task UpdateAsync(StatsData stats, AppSettings settings)
    {
        if (!settings.WidgetEnabled) return;
        if (string.IsNullOrWhiteSpace(settings.DiscordWidgetBotToken)) return;
        if (string.IsNullOrWhiteSpace(settings.DiscordUserId)) return;

        if ((DateTime.UtcNow - _lastPush).TotalSeconds < CooldownSeconds) return;

        string payload = BuildPayload(stats, settings);
        if (payload == _lastPayload) return;

        _lastPush = DateTime.UtcNow;
        try
        {
            string url = $"https://discord.com/api/v9/applications/{settings.DiscordAppId}" +
                         $"/users/{settings.DiscordUserId}/identities/0/profile";

            string tokenSnippet = settings.DiscordWidgetBotToken.Length > 8
                ? settings.DiscordWidgetBotToken[..8] + "…" : "(short)";
            Logger.Log($"Widget → PATCH {url}  bot={tokenSnippet}  payload={payload.Length}B");
            Logger.Log($"Widget payload: {payload}");

            using var req = new HttpRequestMessage(HttpMethod.Patch, url);
            req.Headers.Add("Authorization", $"Bot {settings.DiscordWidgetBotToken}");
            req.Headers.Add("User-Agent", "DiscordBot (https://github.com/aftermath-dev/iRPC, 1.0.0)");
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode)
            {
                _lastPayload = payload;
                Logger.Log($"Widget push OK ({(int)resp.StatusCode})");
            }
            else
            {
                string body = await resp.Content.ReadAsStringAsync();
                Logger.Log($"Widget push failed {(int)resp.StatusCode}: {body}");
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Widget push error: {ex.Message}");
        }
    }

    private static string BuildPayload(StatsData stats, AppSettings settings)
    {
        var dynamic = new List<object>();

        string totalTime = FormatSeconds(stats.TotalSeconds);
        string totalLaps = stats.TotalLaps.ToString();
        string totalDist = $"{stats.TotalDistanceM / 1000.0:F1} km";
        string incidents = stats.TotalIncidents.ToString();
        string favCar    = stats.SecondsByCar.Count > 0
            ? stats.SecondsByCar.MaxBy(x => x.Value).Key : "";
        string favTrack  = stats.SecondsByTrack.Count > 0
            ? stats.SecondsByTrack.MaxBy(x => x.Value).Key : "";

        dynamic.Add(new { type = 1, name = "stat_1_label", value = "Total Time on Track" });
        dynamic.Add(new { type = 1, name = "stat_1_value", value = totalTime });
        dynamic.Add(new { type = 1, name = "stat_2_label", value = "Total Laps" });
        dynamic.Add(new { type = 1, name = "stat_2_value", value = totalLaps });
        dynamic.Add(new { type = 1, name = "stat_3_label", value = "Total Distance" });
        dynamic.Add(new { type = 1, name = "stat_3_value", value = totalDist });
        dynamic.Add(new { type = 1, name = "stat_4_label", value = "Total Incidents" });
        dynamic.Add(new { type = 1, name = "stat_4_value", value = incidents });
        if (favCar.Length > 0)
        {
            dynamic.Add(new { type = 1, name = "stat_5_label", value = "Favorite Vehicle" });
            dynamic.Add(new { type = 1, name = "stat_5_value", value = favCar });
        }
        if (favTrack.Length > 0)
        {
            dynamic.Add(new { type = 1, name = "stat_6_label", value = "Favorite Track" });
            dynamic.Add(new { type = 1, name = "stat_6_value", value = favTrack });
        }

        string username = !string.IsNullOrWhiteSpace(settings.DiscordLinkedUsername)
            ? settings.DiscordLinkedUsername : "iRPC";

        var payload = new
        {
            username,
            data = new { @dynamic = dynamic }
        };
        return JsonSerializer.Serialize(payload);
    }

    private static string FormatSeconds(ulong totalSeconds)
    {
        ulong h = totalSeconds / 3600;
        ulong m = (totalSeconds % 3600) / 60;
        if (h > 0) return $"{h}h {m}m";
        return $"{m}m";
    }
}
