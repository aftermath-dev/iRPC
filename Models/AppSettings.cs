using System.Text.Json;
using System.Text.Json.Serialization;

namespace iRPC;

public enum LargeIconMode { IracingLogo, IrpcLogo, TrackLogo }
public enum SmallIconMode { Off, CarBrand, SessionType }

public class SessionPresenceConfig
{
    public string DetailsTemplate { get; set; } = "{session} - {track} - {config}";
    public string StateTemplate { get; set; } = "{car} | {laps_total} | {time_remain}";
}

public class PresencePreset
{
    public Dictionary<string, SessionPresenceConfig> SessionTemplates { get; set; } = new();
    public string LargeTextTemplate { get; set; } = "{track} - {config}";
    public string SmallTextTemplate { get; set; } = "{car}";
}

public class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "settings.json");

    public string DiscordAppId { get; set; } = "1514987753136193706";
    public bool HasShownWelcome { get; set; } = false;
    public bool LaunchOnStartup { get; set; } = false;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool ShowGitHubButton { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public bool TrackAndCarLogging { get; set; } = false;
    public bool ClassicTemplateEditor { get; set; } = false;
    public int IRatingAvgCustomWindow { get; set; } = 20;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LargeIconMode LargeIcon { get; set; } = LargeIconMode.TrackLogo;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SmallIconMode SmallIcon { get; set; } = SmallIconMode.CarBrand;

    public string LargeTextTemplate { get; set; } = "{track} - {config}";
    public string SmallTextTemplate { get; set; } = "{car}";

    public Dictionary<string, SessionPresenceConfig> SessionTemplates { get; set; } = new(DefaultTemplates);
    public Dictionary<string, PresencePreset> Presets { get; set; } = new();

    public SessionPresenceConfig GetTemplate(string sessionType)
    {
        if (SessionTemplates.TryGetValue(sessionType, out var cfg)) return cfg;
        return new SessionPresenceConfig();
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
                foreach (var kv in DefaultTemplates)
                    loaded.SessionTemplates.TryAdd(kv.Key, kv.Value);
                loaded.SessionTemplates.Remove("Default");
                foreach (var kv in DefaultPresets)
                    loaded.Presets.TryAdd(kv.Key, kv.Value);
                return loaded;
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

            // Write-then-rename so a crash/power loss mid-write can't leave a truncated settings.json.
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch { }
    }

    public static readonly Dictionary<string, SessionPresenceConfig> DefaultTemplates = new()
    {
        ["Practice"] = new()
        {
            DetailsTemplate = "{session} - {track} - {config}",
            StateTemplate   = "{car} | {laps_total} | {time_remain}"
        },
        ["Qualify"] = new()
        {
            DetailsTemplate = "{session} - {track} - {config}",
            StateTemplate   = "{car} | {laps_total} | {time_remain}"
        },
        ["Race"] = new()
        {
            DetailsTemplate = "{session} - {track} - {config}",
            StateTemplate   = "{car} | {position} | {laps_total} | {time_remain} | {flag}"
        },
        ["Test Drive"] = new()
        {
            DetailsTemplate = "{session} - {track} - {config}",
            StateTemplate   = "{car} | {speed_kmh} | {fuel_pct}"
        },
        ["Time Trial"] = new()
        {
            DetailsTemplate = "{session} - {track} - {config}",
            StateTemplate   = "{car} | {laps_total} | {time_remain}"
        },
    };

    public static readonly Dictionary<string, PresencePreset> DefaultPresets = new()
    {
        ["Minimal"] = Preset(
            details: "{track} - {config}",
            state:   "{car}"),

        ["Standard"] = new PresencePreset
        {
            LargeTextTemplate = "{track} - {config}",
            SmallTextTemplate = "{car}",
            SessionTemplates = AllSessions(
                details: "{session} - {track} - {config}",
                state:   "{car} | {laps_total} | {time_remain}",
                raceState: "{car} | {position} | {laps_total} | {time_remain} | {flag}"),
        },

        ["Endurance"] = Preset(
            details: "{track} - {config} | {car}",
            state:   "{position} | {laps_total} | {fuel} | {tire} | {last_lap}"),

        ["iRating"] = Preset(
            details: "{track} - {config}",
            state:   "{position} | {irating} | {sof} | {incidents}",
            smallText: "{irating}"),

        ["Weather"] = Preset(
            details: "{track} - {config} | {sky}",
            state:   "{car} | {air_temp_c} | {track_temp_c} | {flag}"),

        ["Oval"] = Preset(
            details: "{track} - {config}",
            state:   "{position} | {laps_remain} | {fuel_pct} | {incidents} | {flag}"),

        ["Qualifying"] = Preset(
            details: "{session} - {track} - {config}",
            state:   "{car} | {best_lap} | {last_lap} | {position} | {time_remain}"),

        ["Spectator"] = Preset(
            details: "{track} - {config} | {session}",
            state:   "{sof} | {time_remain}",
            smallText: "{session}"),
    };

    private static PresencePreset Preset(string details, string state,
        string largeText = "{track} - {config}", string smallText = "{car}") => new()
    {
        LargeTextTemplate = largeText,
        SmallTextTemplate = smallText,
        SessionTemplates  = AllSessions(details, state),
    };

    private static Dictionary<string, SessionPresenceConfig> AllSessions(
        string details, string state, string? raceState = null)
    {
        var cfg     = new SessionPresenceConfig { DetailsTemplate = details, StateTemplate = state };
        var raceCfg = raceState is null ? cfg : new SessionPresenceConfig { DetailsTemplate = details, StateTemplate = raceState };
        return new()
        {
            ["Practice"]   = cfg,
            ["Qualify"]    = cfg,
            ["Race"]       = raceCfg,
            ["Test Drive"] = cfg,
            ["Time Trial"] = cfg,
        };
    }
}
