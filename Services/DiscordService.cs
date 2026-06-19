using System.Text.RegularExpressions;
using DiscordRPC;

namespace iRPC;

public class DiscordService : IDisposable
{
    private DiscordRpcClient? _client;
    private string _currentAppId = string.Empty;
    private bool _presenceActive;

    public bool IsConnected => _client?.IsInitialized == true;

    public void Update(SessionData data, AppSettings settings)
    {
        EnsureClient(settings.DiscordAppId);
        if (_client is null) return;

        if (!data.IsConnected)
        {
            if (_presenceActive)
            {
                _client.ClearPresence();
                _presenceActive = false;
            }
            _client.Invoke();
            return;
        }

        var cfg = settings.GetTemplate(data.SessionType);
        var presence = new RichPresence
        {
            Details = Truncate(ApplyTemplate(cfg.DetailsTemplate, data), 128),
            State   = Truncate(ApplyTemplate(cfg.StateTemplate, data), 128),
            Assets  = BuildAssets(data, settings),
            Buttons = settings.ShowGitHubButton
                ? [new DiscordRPC.Button { Label = "iRPC on GitHub", Url = "https://github.com/aftermath-dev/iRPC" }]
                : null,
        };

        if (settings.ShowElapsedTimer && data.SessionStartUtc.HasValue)
            presence.Timestamps = new Timestamps(data.SessionStartUtc.Value);

        _client.SetPresence(presence);
        _presenceActive = true;
        _client.Invoke();
    }

    public static string ApplyTemplate(string template, SessionData data)
    {
        string lapTotal = data.CurrentLap > 0
            ? (data.LapsRemain is > 0 and < 32767
                ? $"Lap {data.CurrentLap}/{data.CurrentLap + data.LapsRemain}"
                : $"Lap {data.CurrentLap}")
            : string.Empty;

        string timeRemain = data.TimeRemaining > 0 && data.TimeRemaining < 86400
            ? TimeSpan.FromSeconds(data.TimeRemaining) is var ts
                ? ts.Hours > 0 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss")
                : string.Empty
            : string.Empty;

        string result = template
            .Replace("{session}",      data.SessionType)
            .Replace("{track}",        data.TrackName)
            .Replace("{config}",       data.TrackConfig)
            .Replace("{car}",          data.CarName)
            .Replace("{position}",     data.Position > 0 ? $"P{data.Position}" : string.Empty)
            .Replace("{lap}",          data.CurrentLap > 0 ? $"Lap {data.CurrentLap}" : string.Empty)
            .Replace("{laps_total}",   lapTotal)
            .Replace("{laps_remain}",  data.LapsRemain is > 0 and < 32767 ? $"{data.LapsRemain} laps left" : string.Empty)
            .Replace("{time_remain}",  timeRemain)
            .Replace("{speed_kmh}",    $"{data.Speed * 3.6f:F0} km/h")
            .Replace("{speed_mph}",    $"{data.Speed * 2.237f:F0} mph")
            .Replace("{fuel}",         $"{data.FuelLevel:F1}L")
            .Replace("{fuel_pct}",     $"{data.FuelPercent * 100:F0}%")
            .Replace("{flag}",         data.IsCheckered ? "Checkered" : data.IsCaution ? "Caution" : string.Empty)
            .Replace("{pit}",          data.OnPitRoad ? "In Pits" : string.Empty)
            .Replace("{garage}",       data.IsInGarage ? "In Garage" : string.Empty);

        return CleanResult(result);
    }

    private static string CleanResult(string s)
    {
        // Collapse consecutive | separators
        s = Regex.Replace(s, @"(\s*\|\s*){2,}", " | ");
        s = s.Trim().TrimStart('|').TrimEnd('|').Trim();
        // Collapse consecutive space-dash separators (empty middle placeholder, e.g. "A -  - B" → "A - B")
        s = Regex.Replace(s, @"(\s+-){2,}\s*", " - ");
        // Trim trailing separators
        s = Regex.Replace(s, @"\s+[-|]\s*$", "").Trim();
        return s;
    }

    private const string AssetBase =
        "https://raw.githubusercontent.com/aftermath-dev/iRPC/main/ArtAssets";

    private static Assets BuildAssets(SessionData data, AppSettings s)
    {
        string? largeUrl = s.LargeIcon switch
        {
            LargeIconMode.IracingLogo => $"{AssetBase}/Icons/iracing_logo.png",
            LargeIconMode.IrpcLogo   => $"{AssetBase}/Icons/irpc_logo.png",
            LargeIconMode.TrackLogo  => TrackUrl(data.TrackName),
            _                        => null,
        };

        string? smallUrl = s.SmallIcon switch
        {
            SmallIconMode.CarBrand    => BrandUrl(data.CarName),
            SmallIconMode.SessionType => SessionIconUrl(data.SessionType),
            _                         => null,
        };

        string largeText = CleanResult(ApplyTemplate(s.LargeTextTemplate, data));
        string? smallText = s.SmallIcon != SmallIconMode.Off
            ? CleanResult(ApplyTemplate(s.SmallTextTemplate, data)) is { Length: > 0 } st ? st : null
            : null;

        Logger.Log($"Assets — large={largeUrl} small={smallUrl}");

        return new Assets
        {
            LargeImageKey  = largeUrl,
            LargeImageText = largeText,
            SmallImageKey  = smallUrl,
            SmallImageText = smallText,
        };
    }

    private static string? TrackUrl(string? name)
    {
        if (AssetKey(name) is not { } k) return null;
        string mapped = KeyOverrides.Apply($"track_{k}");
        return $"{AssetBase}/Tracks/{mapped}.png";
    }

    private static string? BrandUrl(string carName)
    {
        string brand = carName.Split(' ')[0];
        if (AssetKey(brand) is not { } k) return null;
        string mapped = KeyOverrides.Apply($"brand_{k}");
        return $"{AssetBase}/Brands/{mapped}.png";
    }

    private static string? SessionIconUrl(string sessionType) =>
        sessionType.ToLowerInvariant() switch
        {
            "practice"   => $"{AssetBase}/Icons/icon_practice.png",
            "qualify"    => $"{AssetBase}/Icons/icon_qualify.png",
            "race"       => $"{AssetBase}/Icons/icon_race.png",
            "test drive" => $"{AssetBase}/Icons/icon_test_drive.png",
            "time trial" => $"{AssetBase}/Icons/icon_time_trial.png",
            _            => null,
        };

    private static string? AssetKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = new System.Text.StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            // Fold accented/umlauted characters to their ASCII base
            char n = c switch
            {
                'ü' or 'ú' or 'ù' or 'û' => 'u',
                'ö' or 'ó' or 'ò' or 'ô' => 'o',
                'ä' or 'á' or 'à' or 'â' => 'a',
                'é' or 'è' or 'ê' or 'ë' => 'e',
                'ï' or 'í' or 'ì' or 'î' => 'i',
                'ñ' => 'n', 'ç' => 'c', 'ß' => 's',
                _ => c,
            };
            if (char.IsAsciiLetterOrDigit(n)) key.Append(n);
            else if (n is ' ' or '_' or '-') key.Append('_');
        }
        string result = Regex.Replace(key.ToString(), "_+", "_").Trim('_');
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
