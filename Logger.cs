namespace iRPC;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "iRPC.log");
    private static readonly string CrashMarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "crashed.marker");
    private static readonly object _lock = new();

    public static bool Enabled { get; set; } = false;

    public static void Log(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    // Dropped right before a fatal unhandled exception takes the process down, so the
    // next launch can tell the user something went wrong even with debug logging off.
    public static void MarkCrashed()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashMarkerPath)!);
            File.WriteAllText(CrashMarkerPath, DateTime.Now.ToString("u"));
        }
        catch { }
    }

    public static bool ConsumeCrashMarker()
    {
        try
        {
            if (!File.Exists(CrashMarkerPath)) return false;
            File.Delete(CrashMarkerPath);
            return true;
        }
        catch { return false; }
    }
}
