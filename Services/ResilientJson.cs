using System.Text.Json;

namespace iRPC;

// Shared load/save helper for the plain (unsigned) %AppData%\iRPC json files. Saves atomically
// (write to .tmp then rename) and keep a rolling .bak copy of the last good file; on a bad read,
// falls back to that backup before giving up, and always logs the failure to a sibling
// .load-errors.log so a corrupt file never silently resets to blank with zero trace.
public static class ResilientJson
{
    public static T Load<T>(string filePath) where T : new()
    {
        if (!File.Exists(filePath)) return new();

        if (TryLoad<T>(filePath, out var data, out string? failure))
            return data!;

        LogFailure(filePath, failure!);

        string backupPath = filePath + ".bak";
        if (File.Exists(backupPath) && TryLoad<T>(backupPath, out data, out failure))
        {
            LogFailure(filePath, "recovered from backup after main file failed");
            return data!;
        }
        if (File.Exists(backupPath))
            LogFailure(filePath, failure!);

        // Preserve the unreadable file for inspection instead of silently overwriting it.
        try { File.Copy(filePath, filePath + ".corrupt", overwrite: true); } catch { }

        return new();
    }

    private static bool TryLoad<T>(string path, out T? data, out string? failure) where T : new()
    {
        data = default;
        failure = null;
        try
        {
            data = JsonSerializer.Deserialize<T>(File.ReadAllText(path));
            if (data is null) { failure = "deserialized to null"; return false; }
            return true;
        }
        catch (Exception ex)
        {
            failure = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    public static void Save<T>(string filePath, T data, bool indented = true)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = indented });

            string tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // Roll the current (about-to-be-replaced) good file into the backup slot first, so a
            // failed read always has a known-good fallback to recover from.
            if (File.Exists(filePath))
                File.Copy(filePath, filePath + ".bak", overwrite: true);

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LogFailure(filePath, "save failed: " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void LogFailure(string filePath, string reason)
    {
        try
        {
            File.AppendAllText(filePath + ".load-errors.log",
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {reason}{Environment.NewLine}");
        }
        catch { }
    }
}
