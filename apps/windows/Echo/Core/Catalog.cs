using System.Text.RegularExpressions;

namespace Echo.Core;

// Music sources: Audius (proxied), Jamendo (if the server has a client id) and the site's own uploads.
// Same normalisation as web api.js: fromAudius / fromJamendo / fromLocal, okAudius filter.
public static class Catalog
{
    const string AU = "/api/au";
    public static bool Jamendo { get; set; }

    static readonly HashSet<string> Exclude = new() { "Podcasts", "Spoken Word", "Comedy", "Audiobooks", "Kids", "Devotional" };
    static readonly TimeSpan Ten = TimeSpan.FromMinutes(10), Five = TimeSpan.FromMinutes(5);
    static string Q(string s) => Uri.EscapeDataString(s);
    static string G0(string? g) => (g ?? "").Split(',')[0].Trim();
    static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    public static Track FromAudius(AuTrack t) => new()
    {
        Id = "au:" + t.Id, Src = "audius", RawId = t.Id,
        Title = t.Title ?? "", Artist = string.IsNullOrEmpty(t.User?.Name) ? "Неизвестный артист" : t.User!.Name!, ArtistId = "au:" + (t.User?.Id ?? ""),
        Cover = t.Artwork ? $"{AU}/art/{t.Id}/480" : null,
        CoverSm = t.Artwork ? $"{AU}/art/{t.Id}/150" : null,
        Duration = t.Duration, Genre = NullIfEmpty(G0(t.Genre)), Mood = NullIfEmpty(t.Mood),
        Tags = (t.Tags ?? "").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).Take(8).ToList(),
        Bpm = t.Bpm is > 0 ? t.Bpm : null, Key = NullIfEmpty(t.MusicalKey), ReleaseDate = NullIfEmpty(t.ReleaseDate),
        Plays = t.PlayCount, Downloadable = t.IsDownloadable, Quality = "MP3 320",
        Stream = $"{AU}/stream/{t.Id}",
        Link = string.IsNullOrEmpty(t.Permalink) ? null : "https://audius.co" + t.Permalink,
    };

    static bool OkAudius(AuTrack? t) => t != null && t.IsStreamable != false && !t.IsStreamGated && t.Duration >= 60 && t.Duration <= 1500 && !Exclude.Contains(G0(t.Genre));
    static List<Track> AuL(AuList d) => (d.Data ?? new()).Where(OkAudius).Select(FromAudius).ToList();

    public static async Task<List<Track>> AuTrending(string? genre = null, string time = "week", int limit = 40)
    {
        var url = $"{AU}/tracks/trending?time={time}&limit={limit}" + (string.IsNullOrEmpty(genre) ? "" : "&genre=" + Q(genre));
        return AuL(await Api.Get<AuList>(url, Ten));
    }

    public static async Task<List<Track>> AuUnderground(int limit = 30) =>
        AuL(await Api.Get<AuList>($"{AU}/tracks/trending/underground?limit={limit}", Ten));

    public static async Task<List<Track>> AuSearch(string query, int limit = 30, bool onlyDl = false)
    {
        var url = $"{AU}/tracks/search?query={Q(query)}&limit={limit}" + (onlyDl ? "&only_downloadable=true" : "");
        return AuL(await Api.Get<AuList>(url, Five));
    }

    public static async Task<List<Track>> AuArtist(string artistId, int limit = 20)
    {
        var id = artistId.StartsWith("au:") ? artistId[3..] : artistId;
        if (string.IsNullOrEmpty(id)) return new();
        return AuL(await Api.Get<AuList>($"{AU}/users/{Q(id)}/tracks?limit={limit}", Ten));
    }

    // ---- Jamendo ----
    public static string JTag(string? genre) => Regex.Replace((genre ?? "").ToLowerInvariant().Replace("&", "and"), "[^a-z0-9]", "");

    public static Track FromJamendo(JaTrack t)
    {
        var genres = t.MusicInfo?.Tags?.Genres ?? new();
        var vartags = t.MusicInfo?.Tags?.Vartags ?? new();
        return new Track
        {
            Id = "ja:" + t.Id, Src = "jamendo", RawId = t.Id,
            Title = t.Name ?? "", Artist = t.ArtistName ?? "", ArtistId = "ja:" + t.ArtistId,
            Cover = $"/api/ja/art/{t.Id}", CoverSm = $"/api/ja/art/{t.Id}",
            Duration = t.Duration, Genre = genres.Count > 0 && genres[0].Length > 0 ? char.ToUpperInvariant(genres[0][0]) + genres[0][1..] : null,
            Tags = genres.Concat(vartags).Select(s => s.ToLowerInvariant()).Take(8).ToList(),
            Plays = 0, Instrumental = t.MusicInfo?.VocalInstrumental == "instrumental",
            ReleaseDate = NullIfEmpty(t.ReleaseDate),
            Downloadable = t.AudioDownloadAllowed && !string.IsNullOrEmpty(t.AudioDownload), Quality = "FLAC",
            Stream = $"/api/ja/stream/{t.Id}", DlOrig = $"/api/ja/dl/{t.Id}", DlMp3 = $"/api/ja/stream/{t.Id}", Link = NullIfEmpty(t.ShareUrl),
        };
    }

    static async Task<List<Track>> JamendoQ(Dictionary<string, string> p)
    {
        if (!Jamendo) return new();
        if (!p.ContainsKey("limit")) p["limit"] = "30";
        var url = "/api/ja/tracks?" + string.Join("&", p.Select(kv => kv.Key + "=" + Q(kv.Value)));
        var d = await Api.Get<JaList>(url, Ten);
        return (d.Results ?? new()).Where(t => !string.IsNullOrEmpty(t.Audio) && t.Duration >= 60).Select(FromJamendo).ToList();
    }

    public static Task<List<Track>> JaSearch(string query, int limit = 30) =>
        JamendoQ(new() { ["search"] = query, ["limit"] = limit.ToString(), ["order"] = "relevance" });

    public static Task<List<Track>> JaPopular(string? tags = null, int limit = 30, bool instrumental = false)
    {
        var p = new Dictionary<string, string> { ["limit"] = limit.ToString(), ["order"] = "popularity_month" };
        if (!string.IsNullOrEmpty(tags)) p["fuzzytags"] = tags;
        if (instrumental) p["vocalinstrumental"] = "instrumental";
        return JamendoQ(p);
    }

    // ---- own uploads ----
    public static Track FromLocal(Upload u)
    {
        var ext = string.IsNullOrEmpty(u.Ext) ? "mp3" : u.Ext;
        var artist = u.Artist ?? "";
        return new Track
        {
            Id = "lo:" + u.Id, Src = "local", RawId = u.Id, Title = u.Title ?? "", Artist = artist, ArtistId = "lo:" + artist.ToLowerInvariant(),
            Cover = u.Cover ? $"/media/{u.Id}/cover" : null, CoverSm = u.Cover ? $"/media/{u.Id}/cover" : null,
            Duration = u.Duration, Genre = NullIfEmpty(u.Genre), Mood = NullIfEmpty(u.Mood),
            Tags = string.IsNullOrEmpty(u.Genre) ? new() : new() { u.Genre.ToLowerInvariant() },
            Bpm = u.Bpm is > 0 ? u.Bpm : null, Plays = 0, Downloadable = true, Quality = ext.ToUpperInvariant(), Ext = ext,
            Stream = $"/media/{u.Id}", DlOrig = $"/media/{u.Id}?download=1", Size = u.Size,
        };
    }

    public static async Task<List<Track>> LocalTracks(string? q = null, bool fresh = false)
    {
        try
        {
            var url = string.IsNullOrEmpty(q) ? "/api/uploads" : "/api/uploads?q=" + Q(q);
            var d = await Api.Get<UploadList>(url, fresh ? TimeSpan.Zero : TimeSpan.FromSeconds(60));
            return (d.Data ?? new()).Select(FromLocal).ToList();
        }
        catch { return new(); }
    }

    // ---- combined search: Audius + Jamendo + uploads in parallel ----
    public static async Task<(List<Track> list, bool failed)> SearchAll(string query, string src = "all", bool onlyDl = false)
    {
        var jobs = new List<Task<List<Track>>>();
        if (src is "all" or "audius") jobs.Add(AuSearch(query, 40, onlyDl));
        if (src is "all" or "jamendo" && Jamendo) jobs.Add(JaSearch(query, 30));
        if (src is "all" or "local")
            jobs.Add(LocalUploadSearch(query));
        try { await Task.WhenAll(jobs); } catch { }
        var ok = jobs.Where(j => j.Status == TaskStatus.RanToCompletion).ToList();
        var list = ok.SelectMany(j => j.Result).ToList();
        if (onlyDl) list = list.Where(t => t.Downloadable).ToList();
        return (Dedupe(list), jobs.Count > 0 && ok.Count == 0);
    }

    static async Task<List<Track>> LocalUploadSearch(string query)
    {
        var l = await LocalTracks(query);
        var q = query.ToLowerInvariant();
        // the server filters by q; keep the web's client-side check as a fallback for older servers
        return l.Where(t => $"{t.Title} {t.Artist} {t.Genre} {t.Mood}".ToLowerInvariant().Contains(q)).ToList();
    }

    public static List<Track> Dedupe(IEnumerable<Track> list)
    {
        var seen = new HashSet<string>();
        return list.Where(t => t != null && seen.Add(t.Id)).ToList();
    }

    // Saved likes/history may hold URLs from older versions: rebuild them for the current routes.
    public static Track Refresh(Track t)
    {
        if (t.Src == "audius" && !string.IsNullOrEmpty(t.RawId))
        {
            t.Stream = $"{AU}/stream/{t.RawId}";
            if (!string.IsNullOrEmpty(t.Cover) && !t.Cover.StartsWith('/')) { t.Cover = $"{AU}/art/{t.RawId}/480"; t.CoverSm = $"{AU}/art/{t.RawId}/150"; }
        }
        if (t.Src == "jamendo" && !string.IsNullOrEmpty(t.RawId))
        {
            t.Stream = t.DlMp3 = $"/api/ja/stream/{t.RawId}"; t.DlOrig = $"/api/ja/dl/{t.RawId}"; t.Cover = t.CoverSm = $"/api/ja/art/{t.RawId}";
        }
        return t;
    }

    public static List<Track> RefreshAll(IEnumerable<Track> l) => l.Select(t => Refresh(t.Clone())).ToList();
}
