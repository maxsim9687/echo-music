using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Echo.Core;

public enum PlayMode { List, Wave }

// Native playback: Windows.Media.Playback.MediaPlayer (background audio, SystemMediaTransportControls, media keys),
// queue, prefetch of the next track, learning events, and a stop after 4 load errors in a row.
public sealed partial class Player : ObservableObject
{
    public static Player I { get; private set; } = null!;

    readonly MediaPlayer mp;
    readonly DispatcherQueue dq;
    readonly DispatcherQueueTimer tick;
    MediaPlaybackItem? currentItem;
    (string id, MediaPlaybackItem item)? prefetched;
    bool counted, waitingNext;
    DispatcherQueueTimer? pfTimer, errTimer;
    int errRun;

    public List<Track> Queue { get; private set; } = new();
    public int Index { get; private set; } = -1;
    public PlayMode Mode { get; private set; } = PlayMode.List;
    public Track? Current => Index >= 0 && Index < Queue.Count ? Queue[Index] : null;
    public Track? NextTrack => Index + 1 < Queue.Count ? Queue[Index + 1] : null;

    [ObservableProperty] private bool isPlaying;
    [ObservableProperty] private bool shuffle;
    public double Position { get; private set; }
    public double Duration { get; private set; }

    public event Action? StateChanged;     // track, play/pause, queue, likes
    public event Action? PositionChanged;  // ~4 times a second

    public static void Init(DispatcherQueue dq) { I = new Player(dq); }

    Player(DispatcherQueue dq)
    {
        this.dq = dq;
        mp = new MediaPlayer { AudioCategory = MediaPlayerAudioCategory.Media, AutoPlay = false, Volume = Settings.Current.Volume / 100.0 };
        mp.CommandManager.IsEnabled = true;
        mp.CommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        mp.CommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
        mp.CommandManager.NextReceived += (s, e) => { e.Handled = true; dq.TryEnqueue(() => Next()); };
        mp.CommandManager.PreviousReceived += (s, e) => { e.Handled = true; dq.TryEnqueue(Prev); };
        mp.MediaEnded += (s, e) => dq.TryEnqueue(OnEnded);
        mp.MediaFailed += (s, e) => { var src = s.Source; dq.TryEnqueue(() => OnFailed(src, e.ErrorMessage)); };
        mp.MediaOpened += (s, e) => dq.TryEnqueue(() => { Duration = mp.PlaybackSession.NaturalDuration.TotalSeconds; PositionChanged?.Invoke(); });
        mp.PlaybackSession.PlaybackStateChanged += (s, e) => dq.TryEnqueue(OnState);
        Shuffle = Settings.Current.Shuffle;

        tick = dq.CreateTimer();
        tick.Interval = TimeSpan.FromMilliseconds(250);
        tick.Tick += (s, e) => OnTick();
        tick.Start();
    }

    public double Volume
    {
        get => mp.Volume * 100;
        set { mp.Volume = Math.Clamp(value, 0, 100) / 100.0; Settings.Current.Volume = value; }
    }

    // ---------------- queue ----------------
    public void PlayList(IList<Track> list, int start = 0, PlayMode mode = PlayMode.List)
    {
        if (list.Count == 0) { Bus.Toast("Здесь пока нечего играть"); return; }
        Queue = list.Select(t => Catalog.Refresh(t.Clone())).ToList();
        Index = Math.Clamp(start, 0, Queue.Count - 1);
        Mode = mode;
        Load(true);
    }

    public void Append(IEnumerable<Track> add)
    {
        var known = new HashSet<string>(Queue.Select(t => t.Id));
        var n = 0;
        foreach (var t in add) if (known.Add(t.Id)) { Queue.Add(Catalog.Refresh(t.Clone())); n++; }
        if (n > 0) { Changed(); SchedulePrefetch(); }
    }

    public void TruncateAfterCurrent()
    {
        if (Index >= 0 && Index + 1 < Queue.Count) Queue.RemoveRange(Index + 1, Queue.Count - Index - 1);
        prefetched = null;
        Changed();
    }

    public void JumpTo(int i)
    {
        if (i < 0 || i >= Queue.Count) return;
        SkipLearn();
        Index = i; Load(true);
    }

    void Changed() => StateChanged?.Invoke();
    public void NotifyChanged() => Changed();

    MediaPlaybackItem MakeItem(Track t)
    {
        MediaSource src;
        var local = Downloads.LocalPathFor(t);
        if (local != null) src = MediaSource.CreateFromUri(new Uri(local));
        else if (t.Src == "file" && !string.IsNullOrEmpty(t.Stream)) src = MediaSource.CreateFromUri(new Uri(t.Stream));
        else src = MediaSource.CreateFromUri(Api.Abs(t.Stream ?? ""));
        src.CustomProperties["echoId"] = t.Id;
        var item = new MediaPlaybackItem(src);
        try
        {
            var props = item.GetDisplayProperties();
            props.Type = MediaPlaybackType.Music;
            props.MusicProperties.Title = t.Title;
            props.MusicProperties.Artist = t.Artist;
            props.MusicProperties.AlbumTitle = string.IsNullOrEmpty(t.Genre) ? "Эхо" : t.Genre;
            if (!string.IsNullOrEmpty(t.Cover)) props.Thumbnail = RandomAccessStreamReference.CreateFromUri(Api.Abs(t.Cover));
            item.ApplyDisplayProperties(props);
        }
        catch (Exception e) { Logger.Write("display props: " + e.Message); }
        return item;
    }

