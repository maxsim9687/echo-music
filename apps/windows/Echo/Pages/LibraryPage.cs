using Echo.Core;
using Echo.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Echo.Pages;

// Коллекция: Любимое, Мои плейлисты, Недавно слушали, Скачанное (offline files).
public sealed class LibraryPage : PageBase
{
    readonly StackPanel downloaded = new();

    public LibraryPage()
    {
        var liked = Catalog.RefreshAll(Recs.P.Likes.Values.OrderByDescending(t => t.LikedAt ?? 0));
        Body.Children.Add(U.Section("Любимое", liked.Count > 0 ? $"{liked.Count} треков" : null,
            new TrackList(liked, "Нажмите на сердечко у трека, и он появится здесь"),
            liked.Count > 0 ? U.Pill("Слушать", () => Player.I.PlayList(liked), solid: true) : null));

        UIElement pls;
        if (Recs.P.Playlists.Count == 0) pls = U.Status("Попросите ИИшку в поиске собрать плейлист и сохраните его");
        else
        {
            var row = U.Wrap(8);
            var i = 0;
            foreach (var p in Recs.P.Playlists)
            {
                var pl = p;
                var card = U.Card(pl.Tracks.FirstOrDefault(t => t.Cover != null)?.Cover, pl.Title, $"{pl.Tracks.Count} треков", () => Player.I.PlayList(pl.Tracks), 172, i++);
                var menu = new MenuFlyout();
                var del = new MenuFlyoutItem { Text = "Удалить плейлист" };
                del.Click += (s, e) => { Recs.P.Playlists.Remove(pl); Recs.Save(); Shell.Main.Go("library"); Toast($"Плейлист «{pl.Title}» удалён"); };
                menu.Items.Add(del);
                card.ContextFlyout = menu;
                row.Children.Add(card);
            }
            pls = row;
        }
        Body.Children.Add(U.Section("Мои плейлисты", (string?)null, pls));

        Body.Children.Add(U.Section("Недавно слушали", (string?)null, new TrackList(Catalog.RefreshAll(Recs.P.Hist.Take(40)), "История появится после первых прослушиваний")));

        Body.Children.Add(U.Section("Скачанное", "Музыка\\Эхо, играет без интернета", downloaded, U.Pill("Открыть папку", () => Downloads.OpenFolder(), glyph: Glyph.Folder)));
        RenderDownloads();
        U.Bind(this, () => Downloads.Changed += RenderDownloads, () => Downloads.Changed -= RenderDownloads);
    }

    void RenderDownloads()
    {
        downloaded.Children.Clear();
        downloaded.Children.Add(new TrackList(Downloads.Downloaded(), "Скачайте трек кнопкой со стрелкой: он сохранится в папку «Музыка\\Эхо» и будет играть без интернета"));
    }
}
