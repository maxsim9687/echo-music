using Echo.Core;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Echo.UI;

// Position slider + times; seeks on release so playback updates don't fight the drag.
public sealed class SeekBar : Grid
{
    readonly Slider slider;
    readonly TextBlock cur, dur;
    bool updating, dragging;

    public SeekBar()
    {
        ColumnSpacing = 10;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        cur = U.Muted("0:00", 12); cur.HorizontalAlignment = HorizontalAlignment.Right; cur.VerticalAlignment = VerticalAlignment.Center;
        dur = U.Muted("0:00", 12); dur.VerticalAlignment = VerticalAlignment.Center;
        slider = U.Slider(0, 1, 0.5, 0);
        slider.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, "Позиция в треке");
        Children.Add(cur);
        Grid.SetColumn(slider, 1); Children.Add(slider);
        Grid.SetColumn(dur, 2); Children.Add(dur);
        slider.AddHandler(PointerPressedEvent, new PointerEventHandler((s, e) => dragging = true), true);
        slider.AddHandler(PointerReleasedEvent, new PointerEventHandler((s, e) => { if (dragging) { dragging = false; Player.I.Seek(slider.Value); } }), true);
        slider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((s, e) => { if (dragging) { dragging = false; Player.I.Seek(slider.Value); } }), true);
        slider.ValueChanged += (s, e) =>
        {
            if (updating) return;
            if (dragging) { cur.Text = U.Fmt(e.NewValue); return; }
            Player.I.Seek(e.NewValue);
        };
        U.Bind(this, () => { Player.I.PositionChanged += Sync; Sync(); }, () => Player.I.PositionChanged -= Sync);
    }

    void Sync()
    {
        if (dragging) return;
        var p = Player.I;
        var d = p.Duration > 0 ? p.Duration : p.Current?.Duration ?? 0;
        updating = true;
        slider.Maximum = Math.Max(1, d);
        slider.Value = Math.Min(slider.Maximum, Math.Max(0, p.Position));
        updating = false;
        cur.Text = p.Current == null ? "0:00" : U.Fmt(p.Position) == "—" ? "0:00" : U.Fmt(p.Position);
        dur.Text = p.Current == null ? "0:00" : U.Fmt(d);
    }
}

// Mini player bar, always visible at the bottom.
public sealed class PlayerBar : Grid
{
    readonly Border cover;
    readonly TextBlock title, artist, quality;
    readonly Border qBox;
    readonly Button play, like, shuf, dl;

    public PlayerBar()
    {
        Background = Theme.BarBg;
        BorderBrush = Theme.Line;
        BorderThickness = new Thickness(0, Theme.T.Bw, 0, 0);
        Padding = new Thickness(16, 10, 16, 10);
        ColumnSpacing = 16;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MaxWidth = 640 });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });

        // now playing
        var np = new Grid { ColumnSpacing = 12, Background = Theme.Transparent };
        np.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        np.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cover = U.Cover(null, 48, 1, Theme.T.RoundCovers ? new CornerRadius(24) : new CornerRadius(Math.Min(Theme.T.CoverR, 8)));
        cover.BorderThickness = new Thickness(1);
        np.Children.Add(cover);
        title = U.Bold("Ничего не играет");
        quality = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, CharacterSpacing = 80 };
        qBox = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Child = quality, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var tRow = new Grid { ColumnSpacing = 8 };
        tRow.Children.Add(title);
        artist = U.Muted("Включите волну или найдите трек", 13);
        var tt = U.V(0, tRow, artist); tt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tt, 1); np.Children.Add(tt);
        np.Tapped += (s, e) => { if (Player.I.Current != null) Shell.Main.OpenNowPlaying(); };
        ToolTipService.SetToolTip(np, "Открыть плеер");
        Children.Add(np);

        // controls
        shuf = U.IconBtn(Glyph.Shuffle, "Перемешать", () => Player.I.ToggleShuffle(), 40, 15);
        var prev = U.IconBtn(Glyph.Prev, "Предыдущий трек", () => Player.I.Prev(), 40, 16);
        play = U.IconBtn(Glyph.Play, "Воспроизвести", () => Player.I.Toggle(), 46, 18, main: true);
        var next = U.IconBtn(Glyph.Next, "Следующий трек", () => Player.I.Next(), 40, 16);
        like = U.IconBtn(Glyph.Heart, "Нравится", () => { if (Player.I.Current is Track t) Shell.DoLike(t); }, 40, 15);
        var btns = U.H(6, shuf, prev, play, next, like);
        btns.HorizontalAlignment = HorizontalAlignment.Center;
        var ctl = U.V(2, btns, new SeekBar());
        Grid.SetColumn(ctl, 1); Children.Add(ctl);

        // right side
        dl = U.IconBtn(Glyph.Download, "Скачать трек", () => { }, 40, 16);
        dl.Click += (s, e) => DownloadUi.Open(Player.I.Current, dl);
        var vol = U.Slider(0, 100, 1, Player.I.Volume);
        vol.Width = 110; vol.VerticalAlignment = VerticalAlignment.Center;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(vol, "Громкость");
        vol.ValueChanged += (s, e) => { Player.I.Volume = e.NewValue; };
        vol.AddHandler(PointerReleasedEvent, new PointerEventHandler((s, e) => Settings.Save()), true);
        var full = U.IconBtn(Glyph.FullScreen, "Полноэкранный плеер", () => Shell.Main.OpenNowPlaying(), 40, 15);
        var right = U.H(8, dl, U.Icon(Glyph.Volume, 15, Theme.Muted), vol, full);
        right.HorizontalAlignment = HorizontalAlignment.Right; right.VerticalAlignment = VerticalAlignment.Center;
        ((FontIcon)right.Children[1]).VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(right, 2); Children.Add(right);

        U.Bind(this, () => { Player.I.StateChanged += Sync; Sync(); }, () => Player.I.StateChanged -= Sync);
    }

    void Sync()
    {
        var p = Player.I; var t = p.Current;
        U.SetGlyph(play, p.IsPlaying ? Glyph.Pause : Glyph.Play);
        ToolTipService.SetToolTip(play, p.IsPlaying ? "Пауза" : "Воспроизвести");
        Btns.SetOn(shuf, p.Shuffle);
        if (t == null)
        {
            title.Text = "Ничего не играет"; artist.Text = "Включите волну или найдите трек"; qBox.Visibility = Visibility.Collapsed;
            U.SetCover(cover, null, 48, 1); dl.IsEnabled = false; return;
        }
        title.Text = t.Title; artist.Text = string.IsNullOrEmpty(t.Quality) ? t.Artist : $"{t.Artist} · {t.Quality}";
        if ((cover.Tag as string) != t.Id) { cover.Tag = t.Id; U.SetCover(cover, t.CoverSm ?? t.Cover, 48, 1); }
        var liked = Recs.IsLiked(t);
        U.SetGlyph(like, liked ? Glyph.HeartFill : Glyph.Heart);
        Btns.SetOn(like, liked);
        dl.IsEnabled = t.Downloadable;
        ToolTipService.SetToolTip(dl, t.Downloadable ? "Скачать" : "Автор не разрешил скачивание");
    }
}

