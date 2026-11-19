using Echo.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Echo.UI;

// ---------------- toasts (bottom right, like the web) ----------------
public sealed class ToastHandle
{
    internal Border Root = null!;
    internal TextBlock Label = null!;
    internal ProgressBar? Bar;
    internal StackPanel Actions = null!;
    internal Action<ToastHandle>? Remove;
    DispatcherQueueTimer? timer;

    public void Set(double p) { if (Bar != null) Bar.Value = Math.Clamp(p, 0, 1) * 100; }
    public void Text(string t) => Label.Text = t;
    public void Action(string label, Action a)
    {
        var b = new Button { Content = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextDecorations = Windows.UI.Text.TextDecorations.Underline }, Padding = new Thickness(0, 4, 8, 0), BorderThickness = new Thickness(0) };
        var bg = new SolidColorBrush(Theme.BgC);
        U.Style(b, Theme.Transparent, bg, Theme.Transparent, Theme.Transparent, bg, Theme.Transparent);
        b.Click += (s, e) => { a(); Close(); };
        Actions.Children.Add(b);
        Actions.Visibility = Visibility.Visible;
    }
    public void Close(int after = 0)
    {
        if (after <= 0) { Remove?.Invoke(this); return; }
        var dq = DispatcherQueue.GetForCurrentThread();
        if (dq == null) { Remove?.Invoke(this); return; }
        timer?.Stop();
        timer = dq.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(after);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => { s.Stop(); Remove?.Invoke(this); };
        timer.Start();
    }
}

public static class Toasts
{
    public static StackPanel Host { get; } = new()
    {
        Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(16, 16, 20, 16), MaxWidth = 380,
    };

    public static ToastHandle Show(string text, bool progress = false, int ms = 3800)
    {
        var h = new ToastHandle();
        var fgOnToast = new SolidColorBrush(Theme.BgC);
        h.Label = new TextBlock { Text = text, FontSize = 14, Foreground = fgOnToast, TextWrapping = TextWrapping.WrapWholeWords, FontFamily = Theme.BodyFont };
        var sp = new StackPanel { Spacing = 8 };
        sp.Children.Add(h.Label);
        if (progress)
        {
            h.Bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Foreground = fgOnToast, Background = Theme.Alpha(Theme.BgC, 0.25), MinHeight = 3 };
            sp.Children.Add(h.Bar);
        }
        h.Actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Visibility = Visibility.Collapsed };
        sp.Children.Add(h.Actions);
        h.Root = new Border
        {
            Background = Theme.Fg, CornerRadius = new CornerRadius(Math.Min(Theme.T.R, 12)), Padding = new Thickness(16, 12, 16, 12), Child = sp,
            Shadow = new ThemeShadow(), Translation = new System.Numerics.Vector3(0, 0, 24),
        };
        h.Remove = x => Host.Children.Remove(x.Root);
        Host.Children.Add(h.Root);
        while (Host.Children.Count > 5) Host.Children.RemoveAt(0);
        if (!progress) h.Close(ms);
        return h;
    }
}

// ---------------- download menu ----------------
public static class DownloadUi
{
    public static void Open(Track? t, FrameworkElement anchor)
    {
        if (t == null) return;
        var opts = Downloads.Options(t);
        if (opts.Count == 0) { Toasts.Show("Автор этого трека не разрешил скачивание"); return; }
        var menu = new MenuFlyout();
        foreach (var o in opts)
        {
            var it = new MenuFlyoutItem { Text = $"{o.Label}  ·  {o.Note}", Icon = new FontIcon { Glyph = Glyph.Download, FontFamily = Theme.Icons } };
            it.Click += (s, e) => _ = Start(t, o);
            menu.Items.Add(it);
        }
        if (Downloads.LocalPathFor(t) is string path)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var f = new MenuFlyoutItem { Text = "Открыть папку", Icon = new FontIcon { Glyph = Glyph.Folder, FontFamily = Theme.Icons } };
            f.Click += (s, e) => Downloads.OpenFolder(path);
            menu.Items.Add(f);
        }
        menu.ShowAt(anchor);
    }

    public static async Task Start(Track t, DlOption o)
    {
        var tt = Toasts.Show($"Готовлю {o.Label}: {t.Title}", progress: true);
        try
        {
            var path = await Downloads.Download(t, o, new Progress<double>(tt.Set));
            tt.Text($"Скачано: {o.Label} · Музыка\\Эхо");
            tt.Set(1);
            tt.Action("Открыть папку", () => Downloads.OpenFolder(path));
            tt.Close(7000);
        }
        catch (Exception e)
        {
            Logger.Write("download: " + e);
            tt.Text("Не получилось скачать. Попробуйте другой формат.");
            tt.Close(5000);
        }
    }
}

// ---------------- track list (`.tracks` / `.tr`) ----------------
public sealed class TrackList : Grid
{
    readonly List<Track> tracks;
    readonly List<(Grid row, Track t, TextBlock n, FontIcon eq, Button like)> rows = new();
    readonly Action<Track>? onDelete;
    readonly Action<IList<Track>, int>? onPlay;
    readonly int max;
    readonly Border box = new();

    public IReadOnlyList<Track> Tracks => tracks;

