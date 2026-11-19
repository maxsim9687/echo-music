using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using NAudio.Wave;

namespace Echo.Core;

public sealed record DlOption(string Label, string Note, string Fmt, string Url, bool Convert = false, int Bits = 16, bool Direct = false);

public sealed class DlEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("fmt")] public string Fmt { get; set; } = "";
    [JsonPropertyName("at")] public long At { get; set; }
    [JsonPropertyName("track")] public Track? Track { get; set; }
}

// Downloads in the best quality the source gives (same options as web download.js) into Music\Эхо.
// WAV is decoded locally at the source sample rate: NAudio MediaFoundationReader → WaveFileWriter.
public static class Downloads
{
    public static string Folder => Paths.Music;
    static Dictionary<string, DlEntry> index = new();
    static readonly object L = new();
    public static event Action? Changed;
    static readonly string[] AudioExt = { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac", ".opus" };

    public static void Load()
    {
        index = Paths.ReadJson<Dictionary<string, DlEntry>>(Paths.DownloadsIndex) ?? new();
    }
    static void SaveIndex()
    {
        lock (L) Paths.WriteAtomic(Paths.DownloadsIndex, JsonSerializer.Serialize(index, Json.Opts));
    }

    public static List<DlOption> Options(Track? t)
    {
        if (t == null || !t.Downloadable) return new();
        if (t.Src == "audius") return new()
        {
            new("MP3", "320 kbps, как у автора", "mp3", t.Stream ?? ""),
            new("WAV", "без потерь из MP3 320", "wav", t.Stream ?? "", Convert: true),
        };
        if (t.Src == "jamendo") return new()
        {
            new("FLAC", "оригинал без потерь", "flac", t.DlOrig ?? ""),
            new("WAV", "из FLAC, 24 бит", "wav", t.DlOrig ?? "", Convert: true, Bits: 24),
            new("MP3", "для телефона", "mp3", t.DlMp3 ?? ""),
        };
        if (t.Src == "local")
        {
            var ext = (t.Ext ?? "mp3").ToLowerInvariant();
            var o = new List<DlOption> { new(ext.ToUpperInvariant(), "оригинал, как загружен", ext, t.DlOrig ?? "", Direct: true) };
            if (ext != "wav") o.Add(new("WAV", $"из {ext.ToUpperInvariant()}", "wav", t.Stream ?? "", Convert: true, Bits: ext == "mp3" ? 16 : 24));
            return o;
        }
        return new();
    }

    static string Safe(string s)
    {
        var bad = "\\/:*?\"<>|".ToCharArray().Concat(Path.GetInvalidFileNameChars()).ToHashSet();
        var r = new string(s.Where(c => !bad.Contains(c)).ToArray()).Trim();
        if (r.Length > 150) r = r[..150];
        return r.Length == 0 ? "track" : r;
    }

    public static string FileNameFor(Track t, string fmt) => $"{Safe(t.Artist)} - {Safe(t.Title)}.{fmt}";

    public static async Task<string> Download(Track t, DlOption o, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Folder);
        var dest = Path.Combine(Folder, FileNameFor(t, o.Fmt));
        var srcExt = o.Convert ? (t.Src == "jamendo" ? "flac" : t.Src == "local" ? (t.Ext ?? "mp3") : "mp3") : o.Fmt;
        Directory.CreateDirectory(Paths.Temp);
        var tmp = Path.Combine(Paths.Temp, Guid.NewGuid().ToString("N") + "." + srcExt);
        try
        {
            await Fetch(o.Url, tmp, p => progress?.Report(o.Convert ? p * 0.8 : p), ct);
            if (!o.Convert)
            {
                File.Move(tmp, dest, true);
            }
            else
            {
                var bits = o.Bits;
                await Task.Run(() => ToWav(tmp, dest, bits, p => progress?.Report(0.8 + p * 0.2)), ct);
            }
            progress?.Report(1);
            lock (L) index[t.Id] = new DlEntry { Path = dest, Fmt = o.Fmt, At = Recs.Now, Track = Recs.Slim(t) };
            SaveIndex();
            Bus.Ui(() => Changed?.Invoke());
            return dest;
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    static async Task Fetch(string url, string path, Action<double> progress, CancellationToken ct)
    {
        using var r = await Api.Http.GetAsync(Api.Abs(url), HttpCompletionOption.ResponseHeadersRead, ct);
        if (!r.IsSuccessStatusCode) throw new ApiException($"HTTP {(int)r.StatusCode}", (int)r.StatusCode);
        var total = r.Content.Headers.ContentLength ?? 0;
        await using var s = await r.Content.ReadAsStreamAsync(ct);
        await using var f = File.Create(path);
        var buf = new byte[1 << 16];
        long got = 0; int n;
        var last = DateTime.UtcNow;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            await f.WriteAsync(buf.AsMemory(0, n), ct);
            got += n;
            if (total > 0 && (DateTime.UtcNow - last).TotalMilliseconds > 100) { last = DateTime.UtcNow; progress((double)got / total); }
        }
        if (got == 0) throw new ApiException("Пустой файл", 0);
    }

    // Decodes with Media Foundation (MP3, FLAC, AAC…) at the file's own sample rate and writes PCM WAV.
    public static void ToWav(string src, string dest, int bits, Action<double>? progress = null)
    {
        using var reader = new MediaFoundationReader(src, new MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true });
        var sp = reader.ToSampleProvider();
        var fmt = new WaveFormat(sp.WaveFormat.SampleRate, bits, sp.WaveFormat.Channels);
        var tmp = dest + ".part";
        using (var w = new WaveFileWriter(tmp, fmt))
        {
            var buf = new float[sp.WaveFormat.SampleRate * sp.WaveFormat.Channels];
            int n;
            long len = Math.Max(1, reader.Length);
            while ((n = sp.Read(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i < n; i++) buf[i] = Math.Clamp(buf[i], -1f, 1f);
                w.WriteSamples(buf, 0, n);
                progress?.Invoke(Math.Min(1, (double)reader.Position / len));
            }
        }
        File.Move(tmp, dest, true);
    }

    public static string? LocalPathFor(Track t)
    {
        lock (L)
            if (index.TryGetValue(t.Id, out var e) && File.Exists(e.Path)) return e.Path;
        return null;
    }

    public static bool IsDownloaded(Track t) => LocalPathFor(t) != null;

    // Everything in «Скачанное»: files we saved (with their track info) plus any other audio in Music\Эхо.
    public static List<Track> Downloaded()
    {
        var list = new List<Track>();
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<KeyValuePair<string, DlEntry>> entries;
        lock (L) entries = index.OrderByDescending(kv => kv.Value.At).ToList();
        foreach (var (id, e) in entries)
        {
            if (!File.Exists(e.Path) || e.Track == null) continue;
            known.Add(e.Path);
            var t = e.Track.Clone();
            t.Quality = e.Fmt.ToUpperInvariant();
            list.Add(t);
        }
        try
        {
            if (Directory.Exists(Folder))
                foreach (var f in Directory.EnumerateFiles(Folder).Where(f => AudioExt.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderByDescending(File.GetLastWriteTimeUtc))
                {
                    if (known.Contains(f)) continue;
                    var name = Path.GetFileNameWithoutExtension(f);
                    var parts = name.Split(" - ", 2);
                    list.Add(new Track
                    {
                        Id = "fi:" + f.ToLowerInvariant(), Src = "file", Title = parts.Length > 1 ? parts[1] : name, Artist = parts.Length > 1 ? parts[0] : "Файл",
                        ArtistId = "fi:" + (parts.Length > 1 ? parts[0].ToLowerInvariant() : ""), Stream = f, Quality = Path.GetExtension(f).TrimStart('.').ToUpperInvariant(),
                    });
                }
        }
        catch (Exception e) { Logger.Write("scan downloads: " + e.Message); }
        return list;
    }

    public static void OpenFolder(string? select = null)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (select != null && File.Exists(select)) Process.Start("explorer.exe", $"/select,\"{select}\"");
            else Process.Start("explorer.exe", $"\"{Folder}\"");
        }
        catch (Exception e) { Logger.Write("open folder: " + e.Message); }
    }
}
