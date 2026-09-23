namespace TiktokStreakSaver.Platforms.Android;

internal static class CrashLog
{
    private static string FilePath =>
        System.IO.Path.Combine(global::Android.App.Application.Context.FilesDir?.AbsolutePath ?? string.Empty, "last_crash.txt");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Write("Task", e.Exception);
        global::Android.Runtime.AndroidEnvironment.UnhandledExceptionRaiser += (_, e) => Write("Android", e.Exception);
    }

    public static void Write(string source, Exception? ex)
    {
        try { File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n"); }
        catch { }
    }

    public static string? TakeLast()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var text = File.ReadAllText(FilePath);
            File.Delete(FilePath);
            return text.Length > 3000 ? text[^3000..] : text;
        }
        catch { return null; }
    }
}
