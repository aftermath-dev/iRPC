namespace iRPC;

// Appends newly seen cars to %AppData%\iRPC\cars.txt so you can
// build up the full list of asset keys just by hopping in cars in iRacing.
public static class CarCollector
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "cars.txt");
    private static readonly HashSet<string> _seen = Load();
    private static readonly object _lock = new();

    public static bool Enabled { get; set; } = false;

    public static void Record(string carName, string carCodeName)
    {
        if (!Enabled) return;
        if (string.IsNullOrWhiteSpace(carName)) return;

        string entry = string.IsNullOrWhiteSpace(carCodeName)
            ? carName
            : $"{carName} | {carCodeName}";

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
