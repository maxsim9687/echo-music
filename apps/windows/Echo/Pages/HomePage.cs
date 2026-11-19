using Echo.Core;
using Echo.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Echo.Pages;

// Главная: «Моя волна» hero + Spotify-like dense sections.
public sealed class HomePage : PageBase
{
    static readonly ScoreCtx MixCtx = new() { Cfg = new WaveSettings(), Explore = 0.8 };

    // hero
    Grid hero = null!;
    TextBlock heroTitle = null!, eyebrowText = null!, vibeL = null!, vibeT = null!, vibeA = null!, nextT = null!, nextA = null!;
    Border eyebrowDot = null!, bigArt = null!, cfgPanel = null!;
    Button knob = null!, wLike = null!;
    StackPanel vibeActs = null!;
    readonly List<Button> moodChips = new();
    string? bigArtId;

    // wave settings panel
    Slider novelty = null!, pop = null!;
    TextBlock noveltyV = null!, popV = null!;
    readonly List<(Button b, string v)> tempoBtns = new(), vocalBtns = new();
    CheckBox onlyDl = null!;
    bool cfgUpdating;

    readonly StackPanel artistsHost = new(), chartsHost = new(), newHost = new(), uploadsHost = new(), mixesHost = new();

    public HomePage()
    {
        Body.Children.Add(BuildHero());
        if (!Recs.HasTaste()) Body.Children.Add(BuildOnboarding());
        Body.Children.Add(BuildQuick());
        var recent = BuildRecent();
        if (recent != null) Body.Children.Add(recent);
        Body.Children.Add(mixesHost);
        Body.Children.Add(artistsHost);
        Body.Children.Add(chartsHost);
        Body.Children.Add(newHost);
        Body.Children.Add(uploadsHost);
        Body.Children.Add(BuildGenres());

        RenderMixes();
        _ = LoadArtists();
        _ = LoadCharts();
        _ = LoadNew();
        _ = LoadUploads();

        U.Bind(this, () => { Player.I.StateChanged += SyncHero; Wave.Changed += OnWave; SyncHero(); RenderCfg(); },
                     () => { Player.I.StateChanged -= SyncHero; Wave.Changed -= OnWave; });
    }

    void OnWave() { SyncHero(); RenderCfg(); }

    // ---------------- hero ----------------
    FrameworkElement BuildHero()
    {
        hero = new Grid { RowSpacing = 22, Padding = new Thickness(32, 28, 32, 30), MinHeight = 470 };
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var card = new Border { CornerRadius = Theme.R2, BorderBrush = Theme.Line, BorderThickness = Theme.Bw, Background = Theme.HeroBg, Child = hero };
        card.Loaded += (s, e) => Shell.WaveAnchor = card;

        // top: eyebrow · moods · Настроить
        var top = new Grid { ColumnSpacing = 14 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        eyebrowDot = new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Theme.Fg, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Center };
        eyebrowText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, CharacterSpacing = 160, Foreground = Theme.Muted, VerticalAlignment = VerticalAlignment.Center, FontFamily = Theme.BodyFont };
        var eb = U.H(8, eyebrowDot, eyebrowText); eb.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(eb);
        var moods = U.Wrap(8);
        foreach (var (name, _) in Recs.Moods)
        {
            var b = U.Chip(name, Wave.Mood == name, async _ => await Wave.SetMood(name));
            moodChips.Add(b); moods.Children.Add(b);
        }
        moods.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(moods, 1); top.Children.Add(moods);
        var cfgBtn = U.ChipBtn("Настроить", Glyph.Settings, () =>
        {
            cfgPanel.Visibility = cfgPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            RenderCfg();
        });
        cfgBtn.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(cfgBtn, 2); top.Children.Add(cfgBtn);
        hero.Children.Add(top);

        cfgPanel = BuildCfg();
        Grid.SetRow(cfgPanel, 1); hero.Children.Add(cfgPanel);

        heroTitle = U.Display("Моя волна", 120);
        heroTitle.VerticalAlignment = VerticalAlignment.Bottom;
        heroTitle.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        Grid.SetRow(heroTitle, 2); hero.Children.Add(heroTitle);
        hero.SizeChanged += (s, e) =>
        {
            var size = Math.Clamp(e.NewSize.Width * 0.125, 56, 172);
            heroTitle.FontSize = size; heroTitle.LineHeight = size * 0.9;
            var art = e.NewSize.Width < 720 ? 110 : Math.Clamp(e.NewSize.Width * 0.17, 130, 210);
            bigArt.Width = bigArt.Height = art;
        };

