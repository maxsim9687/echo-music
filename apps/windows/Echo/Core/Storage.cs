using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Core;

// Unpackaged apps have no ApplicationData.Current: everything lives in %LocalAppData%\Echo.
public static class Paths
{
    public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Echo");
    public static string Settings => Path.Combine(Dir, "settings.json");
    public static string Profile => Path.Combine(Dir, "profile.json");
    public static string Cookies => Path.Combine(Dir, "cookies.json");
    public static string DownloadsIndex => Path.Combine(Dir, "downloads.json");
    public static string Log => Path.Combine(Dir, "echo.log");
    public static string Temp => Path.Combine(Dir, "tmp");
    public static string Music
    {
        get
        {
            var m = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(m)) m = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
            return Path.Combine(m, "Эхо");
        }
    }

    public static void Init()
    {
        Directory.CreateDirectory(Dir);
        try { Directory.CreateDirectory(Temp); } catch { }
    }

    public static void WriteAtomic(string path, string text)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            File.Move(tmp, path, true);
        }
        catch (Exception e) { Logger.Write("write " + path + ": " + e.Message); }
    }

    public static T? ReadJson<T>(string path) where T : class
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Opts) : null; }
        catch (Exception e) { Logger.Write("read " + path + ": " + e.Message); return null; }
    }
}

public static class Logger
{
    static readonly object L = new();
    public static void Write(string s)
    {
        try
        {
            lock (L)
            {
                var fi = new FileInfo(Paths.Log);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                File.AppendAllText(Paths.Log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}{Environment.NewLine}");
            }
        }
        catch { }
    }
}

public sealed class AppSettings
{
    [JsonPropertyName("theme")] public int Theme { get; set; } = 10;
    [JsonPropertyName("volume")] public double Volume { get; set; } = 80;
    [JsonPropertyName("wave")] public WaveSettings Wave { get; set; } = new();
    [JsonPropertyName("chart")] public string ChartCountry { get; set; } = "global";
    [JsonPropertyName("email")] public string? LastEmail { get; set; }
    [JsonPropertyName("shuffle")] public bool Shuffle { get; set; }
}

public static class Settings
{
    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        Current = Paths.ReadJson<AppSettings>(Paths.Settings) ?? new AppSettings();
        Current.Wave ??= new WaveSettings();
        if (Current.Theme < 1 || Current.Theme > 10) Current.Theme = 10;
    }

    public static void Save() => Paths.WriteAtomic(Paths.Settings, JsonSerializer.Serialize(Current, Json.Pretty));
}
