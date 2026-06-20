using System.Text.RegularExpressions;
using DiscordRPC;

namespace iRPC;

public class DiscordService : IDisposable
{
    private DiscordRpcClient? _client;
    private string _currentAppId = string.Empty;
    private bool _presenceActive;
    private bool _connected;

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

    public void Clear()
    {
        if (_client is null) return;
        if (_presenceActive)
        {
            _client.ClearPresence();
            _presenceActive = false;
        }
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

    // Series names shared by multiple real manufacturers (e.g. "ARCA Ford"/"ARCA Chevrolet"/
    // "ARCA Toyota", "Formula Renault"/"Formula Mazda"/"Formula Vee") collapse to the same
    // first-word key, which would make any override ambiguous. For these, key off the first
    // two words instead so each manufacturer/variant gets its own resolvable key.
    private static readonly HashSet<string> AmbiguousBrandPrefixes =
        new(StringComparer.OrdinalIgnoreCase) { "arca", "formula", "super" };

    // Known multi-word manufacturer names whose first word alone doesn't match an asset
    // filename — these are facts about the car roster, not something a user should have to
    // configure via key_overrides.json.
    private static readonly Dictionary<string, string> BrandAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["aston"] = "aston_martin",
            ["mercedes_amg"] = "mercedes",
        };

    private static string? BrandUrl(string carName)
    {
        string[] words = carName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || AssetKey(words[0]) is not { } firstKey) return null;

        string lookupKey = AmbiguousBrandPrefixes.Contains(firstKey) && words.Length > 1
            ? AssetKey($"{words[0]} {words[1]}") ?? firstKey
            : firstKey;

        if (BrandAliases.TryGetValue(lookupKey, out string? alias))
            lookupKey = alias;

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
