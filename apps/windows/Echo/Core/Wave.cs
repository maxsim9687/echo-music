using Microsoft.UI.Dispatching;

namespace Echo.Core;

// Small bridge from core services to the UI (toasts, the UI thread).
public static class Bus
{
    public static DispatcherQueue? Dq { get; set; }
    public static event Action<string>? ToastRequested;
    public static void Toast(string text) => ToastRequested?.Invoke(text);
    public static void Ui(Action a)
    {
        if (Dq == null || Dq.HasThreadAccess) a();
        else Dq.TryEnqueue(() => a());
    }
}

// «Моя волна»: progressive candidate pool (starts playing as soon as 6 rankable tracks exist), refills in batches of 8.
public static class Wave
{
    sealed class PoolRun
    {
        public string Key = "";
        public readonly TaskCompletionSource<List<Track>> First = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<List<Track>> All = Task.FromResult(new List<Track>());
    }

    static List<Track> pool = new();
    static string poolKey = "";
    static DateTime poolAt;
    static PoolRun? run;
    static Task? refilling;
    static int gen;
    static readonly Random Rnd = new();
    static DispatcherQueueTimer? retuneTimer;

    public static string? Mood { get; private set; }
    public static string? Genre { get; private set; }
    public static string? Status { get; private set; }
    public static string? StatusSub { get; private set; }
    public static bool Busy { get; private set; }
    public static event Action? Changed;
    public static WaveSettings W => Settings.Current.Wave;

    public static ScoreCtx Ctx(double? explore = null, Track? current = null, IReadOnlyList<Track>? recent = null) =>
        new() { Mood = Mood, Genre = Genre, Cfg = W, Explore = explore, Current = current, Recent = recent };

    static List<Task<List<Track>>> Jobs()
    {
        var pre = Recs.Preset(Mood);
        var jobs = new List<Task<List<Track>>>();
        if (!string.IsNullOrEmpty(Genre))
        {
            jobs.Add(Catalog.AuTrending(Genre, "week", 50));
            jobs.Add(Catalog.AuTrending(Genre, "month", 40));
            jobs.Add(Catalog.AuSearch(Genre.ToLowerInvariant(), 40));
        }
        else if (pre?.Tags != null)
        {
            // genre waves (Phonk…) live in search results more than in the genre charts
            foreach (var q in pre.Queries) jobs.Add(Catalog.AuSearch(q, 40));
            foreach (var g in pre.Genres.Take(2)) jobs.Add(Catalog.AuTrending(g, "week", 30));
        }
        else
        {
            // taste: trending in top-3 genres, tracks of top-3 artists, search by top-2 tags
            foreach (var g in Recs.TopGenres(3)) jobs.Add(Catalog.AuTrending(g, "week", 30));
            foreach (var a in Recs.TopArtists(3)) if (a.StartsWith("au:")) jobs.Add(Catalog.AuArtist(a));
            foreach (var tag in Recs.TopTags(2)) jobs.Add(Catalog.AuSearch(tag, 20));
            // similar to liked: 3 random liked tracks, by their first two tags and by their genre
            foreach (var l in Recs.P.Likes.Values.OrderBy(_ => Rnd.Next()).Take(3))
            {
                if (l.TagList.Count > 0) jobs.Add(Catalog.AuSearch(string.Join(" ", l.TagList.Take(2)), 20));
                if (!string.IsNullOrEmpty(l.Genre)) jobs.Add(Catalog.AuTrending(l.Genre, "week", 25));
            }
            // discovery: underground, trending week, one random genre never played (exploration)
            jobs.Add(Catalog.AuTrending(null, "week", 60));
            jobs.Add(Catalog.AuTrending(null, "month", 40));
            jobs.Add(Catalog.AuUnderground(30));
            var never = Recs.GenreList.Where(g => !Recs.P.G.ContainsKey(g)).ToList();
            if (never.Count > 0) jobs.Add(Catalog.AuTrending(never[Rnd.Next(never.Count)], "week", 25));
            // preset queries for the selected mood
            if (pre != null)
            {
                foreach (var g in pre.Genres.Take(3)) jobs.Add(Catalog.AuTrending(g, "week", 30));
                foreach (var q in pre.Queries) jobs.Add(Catalog.AuSearch(q, 25));
            }
        }
        jobs.Add(Catalog.LocalTracks());
        if (W.Novelty < 60) jobs.Add(Task.FromResult(Catalog.RefreshAll(Recs.P.Likes.Values)));
        if (Catalog.Jamendo)
        {
            IEnumerable<string> src = !string.IsNullOrEmpty(Genre) ? new[] { Catalog.JTag(Genre) }
                : pre?.Tags ?? (pre != null ? pre.Genres : Recs.TopGenres(3).ToArray()).Select(Catalog.JTag);
            var tags = string.Join("+", src.Where(x => x.Length > 0).Take(3));
            jobs.Add(Catalog.JaPopular(tags.Length > 0 ? tags : null, 40, W.Vocals == "instrumental"));
        }
        return jobs;
    }

