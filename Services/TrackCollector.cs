namespace iRPC;

// Appends newly seen tracks to %AppData%\iRPC\tracks.txt so you can
// build up the full list of asset keys just by loading tracks in iRacing.
public static class TrackCollector
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "tracks.txt");
    private static readonly HashSet<string> _seen = Load();
    private static readonly object _lock = new();

    public static bool Enabled { get; set; } = false;

    public static void Record(string trackName, string trackConfig, string trackCodeName)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(trackName)) return;

        string entry = string.IsNullOrWhiteSpace(trackConfig)
            ? trackName
            : $"{trackName} | {trackConfig}";

        if (!string.IsNullOrWhiteSpace(trackCodeName))
            entry += $" | {trackCodeName}";

        lock (_lock)
        {
            if (!_seen.Add(entry)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.AppendAllText(FilePath, entry + Environment.NewLine);
        }
    }

    private static HashSet<string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return new HashSet<string>(File.ReadAllLines(FilePath), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}
