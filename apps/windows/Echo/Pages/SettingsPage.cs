using Echo.Core;
using Echo.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Echo.Pages;

// Настройки: 10 themes, taste bars, taste setup/reset, sources, account, downloads folder.
public sealed class SettingsPage : PageBase
{
    readonly StackPanel taste = new() { Spacing = 22 };
    readonly WrapPanel prefGenres = U.Wrap(8);
    readonly TextBlock resetNote = U.Muted("", 13);
    Button resetBtn = null!;
    bool armed;

    public SettingsPage()
    {
        // themes
        var themes = U.Wrap(12);
        foreach (var th in Theme.All)
        {
            var t = th;
            var sw = new Grid { Height = 64, Width = 150, CornerRadius = new CornerRadius(Math.Min(Theme.T.R, 10)) };
            var bgc = Theme.Hex(t.Bg); var fgc = Theme.Hex(t.Fg);
            sw.Background = new SolidColorBrush(t.Glass ? Windows.UI.Color.FromArgb(255, 0x2a, 0x2a, 0x2a) : bgc);
            var bars = U.V(6,
                new Border { Height = 8, Width = 70, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(fgc), HorizontalAlignment = HorizontalAlignment.Left },
                new Border { Height = 8, Width = 46, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(fgc), Opacity = 0.4, HorizontalAlignment = HorizontalAlignment.Left });
            bars.VerticalAlignment = VerticalAlignment.Center; bars.Margin = new Thickness(14, 0, 0, 0);
            sw.Children.Add(bars);
            var name = new TextBlock { Text = $"{t.Id}. {t.Name}", FontFamily = new FontFamily(t.Display), FontWeight = new Windows.UI.Text.FontWeight { Weight = Math.Max((ushort)400, t.HeroWeight) }, FontStyle = t.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal, FontSize = 15, Foreground = Theme.Fg };
            var sub = U.Muted(t.Id == 10 ? "по умолчанию" : " ", 12);
            var sp = U.V(8, sw, name, sub);
            var b = new Button { Content = sp, Padding = new Thickness(10), CornerRadius = new CornerRadius(Math.Min(Theme.T.R, 14)), BorderThickness = new Thickness(t.Id == Theme.T.Id ? 2 : 1) };
            U.Style(b, Theme.Transparent, Theme.Fg, t.Id == Theme.T.Id ? Theme.Fg : Theme.Line, Theme.Card, Theme.Fg, Theme.Fg);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"Тема {t.Id}: {t.Name}");
            b.Click += (s, e) => Shell.Main.SetTheme(t.Id);
            themes.Children.Add(b);
        }
        Body.Children.Add(U.Section("Оформление", "10 стилей, как на сайте", themes));

        // taste
        var tasteSec = U.Section("Ваш вкус", "так волна видит вас сейчас", taste);
        tasteSec.Children.Add(prefGenres);
        resetBtn = U.Pill("Сбросить вкус", Reset);
        var row = U.H(12, U.Pill("Настроить вкус", () => Shell.Main.Go("welcome"), solid: true), resetBtn, resetNote);
        resetNote.VerticalAlignment = VerticalAlignment.Center;
        tasteSec.Children.Add(row);
        Body.Children.Add(tasteSec);

