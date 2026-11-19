using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Echo.Core;

// Recommendation engine v2 («Моя волна 2»), see apps/SPEC.md. Profile keys and shape are shared with web recs.js.

public sealed class MoodPreset
{
    public required string[] Genres { get; init; }
    public required string[] Moods { get; init; }
    public required double BpmMin { get; init; }
    public required double BpmMax { get; init; }
    public required string[] Queries { get; init; }
    public string[]? Tags { get; init; }
}

public sealed class ScoreCtx
{
    public string? Mood { get; init; }
    public string? Genre { get; init; }          // genre wave started from «Жанры и настроения»
    public WaveSettings? Cfg { get; init; }
    public AiPlan? Plan { get; init; }
    public double? Explore { get; init; }        // noise width; lowered for mixes, AI playlists and charts
    public Track? Current { get; init; }         // for bpm / key flow
    public IReadOnlyList<Track>? Recent { get; init; } // tail of the queue for the re-rank rules
}

public static class Recs
{
    public static readonly (string Name, MoodPreset P)[] Moods =
    {
        ("Энергия", new MoodPreset { Genres = new[] { "Electronic", "Drum & Bass", "House", "Trap", "Hip-Hop/Rap" }, Moods = new[] { "Energizing", "Upbeat", "Excited", "Fiery", "Empowering" }, BpmMin = 118, BpmMax = 180, Queries = new[] { "energy", "workout" } }),
        ("Спокойствие", new MoodPreset { Genres = new[] { "Ambient", "Lo-Fi", "Acoustic", "Jazz" }, Moods = new[] { "Peaceful", "Easygoing", "Tender", "Cool" }, BpmMin = 55, BpmMax = 100, Queries = new[] { "chill", "calm" } }),
        ("Фокус", new MoodPreset { Genres = new[] { "Lo-Fi", "Ambient", "Electronic", "Classical" }, Moods = new[] { "Peaceful", "Cool", "Sophisticated", "Serious" }, BpmMin = 65, BpmMax = 110, Queries = new[] { "lofi study", "focus" } }),
        ("Ночь", new MoodPreset { Genres = new[] { "Deep House", "Techno", "Downtempo", "R&B/Soul" }, Moods = new[] { "Brooding", "Cool", "Sensual", "Melancholy" }, BpmMin = 88, BpmMax = 126, Queries = new[] { "night", "late night" } }),
        ("Грусть", new MoodPreset { Genres = new[] { "Alternative", "Acoustic", "Ambient", "Lo-Fi" }, Moods = new[] { "Melancholy", "Sentimental", "Yearning" }, BpmMin = 55, BpmMax = 105, Queries = new[] { "sad", "melancholy" } }),
        ("Phonk", new MoodPreset { Genres = new[] { "Trap", "Hip-Hop/Rap", "Electronic" }, Moods = new[] { "Aggressive", "Fiery", "Defiant", "Energizing", "Gritty" }, BpmMin = 115, BpmMax = 175, Queries = new[] { "phonk", "memphis phonk", "phonk house", "dark phonk" }, Tags = new[] { "phonk", "memphis", "cowbell" } }),
        ("Drift Phonk", new MoodPreset { Genres = new[] { "Trap", "Electronic", "Hip-Hop/Rap" }, Moods = new[] { "Aggressive", "Energizing", "Fiery" }, BpmMin = 110, BpmMax = 170, Queries = new[] { "drift phonk", "phonk drift", "brazilian phonk", "funk phonk" }, Tags = new[] { "driftphonk", "drift", "phonk", "brazilianphonk" } }),
    };
    public static MoodPreset? Preset(string? name) => name == null ? null : Moods.FirstOrDefault(m => m.Name == name).P;