        // bottom: play knob + now playing · next + big cover
        var bottom = new Grid { ColumnSpacing = 24 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        knob = new Button
        {
            Width = 88, Height = 88, CornerRadius = new CornerRadius(44), Padding = new Thickness(0), BorderThickness = new Thickness(Theme.T.Id == 3 ? 3 : 0),
            Content = new FontIcon { Glyph = Glyph.Play, FontFamily = Theme.Icons, FontSize = 30 },
        };
        U.Style(knob, Theme.Fg, new SolidColorBrush(Theme.BgC), Theme.Fg, Theme.Alpha(Theme.FgC, 0.86), new SolidColorBrush(Theme.BgC), Theme.Fg);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(knob, "Включить мою волну");
        knob.Click += (s, e) =>
        {
            var p = Player.I;
            if (p.Mode == PlayMode.Wave && p.Current != null) p.Toggle(); else _ = Wave.Start(true);
        };
        vibeL = new TextBlock { FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 140, Foreground = Theme.Muted, FontFamily = Theme.BodyFont };
        vibeT = U.Txt("Нажмите, чтобы начать", 26, FontWeights.SemiBold);
        vibeA = U.Muted("Подстроится под ваш вкус", 14);
        wLike = U.ChipBtn("Нравится", Glyph.Heart, () => { if (Player.I.Current is Track t) Shell.DoLike(t); });
        var wBan = U.ChipBtn("Не то", Glyph.Ban, Shell.NotThis);
        vibeActs = U.H(6, wLike, wBan); vibeActs.Margin = new Thickness(0, 6, 0, 0); vibeActs.Visibility = Visibility.Collapsed;
        var nowTxt = U.V(3, vibeL, vibeT, vibeA, vibeActs); nowTxt.VerticalAlignment = VerticalAlignment.Center;
        var left = new Grid { ColumnSpacing = 18 };
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        left.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(knob);
        Grid.SetColumn(nowTxt, 1); left.Children.Add(nowTxt);
        left.VerticalAlignment = VerticalAlignment.Bottom;
        bottom.Children.Add(left);

        var nextSmall = new TextBlock { Text = "ДАЛЬШЕ В ВОЛНЕ", FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 140, Foreground = Theme.Muted, HorizontalAlignment = HorizontalAlignment.Right };
        nextT = U.Txt("—", 16, FontWeights.SemiBold); nextT.HorizontalAlignment = HorizontalAlignment.Right; nextT.MaxWidth = 260;
        nextA = U.Muted("", 14); nextA.HorizontalAlignment = HorizontalAlignment.Right; nextA.MaxWidth = 260;
        var nextTxt = U.V(3, nextSmall, nextT, nextA); nextTxt.VerticalAlignment = VerticalAlignment.Bottom;
        bigArt = U.Cover(null, 200, 3, Theme.T.RoundCovers ? new CornerRadius(100) : new CornerRadius(Math.Min(Theme.T.CoverR, 22)));
        bigArt.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        bigArt.RenderTransform = new RotateTransform();
        var right = U.H(20, nextTxt, bigArt);
        Grid.SetColumn(right, 1); bottom.Children.Add(right);
        Grid.SetRow(bottom, 3); hero.Children.Add(bottom);
        return card;
    }

    Border BuildCfg()
    {
        var g = new Grid { ColumnSpacing = 18, RowSpacing = 14 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 420 });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 5; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Row(int r, string label, FrameworkElement ctl, FrameworkElement? val = null)
        {
            var l = U.Txt(label, 14); l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(l, r); g.Children.Add(l);
            ctl.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(ctl, r); Grid.SetColumn(ctl, 1); g.Children.Add(ctl);
            if (val != null) { val.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(val, r); Grid.SetColumn(val, 2); g.Children.Add(val); }
        }
        novelty = U.Slider(0, 100, 5, Wave.W.Novelty);
        noveltyV = U.Muted("", 12);
        novelty.ValueChanged += (s, e) => { if (!cfgUpdating) Wave.SetSettings(w => w.Novelty = e.NewValue); };
        Row(0, "Знакомое или новое", novelty, noveltyV);
        pop = U.Slider(0, 100, 5, Wave.W.Popularity);
        popV = U.Muted("", 12);
        pop.ValueChanged += (s, e) => { if (!cfgUpdating) Wave.SetSettings(w => w.Popularity = e.NewValue); };
        Row(1, "Андеграунд или хиты", pop, popV);

