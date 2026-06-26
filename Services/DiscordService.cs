using System.Text.RegularExpressions;
using DiscordRPC;

namespace iRPC;

public class DiscordService : IDisposable
{
    private DiscordRpcClient? _client;
    private string _currentAppId = string.Empty;
    private bool _presenceActive;
    private bool _connected;

    // Last-sent presence fields, compared each tick so we only push to Discord (and re-render
    // templates) when something actually visible changed — the poll timer fires every second
    // regardless of whether the session state moved at all.
    private string? _lastDetails;
    private string? _lastState;
    private string? _lastLargeImageKey;
    private string? _lastLargeImageText;
    private string? _lastSmallImageKey;
    private string? _lastSmallImageText;
    private bool? _lastShowGitHubButton;
    private DateTime? _lastTimestamp;

    // True only while the named-pipe handshake with the Discord client is actually live —
    // IsInitialized just means Initialize() was called and stays true even after Discord closes.
    public bool IsConnected => _connected;

    public void Update(SessionData data, AppSettings settings)
    {
        EnsureClient(settings.DiscordAppId);
        if (_client is null) return;

        if (!data.IsConnected)
        {
            Clear();
            return;
        }

        var cfg = settings.GetTemplate(data.SessionType);
        string details = Truncate(ApplyTemplate(cfg.DetailsTemplate, data), 128);
        string state   = Truncate(ApplyTemplate(cfg.StateTemplate, data), 128);
        var assets = BuildAssets(data, settings);
        DateTime? timestamp = settings.ShowElapsedTimer ? data.SessionStartUtc : null;

        bool unchanged = _presenceActive
            && details == _lastDetails
            && state == _lastState
            && assets.LargeImageKey == _lastLargeImageKey
            && assets.LargeImageText == _lastLargeImageText
            && assets.SmallImageKey == _lastSmallImageKey
            && assets.SmallImageText == _lastSmallImageText
            && settings.ShowGitHubButton == _lastShowGitHubButton
            && timestamp == _lastTimestamp;
        if (unchanged) return;

        var presence = new RichPresence
        {
            Details = details,
            State   = state,
            Assets  = assets,
            Buttons = settings.ShowGitHubButton
                ? [new DiscordRPC.Button { Label = "iRPC on GitHub", Url = "https://github.com/aftermath-dev/iRPC" }]
                : null,
        };

        if (timestamp.HasValue)
            presence.Timestamps = new Timestamps(timestamp.Value);

        _client.SetPresence(presence);
        _presenceActive = true;
        _client.Invoke();

        _lastDetails = details;
        _lastState = state;
        _lastLargeImageKey = assets.LargeImageKey;
        _lastLargeImageText = assets.LargeImageText;
        _lastSmallImageKey = assets.SmallImageKey;
        _lastSmallImageText = assets.SmallImageText;
        _lastShowGitHubButton = settings.ShowGitHubButton;
        _lastTimestamp = timestamp;
    }

    public void Clear()
    {
        if (_client is null) return;
        if (_presenceActive)
        {
            _client.ClearPresence();
            _presenceActive = false;
            ResetLastSent();
        }
        _client.Invoke();
    }