    public static readonly string[] StarterGenres = { "Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Rock", "Pop", "Ambient", "Alternative", "Drum & Bass", "Techno", "Jazz", "R&B/Soul", "Trap", "Classical" };
    public static readonly string[] GenreList = { "Electronic", "Hip-Hop/Rap", "Lo-Fi", "House", "Deep House", "Techno", "Drum & Bass", "Ambient", "Pop", "Rock", "Alternative", "Jazz", "R&B/Soul", "Classical", "Acoustic", "Trap", "Downtempo", "Synthwave" };
    public static readonly string[] MoodList = { "Peaceful", "Easygoing", "Cool", "Melancholy", "Energizing", "Upbeat", "Fiery", "Romantic", "Brooding", "Sophisticated" };
    static readonly Dictionary<string, (double min, double max)> Tempo = new() { ["slow"] = (0, 95), ["mid"] = (95, 125), ["fast"] = (125, 999) };
    static readonly Regex InstrRe = new("instrumental|lofi|lo-fi|ambient|beat", RegexOptions.Compiled);

    public static Profile P { get; private set; } = new();
    public static event Action? Changed;
    static readonly Random Rnd = new();
    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // ---------------- persistence ----------------
    public static void Load()
    {
        P = Paths.ReadJson<Profile>(Paths.Profile)?.Fix() ?? new Profile();
        Decay();
    }
    static void Write() => Paths.WriteAtomic(Paths.Profile, JsonSerializer.Serialize(P, Json.Opts));
    public static void Save() { P.Updated = Now; Write(); Changed?.Invoke(); }
    public static void Replace(Profile p) { P = p.Fix(); Write(); }
    public static void Reset()
    {
        var keep = new Profile { Likes = P.Likes, Playlists = P.Playlists };
        P = keep; ResetSession(); Save();
    }
    public static void Clear()
    {
        P = new Profile(); ResetSession();
        try { File.Delete(Paths.Profile); } catch { }
    }

    // ---------------- decay: halve every 30 days ----------------
    public static void Decay()
    {
        var now = Now;
        if (P.DecayAt <= 0) { P.DecayAt = now; return; }
        var dt = now - P.DecayAt;
        if (dt < 60_000) return;
        var f = Math.Pow(0.5, dt / (30.0 * 86400e3));
        foreach (var map in new[] { P.G, P.M, P.A, P.T })
            foreach (var k in map.Keys.ToList())
            {
                var v = map[k] * f;
                if (Math.Abs(v) < 0.005) map.Remove(k); else map[k] = v;
            }
        P.DecayAt = now;
        if (dt > 3_600_000) Write(); // local only: decay is not a user change
    }

    // ---------------- learning ----------------
    static double Clamp(double v) => Math.Max(-25, Math.Min(40, v));
    static double V(Dictionary<string, double> m, string? k) => !string.IsNullOrEmpty(k) && m.TryGetValue(k, out var v) ? v : 0;
    static void Bump(Dictionary<string, double> m, string? k, double v) { if (!string.IsNullOrEmpty(k)) m[k] = Clamp(V(m, k) + v); }

    public static void Learn(Track t, double w)
    {
        Decay();
        Bump(P.G, t.Genre, w);
        Bump(P.M, t.Mood, w * 0.7);
        Bump(P.A, t.ArtistId, w * 0.8);
        if (!string.IsNullOrEmpty(t.ArtistId)) P.An[t.ArtistId] = t.Artist;
        foreach (var tag in t.TagList.Take(4)) Bump(P.T, tag, w * 0.3);
        Save();
    }

    static string? E(string? s) => string.IsNullOrEmpty(s) ? null : s;
    public static Track Slim(Track t) => new()
    {
        Id = t.Id, Src = t.Src, RawId = E(t.RawId), Title = t.Title, Artist = t.Artist, ArtistId = t.ArtistId,
        Cover = E(t.Cover), CoverSm = E(t.CoverSm), Duration = t.Duration, Genre = E(t.Genre), Mood = E(t.Mood),
        Tags = t.Tags?.Take(6).ToList(), Bpm = t.Bpm, Key = E(t.Key), Quality = E(t.Quality), Downloadable = t.Downloadable,
        Ext = E(t.Ext), Stream = E(t.Stream), DlOrig = E(t.DlOrig), DlMp3 = E(t.DlMp3), Instrumental = t.Instrumental,
    };

    // like +3 · unlike −2
    public static bool Like(Track t)
    {
        if (P.Likes.Remove(t.Id)) { Learn(t, -2); return false; }
        var s = Slim(t); s.LikedAt = Now;
        P.Likes[t.Id] = s;
        Session(t, 1);
        Learn(t, 3);
        return true;
    }
    public static bool IsLiked(Track? t) => t != null && P.Likes.ContainsKey(t.Id);

