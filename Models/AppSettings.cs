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

public class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "settings.json");

    public string DiscordAppId { get; set; } = "1514987753136193706";
    public bool ShowElapsedTimer { get; set; } = true;
    public bool LaunchOnStartup { get; set; } = false;
    public bool ShowGitHubButton { get; set; } = true;
    public bool AutoPopulateKeyOverrides { get; set; } = true;
    public bool DebugMode { get; set; } = false;
    public bool TrackAndCarLogging { get; set; } = false;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LargeIconMode LargeIcon { get; set; } = LargeIconMode.TrackLogo;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SmallIconMode SmallIcon { get; set; } = SmallIconMode.CarBrand;

    public string LargeTextTemplate { get; set; } = "{track} - {config}";
    public string SmallTextTemplate { get; set; } = "{car}";

    public Dictionary<string, SessionPresenceConfig> SessionTemplates { get; set; } = DefaultTemplates();

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
                // Ensure all session types are present after upgrades
                foreach (var kv in DefaultTemplates())
                    loaded.SessionTemplates.TryAdd(kv.Key, kv.Value);
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
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public static Dictionary<string, SessionPresenceConfig> DefaultTemplates() => new()
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
}
