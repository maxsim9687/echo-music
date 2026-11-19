using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Echo.Core;
using Echo.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Echo.Pages;

// Настройка вкуса: genres, favourite artists, favourite songs (→ likes).
public sealed class WelcomePage : PageBase
{
    readonly WrapPanel genres = U.Wrap(8), artists = U.Wrap(8);
    readonly TextBox artistBox, songBox;
    readonly TextBlock artistNote = U.Muted("", 13, true), summary = U.Muted("", 13);
    readonly StackPanel songs = new();
    readonly DispatcherQueueTimer timer;
    int gen;

    public WelcomePage()
    {
        var intro = new StackPanel { Spacing = 10, Margin = new Thickness(0, 24, 0, 0) };
        intro.Children.Add(U.Eyebrow("Настройка вкуса"));
        intro.Children.Add(U.Display("Расскажите, что вы любите", 52));
        intro.Children.Add(U.Muted("Волна сразу подстроится под ответы. Всё это можно поменять потом в настройках.", 15, true));
        Body.Children.Add(intro);

        Body.Children.Add(U.Section("Жанры", "выберите несколько", genres));

        var form = new Grid { ColumnSpacing = 10 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        artistBox = U.Input("Например: Skrillex, deadmau5");
        artistBox.KeyDown += (s, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = AddArtists(); } };
        form.Children.Add(artistBox);
        var add = U.Pill("Добавить", () => _ = AddArtists(), solid: true);
        Grid.SetColumn(add, 1); form.Children.Add(add);
        var aSec = U.Section("Любимые артисты", "впишите имя и нажмите «Добавить»", form);
        aSec.Children.Add(artists);
        aSec.Children.Add(artistNote);
        Body.Children.Add(aSec);

        songBox = U.Input("Название песни или исполнитель");
        var sSec = U.Section("Любимые песни", "найдите и нажмите на сердце: песня попадёт в любимое", songBox);
        sSec.Children.Add(songs);
        Body.Children.Add(sSec);

        var done = U.Pill("Готово, к моей волне", () => Shell.Main.Go("home"), solid: true);
        var fin = U.H(16, done, summary); summary.VerticalAlignment = VerticalAlignment.Center;
        Body.Children.Add(fin);

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350); timer.IsRepeating = false;
        timer.Tick += (s, e) => { s.Stop(); _ = SearchSongs(); };
        songBox.TextChanged += (s, e) => { timer.Stop(); timer.Start(); };
        U.Bind(this, () => Player.I.StateChanged += Summary, () => Player.I.StateChanged -= Summary);
        Render();
    }

    void Render()
    {
        genres.Children.Clear();
        foreach (var g in Recs.StarterGenres)
            genres.Children.Add(U.Chip(g, Recs.P.G.GetValueOrDefault(g) > 0, b =>
            {
                var on = !U.IsOn(b); U.SetChip(b, on); Recs.SeedGenre(g, on); Wave.ResetPool(); Summary();
            }));
        artists.Children.Clear();
        foreach (var (id, name) in Recs.P.Fav.ToList())
        {
            var chip = new Border { BorderBrush = Theme.Line, BorderThickness = Theme.Bw, CornerRadius = Theme.Btn(36), Padding = new Thickness(14, 4, 4, 4) };
            var x = U.IconBtn(Glyph.Close, $"Убрать {name}", () => { Recs.RemoveFavArtist(id); Wave.ResetPool(); Render(); }, 28, 10);
            var row = U.H(6, U.Txt(name, 14), x); ((FrameworkElement)row.Children[0]).VerticalAlignment = VerticalAlignment.Center;
            chip.Child = row;
            artists.Children.Add(chip);
        }
        if (songBox.Text.Trim().Length == 0)
        {
            songs.Children.Clear();
            songs.Children.Add(new TrackList(Catalog.RefreshAll(Recs.P.Likes.Values.Take(8)), "Найдите песню, которую любите, и нажмите на сердце"));
        }
        Summary();
    }

    void Summary() => summary.Text = $"Жанров: {Recs.TopGenres(20).Count} · артистов: {Recs.P.Fav.Count} · песен в любимом: {Recs.P.Likes.Count}";

    async Task AddArtists()
    {
        var names = artistBox.Text.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Take(5).ToList();
        if (names.Count == 0) { artistBox.Focus(FocusState.Programmatic); return; }
        artistNote.Text = "Ищу…";
        var missed = new List<string>();
        await Task.WhenAll(names.Select(async name =>
        {
            try
            {
                var list = await Catalog.AuSearch(name, 30);
                var low = name.ToLowerInvariant();
                var hit = list.FirstOrDefault(t => t.Artist.ToLowerInvariant() == low) ?? list.FirstOrDefault(t => t.Artist.ToLowerInvariant().Contains(low));
                if (hit != null) Recs.AddFavArtist(hit.ArtistId, hit.Artist, hit.Genre); else lock (missed) missed.Add(name);
            }
            catch { lock (missed) missed.Add(name); }
        }));
        artistBox.Text = "";
        artistNote.Text = missed.Count > 0 ? $"Не нашёл в каталоге: {string.Join(", ", missed)}. Похоже, их музыки нет на Audius." : "Добавлено";
        Wave.ResetPool();
        Render();
    }

    async Task SearchSongs()
    {
        var q = songBox.Text.Trim();
        var g = ++gen;
        if (q.Length == 0) { Render(); return; }
        songs.Children.Clear(); songs.Children.Add(U.Loading(4));
        var (list, failed) = await Catalog.SearchAll(q);
        if (g != gen || !Alive) return;
        songs.Children.Clear();
        if (failed) { songs.Children.Add(U.Status("Поиск не ответил. Попробуйте ещё раз.", true)); return; }
        songs.Children.Add(new TrackList(list.Take(10), $"«{q}» не нашлось. Попробуйте написать по-английски или только исполнителя."));
    }
}