        // sources
        var srcs = new StackPanel { Spacing = 14 };
        void Src(string name, string text)
        {
            var g = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 0, 0, 12), BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.Children.Add(U.Bold(name));
            var t = U.Muted(text, 14, true); Grid.SetColumn(t, 1); g.Children.Add(t);
            srcs.Children.Add(g);
        }
        Src("Audius", "Подключён. Полные треки, MP3 320 kbps. Скачивание там, где автор разрешил.");
        Src("Jamendo", Catalog.Jamendo ? "Подключён. Скачивание FLAC без потерь." : "Не подключён. Нужен бесплатный Client ID в настройках сервера.");
        Src("Ваша музыка", "Файлы, которые загружает администратор. Хранятся без перекодирования.");
        Body.Children.Add(U.Section("Источники музыки", (string?)null, srcs));

        // account
        var acct = new StackPanel { Spacing = 10 };
        if (Account.Me == null)
        {
            acct.Children.Add(U.Muted("Войдите, чтобы вкус, любимое и плейлисты сохранялись в аккаунте и работала ИИшка.", 14, true));
            acct.Children.Add(U.H(0, U.Pill("Войти по почте", () => Shell.Main.Go("login"), solid: true)));
        }
        else
        {
            acct.Children.Add(U.Bold(Account.Me.Email, 16));
            acct.Children.Add(U.Muted(Account.IsAdmin ? "администратор" : "слушатель", 14));
            acct.Children.Add(U.H(0, U.Pill("Выйти", () => _ = Shell.Main.Logout())));
        }
        Body.Children.Add(U.Section("Аккаунт", (string?)null, acct));

        // downloads
        var dl = new StackPanel { Spacing = 10 };
        dl.Children.Add(U.Muted($"Скачанные треки сохраняются в {Downloads.Folder} и играют без интернета.", 14, true));
        dl.Children.Add(U.H(0, U.Pill("Открыть папку", () => Downloads.OpenFolder(), glyph: Glyph.Folder)));
        Body.Children.Add(U.Section("Скачивание", (string?)null, dl));

        // updates
        var up = new StackPanel { Spacing = 10 };
        up.Children.Add(U.Muted($"Версия 1.0.{Updater.Current}. Новые версии скачиваются сами и ставятся при закрытии приложения.", 14, true));

        var check = U.Pill(Updater.ReadyBuild > 0 ? $"Перезапустить с версией 1.0.{Updater.ReadyBuild}" : "Проверить обновления", async () =>
        {
            if (Updater.ReadyBuild > 0) { Updater.Apply(restart: true); Shell.Main.Close(); return; }
            Toasts.Show("Проверяю обновления…", ms: 2000);
            if (await Updater.Check() == 0) Toasts.Show("У вас последняя версия");
        });
        up.Children.Add(U.H(0, check));
        Body.Children.Add(U.Section("Обновления", (string?)null, up));

        RenderTaste();
    }

    void RenderTaste()
    {
        taste.Children.Clear();
        var cols = new Grid { ColumnSpacing = 32 };
        for (var i = 0; i < 3; i++) cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var c = 0;
        foreach (var (title, map) in new[] { ("Жанры", Recs.P.G), ("Настроения", Recs.P.M), ("Артисты", Recs.P.A) })
        {
            var col = new StackPanel { Spacing = 8 };
            col.Children.Add(U.Txt(title, 16, FontWeights.SemiBold));
            var bars = Recs.TasteBars(map, 6);
            if (bars.Count == 0) col.Children.Add(U.Muted("Пока пусто: послушайте и полайкайте пару треков", 13, true));
            var max = bars.Count > 0 ? bars[0].Value : 1;
            foreach (var (k, v) in bars)
            {
                var label = map == Recs.P.A ? Recs.P.An.GetValueOrDefault(k) ?? "артист" : k;
                var g = new Grid { ColumnSpacing = 10 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
                g.Children.Add(U.Muted(label, 13));
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
                var frac = Math.Max(0.06, v / max);
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(frac, GridUnitType.Star) });
                track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - frac), GridUnitType.Star) });
                track.Children.Add(new Border { Background = Theme.Fg, CornerRadius = new CornerRadius(3) });
                Grid.SetColumn(track, 1); g.Children.Add(track);
                var val = U.Muted(v.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), 12); val.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(val, 2); g.Children.Add(val);
                col.Children.Add(g);
            }
            Grid.SetColumn(col, c++); cols.Children.Add(col);
        }
        taste.Children.Add(cols);
        prefGenres.Children.Clear();
        foreach (var g in Recs.StarterGenres)
            prefGenres.Children.Add(U.Chip(g, Recs.P.G.GetValueOrDefault(g) > 0, b =>
            {
                var on = !U.IsOn(b); Recs.SeedGenre(g, on); Wave.ResetPool(); RenderTaste();
            }));
    }

    void Reset()
    {
        if (!armed)
        {
            armed = true; SetText(resetBtn, "Точно сбросить?");
            _ = Task.Delay(4000).ContinueWith(_ => Bus.Ui(() => { armed = false; SetText(resetBtn, "Сбросить вкус"); }));
            return;
        }
        armed = false;
        Recs.Reset(); Wave.ResetPool();
        SetText(resetBtn, "Сбросить вкус");
        resetNote.Text = "Вкус сброшен. Любимое и плейлисты остались.";
        RenderTaste();
    }

    static void SetText(Button b, string t) { if (b.Content is TextBlock tb) tb.Text = t; }
}