    // «Не то»: −5, ban the track; the second «Не то» for one artist bans the artist
    public static void NotThis(Track t)
    {
        P.Ban[t.Id] = 1;
        if (!string.IsNullOrEmpty(t.ArtistId))
        {
            var n = V(P.BanN, t.ArtistId) + 1;
            P.BanN[t.ArtistId] = n;
            if (n >= 2) P.BanA[t.ArtistId] = 1;
        }
        Session(t, -1.5);
        Learn(t, -5);
    }

    // skip <10 s −1.5 · skip 10–30 s −0.8
    public static void Skipped(Track t, double pos)
    {
        P.Skip[t.Id] = Now;
        if (P.Skip.Count > 600)
            foreach (var k in P.Skip.OrderBy(kv => kv.Value).Take(P.Skip.Count - 500).Select(kv => kv.Key).ToList()) P.Skip.Remove(k);
        Session(t, -1.5);
        Learn(t, pos < 10 ? -1.5 : -0.8);
    }

    // finished (≥90 %) +1.5
    public static void Finished(Track t) { Session(t, 1); Learn(t, 1.5); }

    // listened >30 s +0.5, goes to history
    public static void Listened(Track t)
    {
        var s = Slim(t); s.At = Now;
        P.Hist.RemoveAll(h => h.Id == t.Id);
        P.Hist.Insert(0, s);
        if (P.Hist.Count > 100) P.Hist.RemoveRange(100, P.Hist.Count - 100);
        Session(t, 1);
        Learn(t, 0.5);
    }

    public static void SeedGenre(string g, bool on) { Bump(P.G, g, on ? 4 : -4); Save(); }
    public static void AddFavArtist(string id, string name, string? genre)
    {
        P.Fav[id] = name; P.An[id] = name; Bump(P.A, id, 8);
        if (!string.IsNullOrEmpty(genre)) Bump(P.G, genre, 2);
        Save();
    }
    public static void RemoveFavArtist(string id) { P.Fav.Remove(id); Bump(P.A, id, -8); Save(); }

    // ---------------- session vector (in memory, 30 min idle resets it) ----------------
    sealed class SessEntry { public string Id = "", Genre = "", Mood = "", Artist = ""; public double W; }
    static readonly List<SessEntry> Sess = new();
    static readonly HashSet<string> Played = new();
    static long lastAct;

    static void Expire()
    {
        if (lastAct > 0 && Now - lastAct > 30 * 60_000) { Sess.Clear(); Played.Clear(); lastAct = 0; }
    }
    static void Touch() { Expire(); lastAct = Now; }
    public static void ResetSession() { Sess.Clear(); Played.Clear(); lastAct = 0; }
    public static void MarkPlayed(Track t) { Touch(); Played.Add(t.Id); }
    public static bool PlayedThisSession(Track t) => Played.Contains(t.Id);

    static void Session(Track t, double w)
    {
        Touch();
        var e = Sess.Find(x => x.Id == t.Id);
        if (e != null) Sess.Remove(e);
        else e = new SessEntry { Id = t.Id, Genre = t.Genre ?? "", Mood = t.Mood ?? "", Artist = t.ArtistId ?? "" };
        e.W = w;
        Sess.Add(e);
        while (Sess.Count > 8) Sess.RemoveAt(0);
    }

    public static double SessionScore(Track t)
    {
        Expire();
        double s = 0;
        foreach (var e in Sess)
        {
            if (e.Genre.Length > 0 && e.Genre == t.Genre) s += e.W;
            if (e.Mood.Length > 0 && e.Mood == t.Mood) s += e.W;
            if (e.Artist.Length > 0 && e.Artist == t.ArtistId) s += e.W;
        }
        return s * 1.5;
    }

