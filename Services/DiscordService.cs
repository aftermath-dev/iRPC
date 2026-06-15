using DiscordRPC;

namespace iRPC;

public class DiscordService : IDisposable
{
    private DiscordRpcClient? _client;
    private string _currentAppId = string.Empty;
    private bool _presenceActive;

    public void Update(SessionData data, AppSettings settings)
    {
        EnsureClient(settings.DiscordAppId);
        if (_client is null) return;

        bool shouldHide = !data.IsConnected;

        if (shouldHide)
        {
            if (_presenceActive)
            {
                _client.ClearPresence();
                _presenceActive = false;
            }
            _client.Invoke();
            return;
        }

        var presence = new RichPresence
        {
            Details = Truncate(BuildDetails(data), 128),
            State   = Truncate(BuildState(data, settings), 128),
            Assets  = BuildAssets(data, settings),
            Buttons = settings.ShowGitHubButton
                ? [new DiscordRPC.Button { Label = "iRPC on GitHub", Url = "https://github.com/Mathues-Studios/iRPC" }]
                : null,
        };

        if (settings.ShowElapsedTimer && data.SessionStartUtc.HasValue)
            presence.Timestamps = new Timestamps(data.SessionStartUtc.Value);

        _client.SetPresence(presence);
        _presenceActive = true;
        _client.Invoke();
    }

    private static string BuildDetails(SessionData data)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(data.SessionType)) parts.Add(data.SessionType);
        if (!string.IsNullOrEmpty(data.TrackName))   parts.Add(data.TrackName);
        if (!string.IsNullOrEmpty(data.TrackConfig)) parts.Add(data.TrackConfig);
        return string.Join(" - ", parts);
    }

    private static string BuildState(SessionData data, AppSettings s)
    {
        var parts = new List<string>(5);

        if (s.ShowCarName && !string.IsNullOrEmpty(data.CarName))
            parts.Add(data.CarName);

        bool isRace = data.SessionType.Equals("Race", StringComparison.OrdinalIgnoreCase);
        if (s.ShowPosition && isRace && data.Position > 0)
            parts.Add($"P{data.Position}");

        if (s.ShowLapProgress && data.CurrentLap > 0)
        {
            // 32767 is iRacing's sentinel for unlimited laps
            string lap = data.LapsRemain != 32767 && data.LapsRemain >= 0
                ? $"Lap {data.CurrentLap}/{data.CurrentLap + data.LapsRemain}"
                : $"Lap {data.CurrentLap}";
            parts.Add(lap);
        }

        if (s.ShowTimeRemaining && data.TimeRemaining > 0 && data.TimeRemaining < 86400)
        {
            var ts = TimeSpan.FromSeconds(data.TimeRemaining);
            parts.Add(ts.Hours > 0 ? $"{ts:h\\:mm\\:ss}" : $"{ts:m\\:ss}");
        }

        if (s.ShowFlag)
        {
            if (data.IsCheckered)   parts.Add("Checkered");
            else if (data.IsCaution) parts.Add("Caution");
        }

        return string.Join(" | ", parts);
    }

    private static Assets BuildAssets(SessionData data, AppSettings s)
    {
        string? largeKey = s.LargeIcon switch
        {
            LargeIconMode.IracingLogo => "iracing_logo",
            LargeIconMode.IrpcLogo   => "irpc_logo",
            LargeIconMode.TrackLogo  => KeyOverrides.Apply(TrackKey(data.TrackName) ?? "iracing_logo"),
            _                        => null,
        };

        string largeText = string.IsNullOrEmpty(data.TrackConfig)
            ? data.TrackName
            : $"{data.TrackName} – {data.TrackConfig}";

        string? smallKey = s.SmallIcon switch
        {
            SmallIconMode.CarBrand    => KeyOverrides.Apply(BrandKey(data.CarName) ?? string.Empty) is { Length: > 0 } k ? k : null,
            SmallIconMode.SessionType => SessionTypeKey(data.SessionType),
            _                         => null,
        };

        string? smallText = s.SmallIcon switch
        {
            SmallIconMode.CarBrand    => data.CarName,
            SmallIconMode.SessionType => data.SessionType,
            _                         => null,
        };

        Logger.Log($"Assets — large={largeKey} small={smallKey}");

        return new Assets
        {
            LargeImageKey  = largeKey,
            LargeImageText = largeText,
            SmallImageKey  = smallKey,
            SmallImageText = smallText,
        };
    }

    private static string? TrackKey(string? name) =>
        AssetKey(name) is { } k ? $"track_{k}" : null;

    private static string? BrandKey(string carName)
    {
        string brand = carName.Split(' ')[0];
        return AssetKey(brand) is { } k ? $"brand_{k}" : null;
    }

    private static string? SessionTypeKey(string sessionType) =>
        sessionType.ToLowerInvariant() switch
        {
            "practice"   => "icon_practice",
            "qualify"    => "icon_qualify",
            "race"       => "icon_race",
            "test drive" => "icon_test_drive",
            "time trial" => "icon_time_trial",
            _            => null,
        };

    // Converts a display name to a Discord asset key: lowercase, spaces→underscores,
    // strips everything else, truncated to 32 chars. Returns null if result is empty
    // (Discord will just show nothing for that slot).
    private static string? AssetKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = new System.Text.StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) key.Append(c);
            else if (c is ' ' or '_' or '-') key.Append('_');
        }
        // Collapse repeated underscores
        string result = System.Text.RegularExpressions.Regex.Replace(key.ToString(), "_+", "_").Trim('_');
        return result.Length == 0 ? null : result[..Math.Min(result.Length, 32)];
    }

    public void Reconnect()
    {
        _client?.Dispose();
        _client = null;
        _presenceActive = false;
    }

    private void EnsureClient(string appId)
    {
        if (_client != null && _currentAppId == appId) return;

        _client?.Dispose();
        _client = new DiscordRpcClient(appId);
        _client.Initialize();
        _currentAppId = appId;
        _presenceActive = false;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
    }
}