        var tempo = U.Wrap(6);
        foreach (var (v, l) in new[] { ("any", "Любой"), ("slow", "Спокойный"), ("mid", "Средний"), ("fast", "Быстрый") })
        {
            var b = U.Chip(l, Wave.W.Tempo == v, _ => Wave.SetSettings(w => w.Tempo = v));
            tempoBtns.Add((b, v)); tempo.Children.Add(b);
        }
        Row(2, "Темп", tempo);
        var vocals = U.Wrap(6);
        foreach (var (v, l) in new[] { ("any", "Любой"), ("instrumental", "Без слов"), ("vocal", "С вокалом") })
        {
            var b = U.Chip(l, Wave.W.Vocals == v, _ => Wave.SetSettings(w => w.Vocals = v));
            vocalBtns.Add((b, v)); vocals.Children.Add(b);
        }
        Row(3, "Вокал", vocals);
        onlyDl = new CheckBox { Content = new TextBlock { Text = "Только треки, которые можно скачать", FontFamily = Theme.BodyFont }, IsChecked = Wave.W.OnlyDl };
        onlyDl.Checked += (s, e) => { if (!cfgUpdating) Wave.SetSettings(w => w.OnlyDl = true); };
        onlyDl.Unchecked += (s, e) => { if (!cfgUpdating) Wave.SetSettings(w => w.OnlyDl = false); };
        Grid.SetRow(onlyDl, 4); Grid.SetColumnSpan(onlyDl, 2); g.Children.Add(onlyDl);
        var reset = U.Link("Сбросить", Wave.ResetSettings);
        reset.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(reset, 4); Grid.SetColumn(reset, 2); g.Children.Add(reset);
        return new Border { Child = g, Visibility = Visibility.Collapsed, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 1), Padding = new Thickness(0, 16, 0, 16) };
    }

    void RenderCfg()
    {
        if (novelty == null) return;
        cfgUpdating = true;
        var w = Wave.W;
        novelty.Value = w.Novelty; pop.Value = w.Popularity; onlyDl.IsChecked = w.OnlyDl;
        foreach (var (b, v) in tempoBtns) U.SetChip(b, w.Tempo == v);
        foreach (var (b, v) in vocalBtns) U.SetChip(b, w.Vocals == v);
        noveltyV.Text = w.Novelty < 35 ? "больше любимого" : w.Novelty > 65 ? "больше нового" : "поровну";
        popV.Text = w.Popularity < 35 ? "андеграунд" : w.Popularity > 65 ? "хиты" : "всё подряд";
        cfgUpdating = false;
    }

    void SyncHero()
    {
        var p = Player.I; var t = p.Current;
        var playing = p.IsPlaying;
        var waveOn = p.Mode == PlayMode.Wave && t != null;
        if (knob.Content is FontIcon fi)
        {
            var pause = playing && p.Mode == PlayMode.Wave;
            fi.Glyph = pause ? Glyph.Pause : Glyph.Play;
            fi.Margin = new Thickness(pause ? 0 : 5, 0, 0, 0);
        }
        eyebrowText.Text = (Wave.Mood != null ? $"Моя волна · {Wave.Mood}" : Wave.Genre != null ? $"Моя волна · {Wave.Genre}" : "Подобрано под вас").ToUpperInvariant();
        eyebrowDot.Opacity = waveOn && playing ? 1 : 0.4;
        foreach (var b in moodChips) U.SetChip(b, (b.Content as TextBlock)?.Text == Wave.Mood);
        vibeL.Text = (waveOn ? (playing ? "Сейчас играет" : "На паузе") : "Моя волна").ToUpperInvariant();
        vibeT.Text = waveOn ? t!.Title : Wave.Status ?? "Нажмите, чтобы начать";
        vibeA.Text = waveOn ? $"{t!.Artist}{(string.IsNullOrEmpty(t.Genre) ? "" : " · " + t.Genre)}"
            : Wave.StatusSub ?? (Wave.Mood != null ? $"Настроение: {Wave.Mood}" : Wave.Genre != null ? $"Жанр: {Wave.Genre}" : "Подстроится под ваш вкус");
        vibeActs.Visibility = waveOn ? Visibility.Visible : Visibility.Collapsed;
        if (waveOn)
        {
            var liked = Recs.IsLiked(t);
            U.SetChipBtnOn(wLike, liked);
            if (wLike.Content is StackPanel sp && sp.Children[0] is FontIcon hi) hi.Glyph = liked ? Glyph.HeartFill : Glyph.Heart;
        }
        var artId = waveOn ? t!.Id : null;
        if (artId != bigArtId) { bigArtId = artId; U.SetCover(bigArt, waveOn ? t!.Cover ?? t.CoverSm : null, bigArt.Width, 3); }
        if (bigArt.RenderTransform is RotateTransform rt) rt.Angle = waveOn && playing && !Theme.T.RoundCovers ? -2 : 0;
        var nx = p.Mode == PlayMode.Wave ? p.NextTrack : null;
        nextT.Text = nx?.Title ?? "—"; nextA.Text = nx?.Artist ?? "";
    }

    // ---------------- onboarding ----------------
    FrameworkElement BuildOnboarding()
    {
        var chips = U.Wrap(8);
        foreach (var g in Recs.StarterGenres)
        {
            chips.Children.Add(U.Chip(g, Recs.P.G.GetValueOrDefault(g) > 0, b =>
            {
                var on = !U.IsOn(b); U.SetChip(b, on); Recs.SeedGenre(g, on); Wave.ResetPool(); RenderMixes();
            }));
        }
        var s = U.Section("Что вам нравится?", (string?)null, null);
        s.Children.Add(U.Muted("Отметьте пару жанров, чтобы волна сразу попала в настроение. Дальше она учится на лайках и пропусках.", 15, true));
        s.Children.Add(chips);
        return s;
    }

    // ---------------- greeting + quick tiles ----------------
    static string Greeting()
    {
        var h = DateTime.Now.Hour;
        return h >= 5 && h < 12 ? "Доброе утро" : h >= 12 && h < 18 ? "Добрый день" : "Добрый вечер";
    }

    FrameworkElement BuildQuick()
    {
        var tiles = new List<Button>();
        var i = 0;
        var liked = Catalog.RefreshAll(Recs.P.Likes.Values.OrderByDescending(t => t.LikedAt ?? 0));
        if (liked.Count > 0) tiles.Add(Tile(liked.FirstOrDefault(t => t.Cover != null)?.Cover, "Любимое", $"{liked.Count} треков", () => Player.I.PlayList(liked), i++));
        foreach (var pl in Recs.P.Playlists.Take(3))
        {
            var p = pl;
            tiles.Add(Tile(p.Tracks.FirstOrDefault(t => t.Cover != null)?.Cover, p.Title, "плейлист", () => Player.I.PlayList(p.Tracks), i++));
        }
        var hist = Catalog.RefreshAll(Recs.P.Hist.Take(30));
        for (var h = 0; h < hist.Count && tiles.Count < 6; h++)
        {
            var idx = h;
            tiles.Add(Tile(hist[h].CoverSm ?? hist[h].Cover, hist[h].Title, hist[h].Artist, () => Player.I.PlayList(hist, idx), i++));
        }
        foreach (var (name, _) in Recs.Moods)
        {
            if (tiles.Count >= 6) break;
            var m = name;
            tiles.Add(Tile(null, $"Волна: {m}", "настроение", () => _ = Wave.StartWith(m, null), i++));
        }

        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        void LayoutTiles(double w)
        {
            var cols = w < 720 ? 2 : 3;
            grid.ColumnDefinitions.Clear(); grid.RowDefinitions.Clear();
            for (var c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var rows = (tiles.Count + cols - 1) / cols;
            for (var r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var k = 0; k < tiles.Count; k++) { Grid.SetColumn(tiles[k], k % cols); Grid.SetRow(tiles[k], k / cols); }
        }
        foreach (var t in tiles) grid.Children.Add(t);
        LayoutTiles(1000);
        var lastCols = 3;
        grid.SizeChanged += (s, e) => { var c = e.NewSize.Width < 720 ? 2 : 3; if (c != lastCols) { lastCols = c; LayoutTiles(e.NewSize.Width); } };

        var s = new StackPanel { Spacing = 16 };
        s.Children.Add(U.Display(Greeting(), 30));
        s.Children.Add(grid);
        return s;
    }

    static Button Tile(string? cover, string title, string sub, Action click, int i)
    {
        var g = new Grid { ColumnSpacing = 12, Height = 64 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cv = U.Cover(cover, 64, i, new CornerRadius(0), border: false);
        g.Children.Add(cv);
        var tt = U.V(0, U.Bold(title, 14), U.Muted(sub, 12)); tt.VerticalAlignment = VerticalAlignment.Center; tt.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(tt, 1); g.Children.Add(tt);
        var b = new Button
        {
            Content = g, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            CornerRadius = new CornerRadius(Math.Min(Theme.T.R, 8)), BorderThickness = Theme.Bw,
        };
        U.Style(b, Theme.T.Glass ? Theme.Card : Theme.Card2, Theme.Fg, Theme.T.Id == 3 ? Theme.Line : Theme.Transparent, Theme.Line, Theme.Fg, Theme.T.Id == 3 ? Theme.Line : Theme.Transparent);
        b.Click += (s, e) => click();
        return b;
    }

    // ---------------- recently played ----------------
    FrameworkElement? BuildRecent()
    {
        var hist = Catalog.RefreshAll(Recs.P.Hist.Take(20));
        if (hist.Count == 0) return null;
        var row = U.H(6);
        for (var i = 0; i < hist.Count; i++)
        {
            var idx = i;
            row.Children.Add(U.Card(hist[i].Cover ?? hist[i].CoverSm, hist[i].Title, hist[i].Artist, () => Player.I.PlayList(hist, idx), 150, i));
        }
        return U.Section("Недавно слушали", (string?)null, U.HScroll(row), U.Link("Вся история", () => Shell.Main.Go("library")));
    }

    // ---------------- mixes «Для вас» (web mixDefs) ----------------
    sealed record Mix(string T, string S, Func<Task<List<Track>>> Load);

    static List<Track> Ranked(IEnumerable<Track> l, int n = 30, ScoreCtx? ctx = null, int per = 3) => Recs.Pick(Catalog.Dedupe(l), n, ctx ?? MixCtx, per);

    static List<Mix> MixDefs()
    {
        var d = new List<Mix>();
        var tg = Recs.TopGenres(2);
        var g1 = tg.ElementAtOrDefault(0); var g2 = tg.ElementAtOrDefault(1);
        if (g1 != null) d.Add(new($"В духе {g1}", "популярное за месяц", async () => Ranked(await Catalog.AuTrending(g1, "month", 30))));
        var a = Recs.TopArtists(1).FirstOrDefault();
        if (a != null && a.StartsWith("au:"))
            d.Add(new($"Похоже на {Recs.P.An.GetValueOrDefault(a) ?? "любимого артиста"}", "артист и его жанр", async () =>
            {
                var own = await Catalog.AuArtist(a, 10);
                var g = own.FirstOrDefault()?.Genre;
                var more = g != null ? await Catalog.AuTrending(g, "week", 25) : new();
                return Ranked(own.Concat(more), 25, null, 4);
            }));
        if (g2 != null) d.Add(new($"Немного {g2}", "за неделю", async () => Ranked(await Catalog.AuTrending(g2, "week", 30))));
        d.Add(new("Андеграунд", "новые имена, которые набирают", async () => Ranked(await Catalog.AuUnderground(30))));
        d.Add(new("Под учёбу", "без слов, спокойно", async () =>
        {
            var lofi = await Catalog.AuTrending("Lo-Fi", "week", 30);
            var study = await Catalog.AuSearch("study", 20);
            return Ranked(lofi.Concat(study), 25, new ScoreCtx { Mood = "Фокус", Cfg = new WaveSettings(), Explore = 0.8 }, 2);
        }));
        d.Add(new("Хиты месяца", "что слушают все", async () => Ranked(await Catalog.AuTrending(null, "month", 30))));
        return d.Take(6).ToList();
    }

    void RenderMixes()
    {
        mixesHost.Children.Clear();
        var defs = MixDefs();
        var cache = new List<Track>?[defs.Count];
        var row = U.H(6);
        for (var i = 0; i < defs.Count; i++)
        {
            var idx = i; var m = defs[i];
            Button? card = null;
            card = U.Card(null, m.T, m.S, async () =>
            {
                var list = cache[idx];
                if (list == null) { try { list = await m.Load(); } catch { Toast("Подборка не загрузилась"); return; } }
                Player.I.PlayList(list);
            }, 172, i);
            row.Children.Add(card);
            var cv = (Border)card.Tag;
            _ = LoadMix();
            async Task LoadMix()
            {
                try { var list = await m.Load(); cache[idx] = list; U.SetCover(cv, list.FirstOrDefault(t => t.Cover != null)?.Cover, 172, idx); }
                catch { }
            }
        }
        mixesHost.Children.Add(U.Section("Для вас", "собирается из вашей истории", U.HScroll(row)));
    }

    // ---------------- favourite artists ----------------
    async Task LoadArtists()
    {
        var ids = Recs.P.Fav.Keys.Concat(Recs.TopArtists(12)).Distinct().Where(id => id.Length > 3).Take(12).ToList();
        if (ids.Count == 0) return;
        var known = Recs.P.Likes.Values.Concat(Recs.P.Hist).ToList();
        var row = U.H(10);
        var i = 0;
        foreach (var id in ids)
        {
            var name = Recs.P.An.GetValueOrDefault(id) ?? Recs.P.Fav.GetValueOrDefault(id);
            if (string.IsNullOrEmpty(name)) continue;
            var artistId = id;
            var cover = known.FirstOrDefault(t => t.ArtistId == id && t.Cover != null)?.Cover;
            var card = U.Card(cover, name, "артист", () => _ = PlayArtist(artistId), 132, i++, round: true);
            row.Children.Add(card);
            if (cover == null && id.StartsWith("au:"))
            {
                var cv = (Border)card.Tag;
                _ = Fill();
                async Task Fill()
                {
                    try { var l = await Catalog.AuArtist(artistId, 5); var c = l.FirstOrDefault(t => t.Cover != null)?.Cover; if (c != null) U.SetCover(cv, c, 132); }
                    catch { }
                }
            }
        }
        if (row.Children.Count == 0) return;
        artistsHost.Children.Add(U.Section("Любимые артисты", (string?)null, U.HScroll(row)));
        await Task.CompletedTask;
    }

    static async Task PlayArtist(string id)
    {
        var list = new List<Track>();
        if (id.StartsWith("au:"))
        {
            try { list = await Catalog.AuArtist(id, 20); } catch { Toasts.Show("Артист не загрузился"); return; }
        }
        list.AddRange(Catalog.RefreshAll(Recs.P.Likes.Values.Concat(Recs.P.Hist).Where(t => t.ArtistId == id)));
        list = Catalog.Dedupe(list).Select(t => (t, s: Recs.Score(t, MixCtx))).Where(x => x.s > -1e8).OrderByDescending(x => x.s).Select(x => x.t).ToList();
        var g = list.FirstOrDefault()?.Genre;
        if (g != null)
        {
            try { list.AddRange(Ranked((await Catalog.AuTrending(g, "week", 25)).Where(t => t.ArtistId != id), 10)); } catch { }
        }
        if (list.Count == 0) { Toasts.Show("У этого артиста пока нечего послушать"); return; }
        Player.I.PlayList(Catalog.Dedupe(list));
    }

    // ---------------- charts teaser ----------------
    async Task LoadCharts()
    {
        Chart chart;
        var cc = Settings.Current.ChartCountry;
        try { chart = await Api.Get<Chart>($"/api/charts/{Uri.EscapeDataString(cc)}", TimeSpan.FromMinutes(30)); }
        catch { return; }
        if (!Alive || chart.Items.Count == 0) return;
        var list = new StackPanel();
        foreach (var it in chart.Items.Take(5))
        {
            var item = it;
            var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(8, 10, 8, 10), Background = Theme.Transparent, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pos = U.Display(it.Pos.ToString(), 22); pos.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(pos);
            var tt = U.V(0, U.Bold(it.Title), U.Muted(string.Join(", ", new[] { it.Artist }.Concat(it.With ?? new())), 13));
            Grid.SetColumn(tt, 1); g.Children.Add(tt);
            var st = U.Muted(it.Streams > 0 ? it.Streams.ToString("N0", new System.Globalization.CultureInfo("ru-RU")) : "", 13); st.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(st, 2); g.Children.Add(st);
            g.PointerEntered += (s, e) => g.Background = Theme.Card;
            g.PointerExited += (s, e) => g.Background = Theme.Transparent;
            g.Tapped += async (s, e) =>
            {
                var r = await ChartMatch.Match(cc, item);
                if (r.List.Count == 0) { Toast("На Audius этого трека нет. Оригинал есть только в Spotify."); return; }
                Player.I.PlayList(r.List.Take(1).ToList());
                foreach (var other in chart.Items.Take(10).Where(x => x.Pos > item.Pos))
                {
                    var m = await ChartMatch.Match(cc, other);
                    if (m.List.Count > 0 && Recs.BaseScore(m.List[0], new ScoreCtx { Cfg = new WaveSettings(), Explore = 0.3 }) > -1e8) Player.I.Append(m.List.Take(1));
                }
            };
            list.Children.Add(g);
        }
        var box = new Border { Child = list, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, Theme.T.Bw, 0, 0) };
        if (Theme.T.Glass) { box.BorderThickness = new Thickness(1); box.CornerRadius = new CornerRadius(20); box.Background = Theme.Card; }
        chartsHost.Children.Add(U.Section("Чарты", $"{chart.Name} · {chart.Date}", box, U.Pill("Все чарты", () => Shell.Main.Go("charts"))));
    }

    // ---------------- new releases ----------------
    async Task LoadNew()
    {
        newHost.Children.Add(U.Section("Новое", "Audius, свежие релизы недели", U.Loading(4)));
        List<Track> tr;
        try { tr = await Catalog.AuTrending(null, "week", 40); }
        catch
        {
            newHost.Children.Clear();
            var err = U.Status("Каталог не ответил.", true);
            newHost.Children.Add(U.Section("Новое", (string?)null, U.V(8, err, U.Pill("Повторить", () => { newHost.Children.Clear(); _ = LoadNew(); }))));
            return;
        }
        var sorted = tr.OrderByDescending(t => Recs.ParseDate(t.ReleaseDate) ?? DateTime.MinValue).ToList();
        newHost.Children.Clear();
        newHost.Children.Add(U.Section("Новое", "Audius, свежие релизы недели", new TrackList(sorted.Take(10), "Пока пусто")));
    }

    // ---------------- admin uploads ----------------
    async Task LoadUploads()
    {
        var l = await Catalog.LocalTracks();
        if (!Alive || l.Count == 0) return;
        uploadsHost.Children.Add(U.Section("Ваша музыка", $"{l.Count} треков без перекодирования", new TrackList(l, max: 10)));
    }

    // ---------------- genres and moods ----------------
    FrameworkElement BuildGenres()
    {
        var wrap = U.Wrap(12);
        var i = 0;
        foreach (var (name, _) in Recs.Moods)
        {
            var m = name;
            wrap.Children.Add(GenreTile(m, "настроение", () => _ = Wave.StartWith(m, null), i++));
        }
        foreach (var g in Recs.GenreList)
        {
            var gg = g;
            wrap.Children.Add(GenreTile(gg, "жанр", () => _ = Wave.StartWith(null, gg), i++));
        }
        return U.Section("Жанры и настроения", "включат волну с этим настроем", wrap);
    }

    Button GenreTile(string title, string kind, Action click, int i)
    {
        var g = new Grid { Width = 196, Height = 104 };
        g.Children.Add(new Border { Background = U.Pattern(i), Opacity = 0.35 });
        var label = U.Display(title, 20); label.VerticalAlignment = VerticalAlignment.Bottom; label.Margin = new Thickness(14, 0, 14, 12); label.TextWrapping = TextWrapping.WrapWholeWords;
        g.Children.Add(label);
        var k = new TextBlock { Text = kind.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.SemiBold, CharacterSpacing = 140, Foreground = Theme.Muted, Margin = new Thickness(14, 12, 14, 0) };
        g.Children.Add(k);
        var b = new Button { Content = g, Padding = new Thickness(0), CornerRadius = new CornerRadius(Math.Min(Theme.T.R, 14)), BorderThickness = Theme.Bw };
        U.Style(b, Theme.T.Glass ? Theme.Card : Theme.Card2, Theme.Fg, Theme.Line, Theme.Card2, Theme.Fg, Theme.Fg);
        b.Click += (s, e) => { click(); Scroll.ChangeView(null, 0, null); };
        return b;
    }
}
