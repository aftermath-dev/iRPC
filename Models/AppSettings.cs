using System.Text.Json;
using System.Text.Json.Serialization;

namespace iRPC;

public enum LargeIconMode { IracingLogo, IrpcLogo, TrackLogo }
public enum SmallIconMode { Off, CarBrand, SessionType }

public class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "settings.json");

    public string DiscordAppId { get; set; } = "1514987753136193706";
    public bool ShowCarName { get; set; } = true;
    public bool ShowLapProgress { get; set; } = true;
    public bool ShowPosition { get; set; } = true;
    public bool ShowTimeRemaining { get; set; } = true;
    public bool ShowFlag { get; set; } = true;
    public bool ShowElapsedTimer { get; set; } = true;
    public bool LaunchOnStartup { get; set; } = false;
    public bool ShowGitHubButton { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LargeIconMode LargeIcon { get; set; } = LargeIconMode.TrackLogo;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SmallIconMode SmallIcon { get; set; } = SmallIconMode.CarBrand;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