    private void ResetLastSent()
    {
        _lastDetails = null;
        _lastState = null;
        _lastLargeImageKey = null;
        _lastLargeImageText = null;
        _lastSmallImageKey = null;
        _lastSmallImageText = null;
        _lastShowGitHubButton = null;
        _lastTimestamp = null;
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

        string lastLap = FormatLapTime(data.LastLapTime);
        string bestLap = FormatLapTime(data.BestLapTime);
        string sky = FormatSky(data.Skies);
        string fastRepairs = data.FastRepairsAvailable > 0 ? $"FR {data.FastRepairsUsed}/{data.FastRepairsAvailable}" : string.Empty;

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
            .Replace("{fuel}",         data.FuelLevel >= 0 ? $"F {data.FuelLevel:F1}L" : string.Empty)
            .Replace("{fuel_gal}",     data.FuelLevel >= 0 ? $"F {data.FuelLevel * 0.26417f:F1}gal" : string.Empty)
            .Replace("{fuel_pct}",     data.FuelPercent >= 0 ? $"F {data.FuelPercent * 100:F0}%" : string.Empty)
            .Replace("{last_lap}",     lastLap.Length > 0 ? $"LL {lastLap}" : string.Empty)
            .Replace("{best_lap}",     bestLap.Length > 0 ? $"BL {bestLap}" : string.Empty)
            .Replace("{sof}",          data.StrengthOfField > 0 ? $"SoF {data.StrengthOfField}" : string.Empty)
            .Replace("{irating}",          data.PlayerIRating > 0 ? $"iR {data.PlayerIRating}" : string.Empty)
            .Replace("{irating_avg5}",     data.IRatingAvg5 > 0 ? $"iR5 {data.IRatingAvg5}" : string.Empty)
            .Replace("{irating_avg10}",    data.IRatingAvg10 > 0 ? $"iR10 {data.IRatingAvg10}" : string.Empty)
            .Replace("{irating_avg_custom}", data.IRatingAvgCustom > 0 ? $"iR{data.IRatingAvgCustomWindow} {data.IRatingAvgCustom}" : string.Empty)
            .Replace("{class_position}",  data.ClassPosition > 0 ? $"P{data.ClassPosition} in class" : string.Empty)
            .Replace("{sky}",              sky)
            .Replace("{air_temp_c}",       $"Air {data.AirTempC:F0}°C")
            .Replace("{air_temp_f}",       $"Air {data.AirTempC * 9 / 5 + 32:F0}°F")
            .Replace("{track_temp_c}",     $"Track {data.TrackTempC:F0}°C")
            .Replace("{track_temp_f}",     $"Track {data.TrackTempC * 9 / 5 + 32:F0}°F")
            .Replace("{pit_service}",      data.PitstopActive ? "Servicing" : string.Empty)
            .Replace("{pit_repair}",       data.PitRepairLeft > 0 ? $"Rep {data.PitRepairLeft:F0}s" : string.Empty)
            .Replace("{pit_opt_repair}",   data.PitOptRepairLeft > 0 ? $"Opt Rep {data.PitOptRepairLeft:F0}s" : string.Empty)
            .Replace("{fast_repairs}",     fastRepairs)
            .Replace("{incidents}",        data.IncidentCount > 0 ? $"{data.IncidentCount}x" : string.Empty)
            .Replace("{flag}",         data.IsCheckered ? "Checkered" : data.IsCaution ? "Caution" : string.Empty)
            .Replace("{pit}",          data.OnPitRoad ? "In Pits" : string.Empty)
            .Replace("{garage}",       data.IsInGarage ? "In Garage" : string.Empty);

        return CleanResult(result);
    }

    // iRacing reports -1 for LapLastLapTime/LapBestLapTime when no valid lap has been set yet.
    private static string FormatLapTime(float seconds) =>
        seconds > 0 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.fff") : string.Empty;

    private static string FormatSky(int skies) => skies switch
    {
        0 => "Clear",
        1 => "Partly Cloudy",
        2 => "Mostly Cloudy",
        3 => "Overcast",
        _ => string.Empty,
    };

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

    // Debug builds pull art from the Julius dev branch so renamed/new assets can be tested
    // before they're merged to main; Release (published) builds always use main.
#if DEBUG
    public const string AssetBase =
        "https://raw.githubusercontent.com/aftermath-dev/iRPC/Julius/ArtAssets";
#else
    public const string AssetBase =
        "https://raw.githubusercontent.com/aftermath-dev/iRPC/main/ArtAssets";
#endif