    // ---------------- top lists ----------------
    static List<string> Top(Dictionary<string, double> map, int n, double min) =>
        map.Where(kv => kv.Value > min).OrderByDescending(kv => kv.Value).Take(n).Select(kv => kv.Key).ToList();
    public static List<string> TopGenres(int n = 3) => Top(P.G, n, 0.5);
    public static List<string> TopArtists(int n = 3) => Top(P.A, n, 1.5);
    public static List<string> TopTags(int n = 3) => Top(P.T, n, 0.8);
    public static List<string> TopMoods(int n = 3) => Top(P.M, n, 0.5);
    public static bool HasTaste() => TopGenres(1).Count > 0 || P.Likes.Count > 0 || P.Fav.Count > 0;
    public static List<KeyValuePair<string, double>> TasteBars(Dictionary<string, double> map, int n = 6) =>
        map.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(n).ToList();
    public static bool IsDiscovery(Track t) => !string.IsNullOrEmpty(t.Genre) && Math.Abs(V(P.G, t.Genre)) < 1;

    // ---------------- merge with the server copy: nothing liked or saved is lost ----------------
    public static void Merge(Profile? server)
    {
        if (server == null) return;
        server.Fix();
        Profile b, o;
        if (server.Updated > P.Updated) { b = server; o = P; } else { b = P; o = server; }
        foreach (var kv in o.Likes) b.Likes.TryAdd(kv.Key, kv.Value);
        foreach (var kv in o.Fav) b.Fav.TryAdd(kv.Key, kv.Value);
        foreach (var kv in o.An) b.An.TryAdd(kv.Key, kv.Value);
        var ids = new HashSet<string>(b.Playlists.Select(x => x.Id));
        b.Playlists = b.Playlists.Concat(o.Playlists.Where(x => !ids.Contains(x.Id))).Take(40).ToList();
        var seen = new HashSet<string>();
        b.Hist = b.Hist.Concat(o.Hist).OrderByDescending(h => h.At ?? 0).Where(h => seen.Add(h.Id)).Take(120).ToList();
        foreach (var (bm, om) in new[] { (b.G, o.G), (b.M, o.M), (b.A, o.A), (b.T, o.T), (b.K, o.K) })
            foreach (var kv in om) bm.TryAdd(kv.Key, kv.Value);
        foreach (var (bm, om) in new[] { (b.Ban, o.Ban), (b.BanA, o.BanA), (b.BanN, o.BanN), (b.Skip, o.Skip) })
            foreach (var kv in om) bm.TryAdd(kv.Key, kv.Value);
        if (b.DecayAt <= 0) b.DecayAt = Math.Max(o.DecayAt, 0);
        P = b;
        Decay();
        Save();
    }

