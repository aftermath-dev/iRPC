using System.Text.Json;

namespace iRPC;

// Loads %AppData%\iRPC\key_overrides.json and lets you remap any auto-generated
// asset key to a custom one. Example file:
// {
//   "imola_full": "imola",
//   "watkins_glen_boot": "watkins_glen"
// }
public static class KeyOverrides
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "key_overrides.json");

    private static Dictionary<string, string> _map = Load();

    public static string Apply(string key) =>
        _map.TryGetValue(key, out string? mapped) ? mapped : key;

    public static void Reload() => _map = Load();

    // Reads tracks.txt and adds any keys not already in the overrides file,
    // using an identity mapping so users can see and edit them.
    public static void SyncFromTracks()
    {
        try
        {
            string tracksFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "iRPC", "tracks.txt");

            if (!File.Exists(tracksFile)) return;

            bool changed = false;
            foreach (string line in File.ReadAllLines(tracksFile))
            {
                // Format is "TrackName" or "TrackName | Config"
                string trackName = line.Contains('|')
                    ? line[..line.IndexOf('|')].Trim()
                    : line.Trim();

                if (string.IsNullOrWhiteSpace(trackName)) continue;

                string key = $"track_{Sanitize(trackName)}";
                if (_map.TryAdd(key, key))
                    changed = true;
            }

            if (changed) Save(_map);
        }
        catch { }
    }

    private static Dictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(FilePath)) ?? new();

            var defaults = Defaults();
            Save(defaults);
            return defaults;
        }
        catch { }
        return new();
    }

    private static void Save(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(map,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Sanitize(string name)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) sb.Append(c);
            else if (c is ' ' or '_' or '-') sb.Append('_');
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_").Trim('_');
    }

    private static Dictionary<string, string> Defaults() => new()
    {
        ["track_imola_full"] = "track_imola",
    };
}