    public TrackList(IEnumerable<Track> list, string empty = "Пока пусто", Action<Track>? onDelete = null, Action<IList<Track>, int>? onPlay = null, int max = 0)
    {
        tracks = list.ToList();
        this.onDelete = onDelete; this.onPlay = onPlay; this.max = max;
        Children.Add(box);
        if (Theme.T.Glass)
        {
            box.BorderBrush = Theme.Line; box.BorderThickness = new Thickness(1); box.CornerRadius = new CornerRadius(20); box.Background = Theme.Card;
        }
        else { box.BorderBrush = Theme.Line; box.BorderThickness = new Thickness(0, Theme.T.Bw, 0, 0); }
        Build(empty);
        U.Bind(this, () => { Player.I.StateChanged += Sync; Sync(); }, () => Player.I.StateChanged -= Sync);
    }

    void Build(string empty)
    {
        var sp = new StackPanel();
        if (tracks.Count == 0) { sp.Children.Add(U.Status(empty)); box.Child = sp; return; }
        var shown = max > 0 ? tracks.Take(max) : tracks;
        var n = 0;
        foreach (var t in shown) sp.Children.Add(Row(t, n++));
        box.Child = sp;
    }

    Grid Row(Track t, int i)
    {
        var g = new Grid { Padding = new Thickness(8, 8, 8, 8), ColumnSpacing = 14, Background = Theme.Transparent, BorderBrush = Theme.Line };
        g.BorderThickness = new Thickness(0, 0, 0, Theme.T.Id == 3 ? 2 : 1);
        foreach (var w in new[] { new GridLength(32), new GridLength(44), new GridLength(1.6, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(52), new GridLength(36), new GridLength(36) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });

        var num = U.Muted((i + 1).ToString(), 13); num.HorizontalAlignment = HorizontalAlignment.Right; num.VerticalAlignment = VerticalAlignment.Center;
        var eq = U.Icon(Glyph.Wave, 14); eq.HorizontalAlignment = HorizontalAlignment.Right; eq.Visibility = Visibility.Collapsed;
        var nCell = new Grid(); nCell.Children.Add(num); nCell.Children.Add(eq);
        g.Children.Add(nCell);

        var cv = U.Cover(t.CoverSm ?? t.Cover, 44, i, Theme.T.RoundCovers ? new CornerRadius(22) : new CornerRadius(Math.Min(Theme.T.CoverR, 6)));
        cv.BorderThickness = new Thickness(1);
        Grid.SetColumn(cv, 1); g.Children.Add(cv);

        var tt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tt.Children.Add(U.Bold(t.Title));
        tt.Children.Add(U.Muted(t.Artist, 13));
        Grid.SetColumn(tt, 2); g.Children.Add(tt);

        var al = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!string.IsNullOrEmpty(t.Genre)) al.Children.Add(U.Muted(t.Genre, 14));
        if (!string.IsNullOrEmpty(t.Quality))
        {
            var q = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 2, 5, 2), VerticalAlignment = VerticalAlignment.Center };
            q.Child = new TextBlock { Text = t.Quality.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, CharacterSpacing = 80 };
            al.Children.Add(q);
        }
        if (Downloads.IsDownloaded(t)) al.Children.Add(U.Icon(Glyph.Check, 12, Theme.Muted));
        Grid.SetColumn(al, 3); g.Children.Add(al);

        var d = U.Muted(U.Fmt(t.Duration), 13); d.HorizontalAlignment = HorizontalAlignment.Right; d.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(d, 4); g.Children.Add(d);

        var like = U.IconBtn(Recs.IsLiked(t) ? Glyph.HeartFill : Glyph.Heart, "Нравится", () => { Shell.DoLike(t); }, 36, 15);
        Grid.SetColumn(like, 5); g.Children.Add(like);

        Button act;
        if (onDelete != null)
        {
            act = U.IconBtn(Glyph.Delete, "Удалить трек", () => onDelete(t), 36, 15);
        }
        else
        {
            act = U.IconBtn(Glyph.Download, t.Downloadable ? "Скачать" : "Автор не разрешил скачивание", () => { }, 36, 15);
            act.IsEnabled = t.Downloadable;
            act.Click += (s, e) => DownloadUi.Open(t, act);
        }
        Grid.SetColumn(act, 6); g.Children.Add(act);

        g.PointerEntered += (s, e) => { if (Player.I.Current?.Id != t.Id) g.Background = Theme.Card; };
        g.PointerExited += (s, e) => g.Background = Player.I.Current?.Id == t.Id ? Theme.Card2 : Theme.Transparent;
        g.Tapped += (s, e) =>
        {
            if (U.InsideButton(e.OriginalSource, g)) return;
            e.Handled = true;
            if (Player.I.Current?.Id == t.Id) { Player.I.Toggle(); return; }
            var idx = tracks.IndexOf(t);
            if (onPlay != null) onPlay(tracks, idx);
            else Player.I.PlayList(tracks, idx, PlayMode.List);
        };
        rows.Add((g, t, num, eq, like));
        return g;
    }

    void Sync()
    {
        var cur = Player.I.Current;
        var playing = Player.I.IsPlaying;
        foreach (var (row, t, n, eq, like) in rows)
        {
            var now = cur != null && cur.Id == t.Id;
            row.Background = now ? Theme.Card2 : Theme.Transparent;
            n.Visibility = now && playing ? Visibility.Collapsed : Visibility.Visible;
            n.Foreground = now ? Theme.Fg : Theme.Muted;
            eq.Visibility = now && playing ? Visibility.Visible : Visibility.Collapsed;
            var liked = Recs.IsLiked(t);
            U.SetGlyph(like, liked ? Glyph.HeartFill : Glyph.Heart);
            like.Foreground = liked ? Theme.Fg : Theme.Muted;
        }
    }
}
