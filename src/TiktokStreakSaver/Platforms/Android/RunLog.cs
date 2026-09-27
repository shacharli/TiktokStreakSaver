namespace TiktokStreakSaver.Platforms.Android;

/// <summary>Detailed run log persisted to a file, so it survives new runs and app restarts.</summary>
internal static class RunLog
{
    private const int MaxLines = 1500;
    private const int TrimTo = 1000;
    private static readonly object Gate = new();

    private static string FilePath =>
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir?.AbsolutePath ?? string.Empty, "run_log.txt");

    public static void Append(string line)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(FilePath, line + "\n");
                var lines = File.ReadAllLines(FilePath);
                if (lines.Length > MaxLines)
                    File.WriteAllLines(FilePath, lines[^TrimTo..]);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RunLog.Append failed: {ex.Message}");
        }
    }

    public static List<string> Read()
    {
        try
        {
            lock (Gate)
                return File.Exists(FilePath) ? File.ReadAllLines(FilePath).ToList() : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static void Clear()
    {
        try { lock (Gate) if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch { }
    }
}
