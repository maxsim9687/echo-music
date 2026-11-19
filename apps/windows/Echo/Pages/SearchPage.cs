using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Echo.Core;
using Echo.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Echo.Pages;

// Поиск (Audius + Jamendo + uploads in parallel, debounce 350 ms) + ИИшка.
public sealed class SearchPage : PageBase
{
    static string query = "", src = "all";
    static bool onlyDl;
    static (string title, string? desc, string note, List<Track> tracks)? aiResult;
    static readonly string[] AiExamples = { "Под учёбу, без слов", "На пробежку", "Ночная дорога", "Перед сном", "Вечеринка дома", "Когда грустно" };

    readonly TextBox q, aiQ;
    readonly Button aiGo;
    readonly StackPanel aiOut = new() { Spacing = 16, Margin = new Thickness(0, 32, 0, 0), Visibility = Visibility.Collapsed }, results = new();
    readonly TextBlock srTitle, srNote = U.Note();
    readonly List<(Button b, string v)> srcBtns = new();
    readonly DispatcherQueueTimer timer;
    int searchGen;

    public SearchPage()
    {
        // ИИшка
        var ai = new StackPanel { Spacing = 16, Padding = new Thickness(26), };
        var aiCard = new Border { Child = ai, CornerRadius = Theme.R2, BorderBrush = Theme.Line, BorderThickness = Theme.Bw, Background = Theme.HeroBg };
        var badge = new Border { BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = Theme.Btn(28), Padding = new Thickness(10, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center, Child = U.H(6, U.Icon(Glyph.Spark, 13), U.Txt("ИИшка", 13, FontWeights.SemiBold)) };
        var head = U.H(14, badge, U.H2("Соберу плейлист под задачу"));
        ai.Children.Add(head);
        var form = new Grid { ColumnSpacing = 10 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        aiQ = U.Input("Например: под учёбу, без слов, часа на два");
        aiQ.KeyDown += (s, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; _ = RunAi(); } };
        form.Children.Add(aiQ);
        aiGo = U.Pill("Собрать", () => _ = RunAi(), solid: true);
        Grid.SetColumn(aiGo, 1); form.Children.Add(aiGo);
        ai.Children.Add(form);
        ai.Children.Add(U.Wrap(8, AiExamples.Select(x => (UIElement)U.Chip(x, false, btn => { aiQ.Text = x; _ = RunAi(); }))));
        Body.Children.Add(U.V(0, aiCard, aiOut));
        if (aiResult is { } r) ShowAi(r.title, r.note, r.tracks);

        // search box + filters
        var box = new StackPanel { Spacing = 14 };
        q = U.Input("Трек, исполнитель или жанр", query);
        q.FontSize = 16; q.Padding = new Thickness(18, 12, 18, 12);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(q, "Поиск музыки");
        box.Children.Add(q);
        var filters = U.Wrap(8);
        filters.Children.Add(U.Chip("Только со скачиванием", onlyDl, b => { onlyDl = !onlyDl; U.SetChip(b, onlyDl); _ = Run(); }));
        foreach (var (v, l) in new[] { ("all", "Все источники"), ("audius", "Audius"), ("jamendo", "Jamendo"), ("local", "Ваша музыка") })
        {
            var b = U.Chip(l, src == v, btn =>
            {
                if (v == "jamendo" && !Catalog.Jamendo) { Toast("Jamendo не подключён: добавьте JAMENDO_CLIENT_ID на сервере"); return; }
                src = v;
                foreach (var (x, xv) in srcBtns) U.SetChip(x, xv == src);
                _ = Run();
            });
            srcBtns.Add((b, v)); filters.Children.Add(b);
        }
        box.Children.Add(filters);
        Body.Children.Add(box);

        srTitle = U.H2("Поиск");
        var sec = new StackPanel { Spacing = 16 };
        var hdr = U.H(14, srTitle, srNote); srNote.VerticalAlignment = VerticalAlignment.Bottom; srNote.Margin = new Thickness(0, 0, 0, 5);
        sec.Children.Add(hdr);
        sec.Children.Add(results);
        Body.Children.Add(sec);

        timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(350);
        timer.IsRepeating = false;
        timer.Tick += (s, e) => { s.Stop(); _ = Run(); };
        q.TextChanged += (s, e) => { query = q.Text; timer.Stop(); timer.Start(); };
        Loaded += (s, e) => { q.Focus(FocusState.Programmatic); q.SelectionStart = q.Text.Length; };
        _ = Run();
    }

    async Task Run()
    {
        var text = q.Text.Trim();
        var gen = ++searchGen;
        results.Children.Clear();
        if (text.Length == 0)
        {
            srTitle.Text = Theme.Head("Поиск"); srNote.Text = "";
            results.Children.Add(new TrackList(Array.Empty<Track>(), "Введите название трека, имя артиста или жанр"));
            return;
        }
        srTitle.Text = Theme.Head($"«{text}»"); srNote.Text = "";
        results.Children.Add(U.Loading());
        var (list, failed) = await Catalog.SearchAll(text, src, onlyDl);
        if (gen != searchGen || !Alive) return;
        results.Children.Clear();
        if (failed) { results.Children.Add(U.Status("Поиск не ответил. Проверьте интернет.", true)); return; }
        srNote.Text = $"найдено: {list.Count}";
        results.Children.Add(new TrackList(list, onlyDl ? "Нет треков со скачиванием по этому запросу" : $"По запросу «{text}» ничего нет. Попробуйте на английском или другой жанр."));
    }

    // ---------------- ИИшка: prompt → plan → gather → rank like web ----------------
    async Task RunAi()
    {
        var prompt = aiQ.Text.Trim();
        if (prompt.Length == 0) { aiQ.Focus(FocusState.Programmatic); return; }
        aiGo.IsEnabled = false; SetAiBtn("Собираю…");
        aiOut.Children.Clear(); aiOut.Visibility = Visibility.Visible;
        aiOut.Children.Add(U.Section("Думаю над подборкой…", (string?)null, U.Loading(8)));
        try
        {
            var resp = await Api.Post<AiResponse>("/api/ai/playlist", new { prompt }, default);
            var plan = resp.Plan ?? throw new ApiException("Пустой ответ", 500);
            var jobs = plan.Queries.Select(x => Catalog.AuSearch(x, 25)).ToList();
            foreach (var g in plan.Genres) jobs.Add(Catalog.AuTrending(g, "week", 30));
            if (Catalog.Jamendo)
            {
                foreach (var x in plan.Queries.Take(2)) jobs.Add(Catalog.JaSearch(x, 20));
                if (plan.Genres.Count > 0) jobs.Add(Catalog.JaPopular(string.Join("+", plan.Genres.Select(Catalog.JTag)), 30, plan.Instrumental));
            }
            jobs.Add(LocalFor(plan));
            try { await Task.WhenAll(jobs); } catch { }
            var cands = Catalog.Dedupe(jobs.Where(j => j.Status == TaskStatus.RanToCompletion).SelectMany(j => j.Result));
            var list = Recs.Pick(cands, plan.Size > 0 ? plan.Size : 25, new ScoreCtx { Plan = plan, Cfg = new WaveSettings(), Explore = 1.2 }, 2);
            var mins = (int)Math.Round(list.Sum(t => t.Duration) / 60);
            var note = $"{list.Count} треков · {mins} мин · {(plan.Source == "rules" ? "подбор по правилам" : "подобрал ИИ")}";
            aiResult = (plan.Title, plan.Description, note, list);
            if (Alive) ShowAi(plan.Title, note, list);
        }
        catch (ApiException e)
        {
            aiOut.Children.Clear();
            if (e.Status == 401)
            {
                var s = U.Section("Нужно войти", (string?)null, null);
                s.Children.Add(U.H(10, U.Status("ИИшка работает для вошедших.", true), U.Link("Войти по почте", () => Shell.Main.Go("login"))));
                aiOut.Children.Add(s);
            }
            else aiOut.Children.Add(U.Section("Не получилось собрать", (string?)null, U.Status($"{e.Message}. Попробуйте ещё раз.", true)));
        }
        catch (Exception e)
        {
            aiOut.Children.Clear();
            aiOut.Children.Add(U.Section("Не получилось собрать", (string?)null, U.Status($"{e.Message}. Попробуйте ещё раз.", true)));
        }
        aiGo.IsEnabled = true; SetAiBtn("Собрать");
    }

    static async Task<List<Track>> LocalFor(AiPlan plan)
    {
        var l = await Catalog.LocalTracks();
        return l.Where(t => (t.Genre != null && plan.Genres.Contains(t.Genre)) || (t.Mood != null && plan.Moods.Contains(t.Mood))).ToList();
    }

    void SetAiBtn(string text) { if (aiGo.Content is TextBlock tb) tb.Text = text; }

    void ShowAi(string title, string note, List<Track> tracks)
    {
        aiOut.Children.Clear(); aiOut.Visibility = Visibility.Visible;
        var right = U.H(10, U.Pill("Слушать", () => Player.I.PlayList(tracks)), U.Pill("Сохранить", () => SaveAi(title, tracks)));
        aiOut.Children.Add(U.Section(title, note, new TrackList(tracks, "Не нашлось подходящих треков. Переформулируйте запрос."), right));
    }

    static void SaveAi(string title, List<Track> tracks)
    {
        if (tracks.Count == 0) return;
        var id = ToBase36(Recs.Now);
        Recs.P.Playlists.Insert(0, new Playlist { Id = id, Title = title, Desc = aiResult?.desc, Tracks = tracks.Take(50).Select(Recs.Slim).ToList(), At = Recs.Now });
        if (Recs.P.Playlists.Count > 40) Recs.P.Playlists.RemoveRange(40, Recs.P.Playlists.Count - 40);
        Recs.Save();
        Toasts.Show($"Плейлист «{title}» сохранён в коллекцию");
    }

    static string ToBase36(long v)
    {
        const string d = "0123456789abcdefghijklmnopqrstuvwxyz";
        var s = "";
        while (v > 0) { s = d[(int)(v % 36)] + s; v /= 36; }
        return s.Length == 0 ? "0" : s;
    }
}