// Full-screen player: big cover, seek, controls, like, download, shuffle, queue list.
public sealed class NowPlaying : Grid
{
    readonly Border cover;
    readonly TextBlock title, artist, meta, eyebrow;
    readonly Button play, like, shuf, dl, ban;
    readonly StackPanel queue;
    readonly Grid main;
    string? shownId;
    int queueStamp = -1;

    public NowPlaying()
    {
        Background = Theme.T.Glass ? Theme.Alpha(Theme.BgC, 0.72) : Theme.Alpha(Theme.BgC, 0.94);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
        var page = new StackPanel { Spacing = 28, Padding = new Thickness(36, 20, 36, 40), MaxWidth = 1200 };
        scroll.Content = page;
        Children.Add(scroll);

        var top = new Grid();
        var close = U.IconBtn(Glyph.ChevronDown, "Свернуть плеер", () => Shell.Main.CloseNowPlaying(), 44, 18);
        top.Children.Add(close);
        eyebrow = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, CharacterSpacing = 160, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        top.Children.Add(eyebrow);
        page.Children.Add(top);

        main = new Grid { ColumnSpacing = 48, RowSpacing = 28 };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cover = U.Cover(null, 420, 3, Theme.T.RoundCovers ? new CornerRadius(210) : new CornerRadius(Math.Min(Theme.T.CoverR, 22)));
        main.Children.Add(cover);

        var info = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
        title = U.Display("", 44); title.MaxLines = 3;
        artist = U.Txt("", 20, null, Theme.Muted, true);
        meta = U.Muted("", 13);
        info.Children.Add(title); info.Children.Add(artist); info.Children.Add(meta);

        like = U.ChipBtn("Нравится", Glyph.Heart, () => { if (Player.I.Current is Track t) Shell.DoLike(t); });
        dl = U.ChipBtn("Скачать", Glyph.Download, () => { });
        dl.Click += (s, e) => DownloadUi.Open(Player.I.Current, dl);
        ban = U.ChipBtn("Не то", Glyph.Ban, () => Shell.NotThis());
        shuf = U.ChipBtn("Перемешать", Glyph.Shuffle, () => Player.I.ToggleShuffle());
        var acts = U.Wrap(8, new UIElement[] { like, dl, ban, shuf });
        acts.Margin = new Thickness(0, 6, 0, 0);
        info.Children.Add(acts);

        var seek = new SeekBar { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Stretch };
        info.Children.Add(seek);
        var prev = U.IconBtn(Glyph.Prev, "Предыдущий трек", () => Player.I.Prev(), 56, 22);
        play = U.IconBtn(Glyph.Play, "Воспроизвести", () => Player.I.Toggle(), 72, 26, main: true);
        var next = U.IconBtn(Glyph.Next, "Следующий трек", () => Player.I.Next(), 56, 22);
        var ctl = U.H(18, prev, play, next);
        ctl.Margin = new Thickness(46, 0, 0, 0);
        info.Children.Add(ctl);
        Grid.SetColumn(info, 1); main.Children.Add(info);
        page.Children.Add(main);

        queue = new StackPanel();
        page.Children.Add(U.Section("Дальше в очереди", (string?)null, queue));

        SizeChanged += (s, e) => Layout(e.NewSize.Width);
        U.Bind(this, () => { Player.I.StateChanged += Sync; Sync(); }, () => Player.I.StateChanged -= Sync);
    }