    private static Assets BuildAssets(SessionData data, AppSettings s)
    {
        string? largeUrl = s.LargeIcon switch
        {
            LargeIconMode.IracingLogo => $"{AssetBase}/Icons/iracing_logo.png",
            LargeIconMode.IrpcLogo   => $"{AssetBase}/Icons/irpc_logo.png",
            LargeIconMode.TrackLogo  => TrackUrl(data.TrackCodeName, data.TrackName),
            _                        => null,
        };

        string? smallUrl = s.SmallIcon switch
        {
            SmallIconMode.CarBrand    => BrandUrl(data.CarName, data.CarCodeName),
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

    // Track asset filenames in this repo are mostly short, predictable names matching the root
    // install-folder segment of the codename (e.g. tracks/suzuka/suzuka.dat -> "suzuka" ->
    // track_suzuka.png), not the official display name, which is often longer/different
    // (e.g. "Autodromo Enzo e Dino Ferrari" for Imola). Prefer the codename root; fall back to
    // the display name if no codename is available yet (e.g. before the first session-info read).
    private static string? TrackUrl(string? codeName, string? displayName)
    {
        string? root = string.IsNullOrWhiteSpace(codeName) ? null : codeName.Split(' ')[0];
        string? key = AssetKey(root) ?? AssetKey(displayName);
        if (key is null) return null;
        string mapped = KeyOverrides.Apply($"track_{key}");
        return $"{AssetBase}/Tracks/{mapped}.png";
    }

    // Series names shared by multiple real manufacturers (e.g. "Formula Renault"/"Formula Mazda"/
    // "Formula Vee") collapse to the same first-word key, which would make any override ambiguous.
    // For these, key off the first two words instead so each variant gets its own resolvable key.
    private static readonly HashSet<string> AmbiguousBrandPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "formula", "super" };

    // For series-branded cars where the first word is a series label and a middle word is a
    // sponsor/series name — the actual manufacturer sits at this word index.
    // e.g. "ARCA Menards Chevrolet": index 0 = "ARCA" (series), 1 = "Menards" (sponsor), 2 = manufacturer.
    private static readonly Dictionary<string, int> SeriesBrandWordIndex =
        new(StringComparer.OrdinalIgnoreCase) { ["arca"] = 2 };

    private static string? BrandUrl(string carName, string carCodeName)
    {
        // Primary: look up by car codename leaf (e.g. "stockcars\camarozl12018" → "camarozl12018").
        if (!string.IsNullOrWhiteSpace(carCodeName))
        {
            string leaf = carCodeName.Split('/', '\\').Last();
            if (AssetKey(leaf) is { } leafKey)
            {
                string carKey = $"car_{leafKey}";
                string carMapped = KeyOverrides.Apply(carKey);
                if (carMapped != carKey)
                    return $"{AssetBase}/Brands/{carMapped}.png";
            }
        }

        // Fallback: derive brand from first word of CarScreenNameShort.
        string[] words = carName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || AssetKey(words[0]) is not { } firstKey) return null;

        string lookupKey;
        if (SeriesBrandWordIndex.TryGetValue(firstKey, out int brandIdx) && words.Length > brandIdx)
            lookupKey = AssetKey(words[brandIdx]) ?? firstKey;
        else if (AmbiguousBrandPrefixes.Contains(firstKey) && words.Length > 1)
            lookupKey = AssetKey($"{words[0]} {words[1]}") ?? firstKey;
        else
            lookupKey = firstKey;

        string mapped = KeyOverrides.Apply($"brand_{lookupKey}");
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
        _connected = false;
        ResetLastSent();
    }

    private void EnsureClient(string appId)
    {
        if (_client != null && _currentAppId == appId) return;
        _client?.Dispose();
        _connected = false;
        _client = new DiscordRpcClient(appId);
        _client.OnConnectionEstablished += (_, _) => _connected = true;
        _client.OnConnectionFailed      += (_, _) => _connected = false;
        _client.OnClose                 += (_, _) => _connected = false;
        _client.Initialize();
        _currentAppId = appId;
        _presenceActive = false;
        ResetLastSent();
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _connected = false;
    }
}