    // ---------------- scoring ----------------
    public static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)) return d.UtcDateTime;
        var m = Regex.Match(s, @"(\d{4})-(\d{2})-(\d{2})");
        if (m.Success && DateTime.TryParseExact(m.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d2)) return d2.ToUniversalTime();
        return null;
    }
    static bool IsFresh(Track t)
    {
        var d = ParseDate(t.ReleaseDate);
        return d != null && (DateTime.UtcNow - d.Value).TotalDays <= 30 && (DateTime.UtcNow - d.Value).TotalDays >= -2;
    }

    // Everything except the bpm/key flow, which depends on the neighbour in the queue.
    public static double BaseScore(Track t, ScoreCtx ctx)
    {
        if (P.Ban.ContainsKey(t.Id)) return -1e9;
        if (!string.IsNullOrEmpty(t.ArtistId) && P.BanA.ContainsKey(t.ArtistId)) return -1e9;
        var cfg = ctx.Cfg ?? Settings.Current.Wave;
        if (cfg.OnlyDl && !t.Downloadable) return -1e9;

        var fam = 1.5 - cfg.Novelty / 100.0; // 1.5 = stick to what you love, 0.5 = mostly new
        var taste = V(P.G, t.Genre) + 0.8 * V(P.M, t.Mood) + 0.9 * V(P.A, t.ArtistId);
        foreach (var tag in t.TagList) taste += 0.3 * V(P.T, tag);
        var s = fam * taste;
        s += SessionScore(t);
        s += Math.Log10((t.Plays ?? 0) + 10) * (0.5 + (cfg.Popularity - 50) / 30.0);
        if (IsFresh(t)) s += 1.5 * cfg.Novelty / 100.0;
        if (P.Likes.ContainsKey(t.Id)) s += cfg.Novelty < 40 ? 2.5 : cfg.Novelty > 70 ? -4 : 0;

        // tempo / vocals / mood presets exactly as web recs.js score()
        var text = (t.Title + " " + string.Join(" ", t.TagList)).ToLowerInvariant();
        if (Tempo.TryGetValue(cfg.Tempo ?? "any", out var range) && t.Bpm is double b0 && b0 > 0) s += b0 >= range.min && b0 < range.max ? 3 : -4;
        var instr = t.Instrumental == true || InstrRe.IsMatch(text);
        if (cfg.Vocals == "instrumental") s += instr ? 3 : -2.5;
        if (cfg.Vocals == "vocal") s += instr ? -3 : 1;
        var pre = Preset(ctx.Mood);
        if (pre != null)
        {
            if (t.Genre != null && pre.Genres.Contains(t.Genre)) s += pre.Tags != null ? 1.5 : 4;
            if (t.Mood != null && pre.Moods.Contains(t.Mood)) s += 3;
            if (pre.Tags != null) { if (pre.Tags.Any(x => text.Contains(x))) s += 9; else s -= 4; }
            if (t.Bpm is double b1 && b1 > 0) s += b1 >= pre.BpmMin && b1 <= pre.BpmMax ? 2.5 : -2;
        }
        if (!string.IsNullOrEmpty(ctx.Genre))
        {
            var g = ctx.Genre.ToLowerInvariant();
            s += string.Equals(t.Genre, ctx.Genre, StringComparison.OrdinalIgnoreCase) || t.TagList.Contains(g) || t.TagList.Contains(Catalog.JTag(g)) ? 5 : -3;
        }
        if (ctx.Plan != null)
        {
            var pl = ctx.Plan;
            if (t.Genre != null && pl.Genres.Contains(t.Genre)) s += 4;
            if (t.Mood != null && pl.Moods.Contains(t.Mood)) s += 3;
            if (pl.BpmMin is double mn && mn > 0 && t.Bpm is double b2 && b2 > 0) s += b2 >= mn && b2 <= (pl.BpmMax is double mx && mx > 0 ? mx : 999) ? 2.5 : -2.5;
            if (pl.Instrumental && t.Instrumental == true) s += 2;
        }

        // − played in last 3 days 6 · played this session 20 · in skip list 3
        var h = P.Hist.Find(x => x.Id == t.Id);
        if (h?.At is long at && Now - at < 3 * 86400_000L) s -= 6;
        if (Played.Contains(t.Id)) s -= 20;
        if (P.Skip.ContainsKey(t.Id)) s -= 3;
        s += (Rnd.NextDouble() - 0.5) * (ctx.Explore ?? (0.6 + cfg.Novelty / 100.0 * 4.5));
        return s;
    }

    // flow: −min(|Δbpm|, 40)/10, +1.2 for Camelot-compatible keys
    public static double Flow(Track? prev, Track t)
    {
        if (prev == null) return 0;
        double s = 0;
        if (prev.Bpm is double a && a > 0 && t.Bpm is double b && b > 0) s -= Math.Min(Math.Abs(a - b), 40) / 10.0;
        if (Camelot.Compatible(prev.Key, t.Key)) s += 1.2;
        return s;
    }

    public static double Score(Track t, ScoreCtx ctx) => BaseScore(t, ctx) + Flow(ctx.Current, t);

    // Greedy re-rank: no artist repeat within the last 8 queued tracks, max `perArtist` per batch,
    // genre streak of 3 → −2 for the same genre next, every 5th slot is a discovery candidate if one exists.
    public static List<Track> Pick(IEnumerable<Track> cands, int n, ScoreCtx ctx, int perArtist = 2)
    {
        Decay();
        var seen = new HashSet<string>();
        var ranked = new List<(Track t, double s)>();
        foreach (var t in cands)
        {
            if (t == null || !seen.Add(t.Id)) continue;
            var s = BaseScore(t, ctx);
            if (s > -1e8) ranked.Add((t, s));
        }
        var recentArtists = (ctx.Recent ?? Array.Empty<Track>()).TakeLast(8).Select(x => x.ArtistId ?? "").ToList();
        var recentGenres = (ctx.Recent ?? Array.Empty<Track>()).TakeLast(3).Select(x => x.Genre ?? "").ToList();
        var prev = ctx.Current ?? ctx.Recent?.LastOrDefault();
        var cnt = new Dictionary<string, int>();
        var used = new HashSet<string>();
        var outp = new List<Track>();

        for (var slot = 0; slot < n; slot++)
        {
            var wantDisc = (slot + 1) % 5 == 0;
            string? streak = recentGenres.Count >= 3 && recentGenres.TakeLast(3).Distinct().Count() == 1 && recentGenres[^1].Length > 0 ? recentGenres[^1] : null;
            var last8 = new HashSet<string>(recentArtists.TakeLast(8));
            var lastArtist = recentArtists.Count > 0 ? recentArtists[^1] : null;

            Track? best = null, bestDisc = null, relaxed = null;
            double bv = double.MinValue, dv = double.MinValue, rv = double.MinValue;
            foreach (var (t, s) in ranked)
            {
                if (used.Contains(t.Id)) continue;
                var ar = t.ArtistId ?? "";
                if (cnt.GetValueOrDefault(ar) >= perArtist) continue;
                var v = s + Flow(prev, t) - (streak != null && t.Genre == streak ? 2 : 0);
                if (ar != lastArtist && v > rv) { rv = v; relaxed = t; }
                if (last8.Contains(ar)) continue;
                if (v > bv) { bv = v; best = t; }
                if (wantDisc && IsDiscovery(t) && v > dv) { dv = v; bestDisc = t; }
            }
            var pick = (wantDisc && bestDisc != null ? bestDisc : best) ?? relaxed;
            if (pick == null) break;
            outp.Add(pick);
            used.Add(pick.Id);
            var pa = pick.ArtistId ?? "";
            cnt[pa] = cnt.GetValueOrDefault(pa) + 1;
            recentArtists.Add(pa);
            recentGenres.Add(pick.Genre ?? "");
            prev = pick;
        }
        return outp;
    }
}

