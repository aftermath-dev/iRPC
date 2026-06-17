namespace iRPC;

public static class Logger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "iRPC.log");
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
}