    // Resolves when there is enough to start playing (quick) or when every source has answered.
    public static Task<List<Track>> EnsurePool(bool quick)
    {
        var key = $"{Mood}|{Genre}|{W.Novelty}";
        if (poolKey == key && poolAt != default && DateTime.UtcNow - poolAt < TimeSpan.FromMinutes(10)) return Task.FromResult(pool);
        if (run == null || run.Key != key)
        {
            pool = new(); poolAt = default; poolKey = key;
            var r = new PoolRun { Key = key };
            run = r;
            var jobs = Jobs();
            r.All = RunAll(r, jobs);
        }
        return quick ? run.First.Task : run.All;
    }

    static async Task<List<Track>> RunAll(PoolRun r, List<Task<List<Track>>> jobs)
    {
        async Task Add(Task<List<Track>> j)
        {
            try
            {
                var l = await j;
                if (run != r) return;
                pool = Catalog.Dedupe(pool.Concat(l));
                if (!r.First.Task.IsCompleted && Recs.Pick(pool, 6, Ctx(0)).Count >= 6) r.First.TrySetResult(pool);
            }
            catch (Exception e) { Logger.Write("wave job: " + e.Message); }
        }
        await Task.WhenAll(jobs.Select(Add));
        if (run == r) poolAt = DateTime.UtcNow;
        r.First.TrySetResult(pool);
        return pool;
    }

    public static void ResetPool() { pool = new(); poolAt = default; run = null; poolKey = ""; refilling = null; gen++; }

    public static Task Refill()
    {
        if (refilling != null) return refilling;
        var g = gen;
        refilling = Do();
        return refilling;

        async Task Do()
        {
            await Task.Yield();
            try
            {
                await EnsurePool(true);
                if (g != gen) return;
                var pl = Player.I;
                var inQ = new HashSet<string>(pl.Queue.Select(t => t.Id));
                var add = Recs.Pick(pool.Where(t => !inQ.Contains(t.Id)), 8, Ctx(null, pl.Queue.LastOrDefault(), pl.Queue));
                pl.Append(add);
            }
            finally { if (g == gen) refilling = null; }
        }
    }

    public static async Task Start(bool fresh = false)
    {
        var pl = Player.I;
        if (!fresh && pl.Mode == PlayMode.Wave && pl.Current != null) { pl.Toggle(); return; }
        if (Busy) return;
        Busy = true; Status = "Подбираю треки…"; StatusSub = null; Changed?.Invoke();
        try
        {
            await EnsurePool(true);
            var q = Recs.Pick(pool, 8, Ctx(null, null, null));
            if (q.Count == 0) throw new Exception("empty");
            Status = null; StatusSub = null;
            pl.PlayList(q, 0, PlayMode.Wave);
        }
        catch
        {
            Status = W.OnlyDl ? "Не нашлось треков со скачиванием" : "Каталог не ответил";
            StatusSub = W.OnlyDl ? "Ослабьте настройки волны" : "Нажмите плей ещё раз через пару секунд";
            ResetPool();
        }
        finally { Busy = false; Changed?.Invoke(); }
    }

    public static async Task Retune()
    {
        ResetPool();
        var pl = Player.I;
        if (pl.Mode == PlayMode.Wave && pl.Current != null)
        {
            pl.TruncateAfterCurrent();
            try { await Refill(); } catch { Bus.Toast("Не удалось подобрать треки под эти настройки"); }
        }
    }

    public static async Task SetMood(string? m)
    {
        Mood = Mood == m ? null : m;
        Genre = null;
        Changed?.Invoke();
        var pl = Player.I;
        if (pl.Mode == PlayMode.Wave && pl.Current != null) await Retune();
        else { ResetPool(); await Start(true); }
    }

    // Tiles in «Жанры и настроения»: start a fresh wave right away.
    public static async Task StartWith(string? mood, string? genre)
    {
        Mood = mood; Genre = genre;
        ResetPool(); Changed?.Invoke();
        await Start(true);
    }

    public static void SetSettings(Action<WaveSettings> patch)
    {
        patch(W);
        Settings.Save();
        Changed?.Invoke();
        if (Bus.Dq == null) { _ = Retune(); return; }
        retuneTimer ??= Bus.Dq.CreateTimer();
        retuneTimer.Stop();
        retuneTimer.Interval = TimeSpan.FromMilliseconds(450);
        retuneTimer.IsRepeating = false;
        retuneTimer.Tick -= OnRetune;
        retuneTimer.Tick += OnRetune;
        retuneTimer.Start();
    }
    static void OnRetune(DispatcherQueueTimer t, object o) { t.Stop(); _ = Retune(); }

    public static void ResetSettings() => SetSettings(w => { var d = new WaveSettings(); w.Novelty = d.Novelty; w.Popularity = d.Popularity; w.Tempo = d.Tempo; w.Vocals = d.Vocals; w.OnlyDl = d.OnlyDl; });
}
