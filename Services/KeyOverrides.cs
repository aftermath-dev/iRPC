using System.Text.Json;

namespace iRPC;

// Loads %AppData%\iRPC\key_overrides.json and lets you remap any auto-generated
// asset key to a custom one.
//
// Key prefixes:
//   car_{codename}   → brand asset filename (e.g. car_camarozl12018 → brand_chevrolet)
//   brand_{word}     → brand asset filename (first-word fallback, e.g. brand_corvette → brand_chevrolet)
//   track_{key}      → track asset filename (e.g. track_watkins_glen_boot → track_watkins_glen)
//
// User entries always take precedence over the built-in defaults below.
public static class KeyOverrides
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "key_overrides.json");

    // Loaded from embedded JSON resources at startup.
    // Format: { "brand_chevrolet": ["car_c6r", ...], ... } — inverted to key→value on load.
    private static readonly Dictionary<string, string> CarDefaults   = LoadGroupedJson("iRPC.car_brands.json");
    private static readonly Dictionary<string, string> TrackDefaults = LoadGroupedJson("iRPC.track_defaults.json");

    private static Dictionary<string, string> LoadGroupedJson(string resourceName)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) return d;
            var grouped = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream);
            if (grouped is null) return d;
            foreach (var (target, keys) in grouped)
                foreach (var key in keys)
                    d[key] = target;
        }
        catch { }
        return d;
    }


    // First-word-of-display-name fallbacks for cars not in CarDefaults.
    // Used when no CarPath codename match is found.
    private static readonly Dictionary<string, string> BrandDefaults =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["brand_aston"]           = "brand_aston_martin",
            ["brand_mercedes_amg"]    = "brand_mercedes",
            ["brand_corvette"]        = "brand_chevrolet",
            ["brand_cruze"]           = "brand_chevrolet",
            ["brand_corolla"]         = "brand_toyota",
            ["brand_camaro"]          = "brand_chevrolet",
            ["brand_chevy"]           = "brand_chevrolet",
            ["brand_impala"]          = "brand_chevrolet",
            ["brand_monte"]           = "brand_chevrolet",
            ["brand_fusion"]          = "brand_ford",
            ["brand_taurus"]          = "brand_ford",
            ["brand_thunderbird"]     = "brand_ford",
            ["brand_camry"]           = "brand_toyota",
            ["brand_mustang"]         = "brand_ford",
            ["brand_supra"]           = "brand_toyota",
            ["brand_silverado"]       = "brand_chevrolet",
            ["brand_tundra"]          = "brand_toyota",
            ["brand_formula_mazda"]   = "brand_mazda",
            ["brand_formula_renault"] = "brand_renault",
            ["brand_hpd"]             = "brand_honda",
            ["brand_jetta"]           = "brand_volkswagen",
        };

    private static Dictionary<string, string> _map = Load();

    // Resolution order: user map → car defaults → track defaults → brand/word defaults → key itself.
    public static string Apply(string key) =>
        _map.TryGetValue(key, out string? u)              ? u   :
        CarDefaults.TryGetValue(key, out string? c)       ? c   :
        TrackDefaults.TryGetValue(key, out string? t)     ? t   :
        BrandDefaults.TryGetValue(key, out string? b)     ? b   : key;

    public static void Reload() => _map = Load();

    // Returns only user-edited entries; built-in defaults are applied silently via Apply().
    public static Dictionary<string, string> GetAll() => new(_map);

    public static void SetAll(Dictionary<string, string> map)
    {
        _map = new(map);
        Save(_map);
    }

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
                string trackName = line.Contains('|')
                    ? line[..line.IndexOf('|')].Trim()
                    : line.Trim();

                if (string.IsNullOrWhiteSpace(trackName)) continue;

                string key = $"track_{Sanitize(trackName)}";
                // Skip tracks already handled by built-in TrackDefaults; only add user-visible
                // identity entries for tracks that have no automatic default mapping.
                if (!TrackDefaults.ContainsKey(key) && _map.TryAdd(key, key))
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
        }
        catch { }
        return new();
    }

    private static void Save(Dictionary<string, string> map)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string json = JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true });

        string tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, FilePath, overwrite: true);
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
}
