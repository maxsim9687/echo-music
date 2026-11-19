using System.Globalization;
using Echo.Core;
using Echo.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Echo.Pages;

// Чарты Spotify, each row matched against Audius (Оригинал / Версия / Нет).
public sealed class ChartsPage : PageBase
{
    static readonly CultureInfo Ru = new("ru-RU");
    static readonly ScoreCtx ChartCtx = new() { Cfg = new WaveSettings(), Explore = 0.3 };
    readonly WrapPanel countries = U.Wrap(8);
    readonly StackPanel list = new();
    readonly TextBlock note = U.Note(), prog = U.Muted("", 12);
    readonly Button playBtn;
    readonly Dictionary<int, Button> findBtns = new();
    Chart? data;
    string cc = Settings.Current.ChartCountry;
    int gen;

    public ChartsPage()
    {
        var sec = U.SectionN("Чарты Spotify", note, null);
        sec.Children.Add(countries);
        sec.Children.Add(U.Muted("Spotify не отдаёт сами песни, поэтому рядом с каждым треком мы ищем его на Audius. «Оригинал» значит, что артист выложил трек сам. «Версия» означает ремикс, кавер или перезалив.", 13, true));
        playBtn = U.Pill("Слушать то, что нашлось", () => _ = PlayFound(), solid: true);
        var row = U.H(14, playBtn, prog); prog.VerticalAlignment = VerticalAlignment.Center;
        sec.Children.Add(row);
        Body.Children.Add(sec);
        var box = new Border { Child = list, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, Theme.T.Bw, 0, 0) };
        if (Theme.T.Glass) { box.BorderThickness = new Thickness(1); box.CornerRadius = new CornerRadius(20); box.Background = Theme.Card; }
        Body.Children.Add(box);
        _ = Render();
    }

    async Task Render()
    {
        var g = ++gen;
        ChartList cs;
        try { cs = await Api.Get<ChartList>("/api/charts", TimeSpan.FromMinutes(30)); } catch { cs = new ChartList(); }
        countries.Children.Clear();
        foreach (var c in cs.Charts)
        {
            var id = c.Id;
            countries.Children.Add(U.Chip(c.Name, id == cc, btn =>
            {
                cc = id; Settings.Current.ChartCountry = id; Settings.Save();
                foreach (var b in countries.Children.OfType<Button>()) U.SetChip(b, b == btn);
                _ = Render();
            }));
        }
        list.Children.Clear();
        list.Children.Add(U.Loading(8));
        note.Text = ""; prog.Text = ""; findBtns.Clear();
        try { data = await Api.Get<Chart>($"/api/charts/{Uri.EscapeDataString(cc)}", TimeSpan.FromMinutes(30)); }
        catch (Exception e) { list.Children.Clear(); list.Children.Add(U.Status(e.Message, true)); return; }
        if (g != gen) return;
        note.Text = $"{data.Name} · {data.Date} · топ {data.Items.Count}";
        list.Children.Clear();
        foreach (var it in data.Items) list.Children.Add(Row(it));

        // check the top of the chart in the background, a few at a time
        var top = data.Items.Take(20).ToList();
        var country = cc;
        for (var i = 0; i < top.Count; i += 4)
        {
            if (g != gen || !Alive) return;
            await Task.WhenAll(top.Skip(i).Take(4).Select(async it => Mark(it, await ChartMatch.Match(country, it))));
        }
    }

    FrameworkElement Row(ChartItem it)
    {
        var wrap = new StackPanel { BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, Theme.T.Id == 3 ? 2 : 1) };
        var g = new Grid { ColumnSpacing = 14, Padding = new Thickness(8, 10, 8, 10) };
        foreach (var w in new[] { new GridLength(40), new GridLength(44), new GridLength(1, GridUnitType.Star), new GridLength(110), GridLength.Auto, new GridLength(40) })
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
        var pos = U.Display(it.Pos.ToString(), 20); pos.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(pos);
        var chg = U.Muted(it.Change ?? "", 12); chg.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chg, 1); g.Children.Add(chg);
        var tt = U.V(0, U.Bold(it.Title), U.Muted(string.Join(", ", new[] { it.Artist }.Concat(it.With ?? new())), 13)); tt.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(tt, 2); g.Children.Add(tt);
        var st = U.Muted(it.Streams > 0 ? it.Streams.ToString("N0", Ru) : "", 13); st.VerticalAlignment = VerticalAlignment.Center; st.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(st, 3); g.Children.Add(st);
        var res = ChartMatch.Cached(cc, it);
        var find = U.ChipBtn(res != null ? ChartMatch.Label[res.Kind] : "Найти на Audius", null, () => { });
        find.VerticalAlignment = VerticalAlignment.Center;
        if (res != null) StyleFind(find, res.Kind);
        findBtns[it.Pos] = find;
        Grid.SetColumn(find, 4); g.Children.Add(find);
        var ext = U.IconBtn(Glyph.Open, "Открыть в Spotify", () => _ = Windows.System.Launcher.LaunchUriAsync(new Uri($"https://open.spotify.com/track/{Uri.EscapeDataString(it.Spotify ?? "")}")), 36, 14);
        ext.IsEnabled = !string.IsNullOrEmpty(it.Spotify);
        Grid.SetColumn(ext, 5); g.Children.Add(ext);
        wrap.Children.Add(g);
        var matches = new StackPanel { Visibility = Visibility.Collapsed, Padding = new Thickness(24, 0, 0, 10) };
        wrap.Children.Add(matches);
        var country = cc;
        find.Click += async (s, e) =>
        {
            if (matches.Visibility == Visibility.Visible) { matches.Visibility = Visibility.Collapsed; return; }
            matches.Visibility = Visibility.Visible;
            matches.Children.Clear(); matches.Children.Add(U.Loading(3));
            var r = await ChartMatch.Match(country, it);
            Mark(it, r);
            matches.Children.Clear();
            matches.Children.Add(new TrackList(r.List.Take(6), "На Audius этого трека нет. Оригинал есть только в Spotify."));
        };
        return wrap;
    }

    static void StyleFind(Button b, ChartMatch.Kind k)
    {
        if (b.Content is TextBlock tb) tb.Text = ChartMatch.Label[k];
        else if (b.Content is StackPanel sp && sp.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock t2) t2.Text = ChartMatch.Label[k];
        U.SetChipBtnOn(b, k == ChartMatch.Kind.Orig);
        b.Opacity = k == ChartMatch.Kind.None ? 0.55 : 1;
    }

    void Mark(ChartItem it, ChartMatch.Result r)
    {
        if (findBtns.TryGetValue(it.Pos, out var b)) StyleFind(b, r.Kind);
    }

    async Task PlayFound()
    {
        if (data == null) return;
        playBtn.IsEnabled = false;
        var queue = new List<Track>();
        var items = data.Items.Take(50).ToList();
        var started = false; var done = 0;
        var country = cc;
        for (var i = 0; i < items.Count; i += 5)
        {
            var got = await Task.WhenAll(items.Skip(i).Take(5).Select(async it => { var r = await ChartMatch.Match(country, it); Mark(it, r); return r.List.FirstOrDefault(); }));
            done += got.Length;
            foreach (var t in got)
                if (t != null && queue.All(q => q.Id != t.Id) && Recs.BaseScore(t, ChartCtx) > -1e8) queue.Add(t);
            prog.Text = $"проверено {done} из {items.Count}, нашлось {queue.Count}";
            if (!started && queue.Count >= 3) { started = true; Player.I.PlayList(queue.ToList()); }
            else if (started) Player.I.Append(queue);
        }
        if (!started && queue.Count > 0) Player.I.PlayList(queue);
        if (queue.Count == 0) Toast("Из этого чарта на Audius ничего не нашлось");
        playBtn.IsEnabled = true;
    }
}
