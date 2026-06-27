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

    private static StreamWriter? _writer;

    public static bool Enabled
    {
        get => _writer != null;
        set
        {
            lock (_lock)
            {
                if (value == (_writer != null)) return;
                if (value)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    _writer = new StreamWriter(LogPath, append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
                }
                else
                {
                    _writer?.Dispose();
                    _writer = null;
                }
            }
        }
    }

    public static void Log(string message)
    {
        if (_writer is null) return;
        try
        {
            lock (_lock)
                _writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
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