// Camelot wheel: same key, ±1 on the wheel, or the relative major/minor are compatible.
public static class Camelot
{
    static readonly int[] MinorNum = { 5, 12, 7, 2, 9, 4, 11, 6, 1, 8, 3, 10 };
    static readonly int[] MajorNum = { 8, 3, 10, 5, 12, 7, 2, 9, 4, 11, 6, 1 };
    static readonly Dictionary<char, int> Pc = new() { ['C'] = 0, ['D'] = 2, ['E'] = 4, ['F'] = 5, ['G'] = 7, ['A'] = 9, ['B'] = 11 };
    static readonly Regex Cam = new(@"^(\d{1,2})\s*([ABab])$", RegexOptions.Compiled);
    static readonly Regex Note = new(@"^([A-G])([#b]?)\s*(.*)$", RegexOptions.Compiled);

    public static (int num, char letter)? Parse(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var s = key.Trim().Replace("♯", "#").Replace("♭", "b");
        var c = Cam.Match(s);
        if (c.Success) { var n = int.Parse(c.Groups[1].Value); return n is >= 1 and <= 12 ? (n, char.ToUpperInvariant(c.Groups[2].Value[0])) : null; }
        s = char.ToUpperInvariant(s[0]) + s[1..];
        var m = Note.Match(s);
        if (!m.Success) return null;
        var pc = Pc[m.Groups[1].Value[0]] + (m.Groups[2].Value == "#" ? 1 : m.Groups[2].Value == "b" ? -1 : 0);
        pc = (pc + 12) % 12;
        var mode = m.Groups[3].Value.Trim().ToLowerInvariant();
        var minor = mode.StartsWith("min") || mode == "m" || mode.StartsWith("moll");
        return minor ? (MinorNum[pc], 'A') : (MajorNum[pc], 'B');
    }

    public static bool Compatible(string? a, string? b)
    {
        var x = Parse(a); var y = Parse(b);
        if (x == null || y == null) return false;
        var (n1, l1) = x.Value; var (n2, l2) = y.Value;
        if (n1 == n2) return true; // same key or relative major/minor
        if (l1 != l2) return false;
        var d = Math.Abs(n1 - n2);
        return d == 1 || d == 11;
    }
}