    void Load(bool autoplay)
    {
        var t = Current;
        if (t == null) return;
        counted = false;
        Position = 0; Duration = t.Duration;
        MediaPlaybackItem item;
        if (prefetched is { } pf && pf.id == t.Id) item = pf.item; else item = MakeItem(t);
        prefetched = null;
        currentItem = item;
        mp.Source = item;
        if (autoplay) mp.Play();
        Recs.MarkPlayed(t);
        Changed();
        PositionChanged?.Invoke();
        if (Mode == PlayMode.Wave && Queue.Count - Index < 4) _ = SafeRefill();
        SchedulePrefetch();
    }

    async Task SafeRefill()
    {
        try { await Wave.Refill(); } catch (Exception e) { Logger.Write("refill: " + e.Message); }
    }

    // warm the next track on the server and open its source so skipping starts instantly
    void SchedulePrefetch()
    {
        var nx = NextTrack;
        if (nx == null || (prefetched is { } p && p.id == nx.Id)) return;
        var id = nx.Id;
        pfTimer?.Stop();
        var timer = pfTimer = dq.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(1500);
        timer.IsRepeating = false;
        timer.Tick += async (s, e) =>
        {
            s.Stop();
            var cur = NextTrack;
            if (cur == null || cur.Id != id) return;
            try
            {
                if (Downloads.LocalPathFor(cur) == null && cur.Src != "file")
                {
                    if (cur.Src == "audius")
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, Api.Abs(cur.Stream ?? ""));
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        using var r = await Api.Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                    }
                }
                var item = MakeItem(cur);
                _ = item.Source.OpenAsync();
                if (NextTrack?.Id == id) prefetched = (id, item);
            }
            catch (Exception ex) { Logger.Write("prefetch: " + ex.Message); }
        };
        timer.Start();
    }

    // ---------------- transport ----------------
    void SkipLearn()
    {
        var t = Current;
        if (t != null && Position < 30 && currentItem != null) Recs.Skipped(t, Position);
    }

    public void Next(string reason = "skip")
    {
        if (reason == "skip") SkipLearn();
        if (Index < Queue.Count - 1) { Index++; Load(true); }
        else if (Mode == PlayMode.Wave)
        {
            if (waitingNext) return;
            waitingNext = true;
            _ = Do();
            async Task Do()
            {
                try { await Wave.Refill(); if (Index < Queue.Count - 1) { Index++; Load(true); } }
                catch { Bus.Toast("Каталог не ответил"); }
                finally { waitingNext = false; }
            }
        }
        else { mp.Pause(); Changed(); }
    }

    public void Prev()
    {
        if (Position > 4 || Index <= 0) Seek(0);
        else { Index--; Load(true); }
    }

    public void Toggle()
    {
        if (Current == null) { _ = Wave.Start(); return; }
        if (mp.PlaybackSession.PlaybackState == MediaPlaybackState.Playing) mp.Pause();
        else
        {
            if (mp.Source == null) Load(true); else mp.Play();
        }
    }

    public void Pause() => mp.Pause();

    public void Seek(double sec)
    {
        try { mp.PlaybackSession.Position = TimeSpan.FromSeconds(Math.Max(0, sec)); Position = sec; PositionChanged?.Invoke(); } catch { }
    }

    public void ToggleShuffle()
    {
        Shuffle = !Shuffle;
        Settings.Current.Shuffle = Shuffle; Settings.Save();
        if (Shuffle && Queue.Count > Index + 2)
        {
            var rnd = new Random();
            var rest = Queue.Skip(Index + 1).OrderBy(_ => rnd.Next()).ToList();
            Queue.RemoveRange(Index + 1, Queue.Count - Index - 1);
            Queue.AddRange(rest);
            prefetched = null;
            SchedulePrefetch();
        }
        Changed();
    }

    public void Stop()
    {
        mp.Pause();
        mp.Source = null; currentItem = null; prefetched = null;
        Queue = new(); Index = -1; Mode = PlayMode.List;
        Changed();
    }

    // ---------------- events ----------------
    void OnTick()
    {
        if (Current == null || currentItem == null) return;
        var s = mp.PlaybackSession;
        Position = s.Position.TotalSeconds;
        var nd = s.NaturalDuration.TotalSeconds;
        if (nd > 0) Duration = nd;
        if (!counted && Position > 30) { counted = true; Recs.Listened(Current); }
        PositionChanged?.Invoke();
    }

    void OnState()
    {
        var st = mp.PlaybackSession.PlaybackState;
        var playing = st == MediaPlaybackState.Playing;
        if (playing) errRun = 0;
        if (st is MediaPlaybackState.Playing or MediaPlaybackState.Paused or MediaPlaybackState.None)
        {
            if (IsPlaying != playing) { IsPlaying = playing; Changed(); }
        }
    }

    void OnEnded()
    {
        var t = Current;
        if (t != null) Recs.Finished(t);
        Next("end");
    }

    void OnFailed(IMediaPlaybackSource? src, string? msg)
    {
        if (Current == null) return;
        string? id = null;
        try { id = (src as MediaPlaybackItem)?.Source?.CustomProperties["echoId"] as string; } catch { }
        if (id != null && id != Current.Id) return;
        Logger.Write($"media failed {Current.Id}: {msg}");
        errRun++;
        if (errRun >= 4)
        {
            errRun = 0; mp.Pause(); IsPlaying = false; Changed();
            Bus.Toast("Несколько треков подряд не загрузились. Проверьте соединение и нажмите плей ещё раз.");
            return;
        }
        Bus.Toast("Трек не загрузился, включаю следующий");
        var timer = errTimer = dq.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(600);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => { s.Stop(); Next("error"); };
        timer.Start();
    }
}
