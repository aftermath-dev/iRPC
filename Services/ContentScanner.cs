namespace iRPC;

// Walks the iRacing install's cars/ and tracks/ directories and records the relative path
// of every *.dat file found, so every owned car/track codename can be enumerated at once
// without having to load each one in iRacing first.
public static class ContentScanner
{
    private static readonly string OutputPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "iRPC", "content_scan.txt");

    public static string Scan(string iracingRoot)
    {
        var lines = new List<string>();

        foreach (string sub in new[] { "cars", "tracks" })
        {
            string dir = Path.Combine(iracingRoot, sub);
            if (!Directory.Exists(dir)) continue;

            foreach (string file in Directory.EnumerateFiles(dir, "*.dat", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
                lines.Add($"{sub}/{rel}");
            }
        }

        lines.Sort(StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath)!);
        File.WriteAllLines(OutputPath, lines);
        return OutputPath;
    }
}