    void Layout(double w)
    {
        var narrow = w < 900;
        var size = Math.Clamp(narrow ? w - 72 : w * 0.36, 220, 460);
        cover.Width = cover.Height = size;
        if (!Theme.T.RoundCovers) cover.CornerRadius = new CornerRadius(Math.Min(Theme.T.CoverR, 22)); else cover.CornerRadius = new CornerRadius(size / 2);
        var info = (FrameworkElement)main.Children[1];
        if (narrow) { Grid.SetColumn(info, 0); Grid.SetRow(info, 1); if (main.RowDefinitions.Count < 2) { main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); } cover.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumnSpan(cover, 2); Grid.SetColumnSpan(info, 2); }
        else { Grid.SetColumn(info, 1); Grid.SetRow(info, 0); Grid.SetColumnSpan(cover, 1); Grid.SetColumnSpan(info, 1); cover.HorizontalAlignment = HorizontalAlignment.Left; }
    }

    void Sync()
    {
        var p = Player.I; var t = p.Current;
        U.SetGlyph(play, p.IsPlaying ? Glyph.Pause : Glyph.Play);
        eyebrow.Text = (t == null ? "НИЧЕГО НЕ ИГРАЕТ" : p.Mode == PlayMode.Wave ? (Wave.Mood != null ? $"МОЯ ВОЛНА · {Wave.Mood.ToUpperInvariant()}" : "МОЯ ВОЛНА") : "СЕЙЧАС ИГРАЕТ");
        U.SetChipBtnOn(shuf, p.Shuffle);
        ban.Visibility = p.Mode == PlayMode.Wave ? Visibility.Visible : Visibility.Collapsed;
        if (t == null) { title.Text = "Ничего не играет"; artist.Text = "Включите волну или найдите трек"; meta.Text = ""; return; }
        if (shownId != t.Id)
        {
            shownId = t.Id;
            title.Text = Theme.Head(t.Title); artist.Text = t.Artist;
            U.SetCover(cover, t.Cover ?? t.CoverSm, cover.Width, 3);
        }
        meta.Text = string.Join(" · ", new[] { t.Genre, t.Quality, t.Bpm is double b && b > 0 ? $"{Math.Round(b)} BPM" : null, t.Key }.Where(x => !string.IsNullOrEmpty(x)));
        var liked = Recs.IsLiked(t);
        U.SetChipBtnOn(like, liked);
        if (like.Content is StackPanel sp && sp.Children[0] is FontIcon fi) fi.Glyph = liked ? Glyph.HeartFill : Glyph.Heart;
        dl.IsEnabled = t.Downloadable;

        var stamp = HashCode.Combine(p.Index, p.Queue.Count, p.Queue.Count > p.Index + 1 ? p.Queue[p.Index + 1].Id : "");
        if (stamp == queueStamp) return;
        queueStamp = stamp;
        queue.Children.Clear();
        var upcoming = p.Queue.Skip(p.Index + 1).Take(40).ToList();
        if (upcoming.Count == 0) { queue.Children.Add(U.Status(p.Mode == PlayMode.Wave ? "Волна подберёт следующие треки сама" : "Очередь закончилась")); return; }
        var n = p.Index + 1;
        foreach (var q in upcoming)
        {
            var idx = n++;
            var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(8), Background = Theme.Transparent, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            var num = U.Muted((idx - p.Index).ToString(), 13); num.HorizontalAlignment = HorizontalAlignment.Right; num.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(num);
            var c = U.Cover(q.CoverSm ?? q.Cover, 40, idx); c.BorderThickness = new Thickness(1); Grid.SetColumn(c, 1); g.Children.Add(c);
            var tt = U.V(0, U.Bold(q.Title), U.Muted(q.Artist, 13)); tt.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(tt, 2); g.Children.Add(tt);
            var d = U.Muted(U.Fmt(q.Duration), 13); d.VerticalAlignment = VerticalAlignment.Center; d.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(d, 3); g.Children.Add(d);
            g.PointerEntered += (s, e) => g.Background = Theme.Card;
            g.PointerExited += (s, e) => g.Background = Theme.Transparent;
            g.Tapped += (s, e) => Player.I.JumpTo(idx);
            queue.Children.Add(g);
        }
    }
}
