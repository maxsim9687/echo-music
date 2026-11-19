using Echo.Core;
using Echo.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Echo.Pages;

// Загрузка (admin): audio file + optional cover, metadata, upload with progress, list with delete.
public sealed class UploadPage : PageBase
{
    static readonly string[] AudioExt = { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".aac" };
    static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png", ".webp" };
    readonly TextBox title, artist, bpm;
    readonly AutoSuggestBox genre, mood;
    readonly TextBlock dropT, coverT, note = U.Muted("", 13, true);
    readonly ProgressBar bar = new() { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed, MinHeight = 4 };
    readonly Button go;
    readonly StackPanel listHost = new();
    StorageFile? file, cover;
    string? delArm;

    public UploadPage()
    {
        var form = new StackPanel { Spacing = 18 };

        // drop zone
        dropT = U.Bold("Перетащите аудиофайл или нажмите", 16);
        var dz = U.V(8, U.Icon(Glyph.Upload, 26), dropT, U.Muted("WAV, FLAC, MP3, OGG, M4A · до 500 МБ", 13));
        foreach (var c in dz.Children.OfType<FrameworkElement>()) c.HorizontalAlignment = HorizontalAlignment.Center;
        var drop = new Button
        {
            Content = dz, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(24, 36, 24, 36), CornerRadius = Theme.R, BorderThickness = new Thickness(Math.Max(1, Theme.T.Bw)), AllowDrop = true,
        };
        U.Style(drop, Theme.Card, Theme.Fg, Theme.Line, Theme.Card2, Theme.Fg, Theme.Fg);
        drop.Click += async (s, e) => { var f = await Pick(AudioExt); if (f != null) await SetFile(f); };
        drop.DragOver += (s, e) => { e.AcceptedOperation = DataPackageOperation.Copy; e.DragUIOverride.Caption = "Загрузить"; };
        drop.Drop += async (s, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var items = await e.DataView.GetStorageItemsAsync();
            var f = items.OfType<StorageFile>().FirstOrDefault(x => AudioExt.Contains(Path.GetExtension(x.Name).ToLowerInvariant()));
            if (f != null) await SetFile(f); else note.Text = "Это не аудиофайл";
        };
        form.Children.Add(drop);

        // fields
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < 3; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Field(int r, int c, string label, FrameworkElement ctl)
        {
            var f = U.V(6, U.Muted(label, 13), ctl);
            Grid.SetRow(f, r); Grid.SetColumn(f, c); grid.Children.Add(f);
        }
        title = U.Input("Название трека"); Field(0, 0, "Название", title);
        artist = U.Input("Исполнитель"); Field(0, 1, "Исполнитель", artist);
        genre = Suggest("Lo-Fi", Recs.GenreList); Field(1, 0, "Жанр", genre);
        mood = Suggest("Peaceful", Recs.MoodList); Field(1, 1, "Настроение", mood);
        bpm = U.Input("120"); Field(2, 0, "Темп, BPM", bpm);
        coverT = U.Muted("не выбрана", 13);
        var coverBtn = U.Pill("Выбрать обложку", async () => { var f = await Pick(ImageExt); if (f != null) { cover = f; coverT.Text = f.Name; } });
        var coverRow = U.H(12, coverBtn, coverT); coverT.VerticalAlignment = VerticalAlignment.Center;
        Field(2, 1, "Обложка", coverRow);
        form.Children.Add(grid);

        go = U.Pill("Загрузить", () => _ = Upload(), solid: true);
        form.Children.Add(U.H(14, go, note));
        form.Children.Add(bar);
        note.VerticalAlignment = VerticalAlignment.Center;

        Body.Children.Add(U.Section("Загрузить музыку", "WAV, FLAC и MP3 хранятся без перекодирования", form));
        Body.Children.Add(U.Section("Загруженные треки", (string?)null, listHost));
        _ = RenderList();
    }

    static AutoSuggestBox Suggest(string placeholder, string[] items)
    {
        var a = new AutoSuggestBox { PlaceholderText = placeholder, FontFamily = Theme.BodyFont };
        a.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var q = s.Text.Trim().ToLowerInvariant();
            s.ItemsSource = q.Length == 0 ? items : items.Where(x => x.ToLowerInvariant().Contains(q)).ToArray();
        };
        a.GotFocus += (s, e) => { if (string.IsNullOrEmpty(a.Text)) { a.ItemsSource = items; a.IsSuggestionListOpen = true; } };
        a.SuggestionChosen += (s, e) => s.Text = e.SelectedItem?.ToString() ?? s.Text;
        return a;
    }

    static async Task<StorageFile?> Pick(string[] exts)
    {
        var p = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.MusicLibrary, ViewMode = PickerViewMode.List };
        foreach (var e in exts) p.FileTypeFilter.Add(e);
        WinRT.Interop.InitializeWithWindow.Initialize(p, Shell.Main.Hwnd);
        try { return await p.PickSingleFileAsync(); }
        catch (Exception e) { Logger.Write("picker: " + e.Message); return null; }
    }

    async Task SetFile(StorageFile f)
    {
        file = f;
        long size = 0;
        try { size = (long)(await f.GetBasicPropertiesAsync()).Size; } catch { }
        dropT.Text = $"{f.Name} · {size / 1048576.0:0.0} МБ";
        if (string.IsNullOrWhiteSpace(title.Text))
        {
            var b = Path.GetFileNameWithoutExtension(f.Name);
            var m = b.Split(" - ");
            if (m.Length > 1) { if (string.IsNullOrWhiteSpace(artist.Text)) artist.Text = m[0].Trim(); title.Text = string.Join(" - ", m.Skip(1)).Trim(); }
            else title.Text = b;
        }
        note.Text = "";
    }

    static async Task<double> DurationOf(StorageFile f)
    {
        try { var mp = await f.Properties.GetMusicPropertiesAsync(); if (mp.Duration.TotalSeconds > 0) return mp.Duration.TotalSeconds; } catch { }
        try { return await Task.Run(() => { using var r = new NAudio.Wave.MediaFoundationReader(f.Path); return r.TotalTime.TotalSeconds; }); } catch { }
        return 0;
    }

    static string Mime(string name)
    {
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".mp3" => "audio/mpeg", ".wav" => "audio/wav", ".flac" => "audio/flac", ".ogg" => "audio/ogg", ".m4a" => "audio/mp4", ".aac" => "audio/aac",
            ".jpg" or ".jpeg" => "image/jpeg", ".png" => "image/png", ".webp" => "image/webp", _ => "application/octet-stream",
        };
    }

    async Task Upload()
    {
        if (file == null) { note.Text = "Выберите аудиофайл"; return; }
        if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(artist.Text)) { note.Text = "Укажите название и исполнителя"; return; }
        go.IsEnabled = false; bar.Visibility = Visibility.Visible; bar.Value = 0;
        try
        {
            var dur = await DurationOf(file);
            string Q(string s) => Uri.EscapeDataString(s.Trim());
            var url = $"/api/uploads?title={Q(title.Text)}&artist={Q(artist.Text)}&genre={Q(genre.Text ?? "")}&mood={Q(mood.Text ?? "")}&bpm={Q(bpm.Text)}&filename={Q(file.Name)}&duration={(dur > 0 ? dur.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "")}";
            IdResponse res;
            await using (var s = await file.OpenStreamForReadAsync())
            {
                var prog = new Progress<double>(p => { bar.Value = p * 100; note.Text = $"Загружаю… {Math.Round(p * 100)}%"; });
                res = await Api.Upload<IdResponse>(url, s, s.Length, Mime(file.Name), prog);
            }
            var coverFailed = false;
            if (cover != null && !string.IsNullOrEmpty(res.Id))
            {
                note.Text = "Загружаю обложку…";
                try
                {
                    await using var cs = await cover.OpenStreamForReadAsync();
                    await Api.Upload<object>($"/api/uploads/{Uri.EscapeDataString(res.Id)}/cover", cs, cs.Length, Mime(cover.Name), null);
                }
                catch { coverFailed = true; }
            }
            note.Text = coverFailed ? "Трек загружен, а обложка нет. Попробуйте файл JPG или PNG до 8 МБ." : "Готово";
            Toast("Трек загружен и уже доступен в поиске и волне");
            file = null; cover = null; title.Text = artist.Text = bpm.Text = ""; genre.Text = mood.Text = "";
            dropT.Text = "Перетащите аудиофайл или нажмите"; coverT.Text = "не выбрана";
            Api.ClearCache(); Wave.ResetPool();
            await RenderList();
        }
        catch (Exception e) { note.Text = e.Message; }
        go.IsEnabled = true; bar.Visibility = Visibility.Collapsed;
    }

    async Task RenderList()
    {
        listHost.Children.Clear();
        listHost.Children.Add(U.Loading(3));
        var l = await Catalog.LocalTracks(fresh: true);
        if (!Alive) return;
        listHost.Children.Clear();
        listHost.Children.Add(new TrackList(l, "Вы ещё ничего не загрузили", onDelete: t => _ = Delete(t)));
    }

    async Task Delete(Track t)
    {
        if (delArm != t.Id)
        {
            delArm = t.Id;
            Toast($"Нажмите ещё раз, чтобы удалить «{t.Title}»");
            var id = t.Id;
            _ = Task.Delay(4000).ContinueWith(_ => Bus.Ui(() => { if (delArm == id) delArm = null; }));
            return;
        }
        delArm = null;
        try
        {
            await Api.Delete($"/api/uploads/{Uri.EscapeDataString(t.RawId ?? "")}");
            Toast("Трек удалён");
            Api.ClearCache(); Wave.ResetPool();
            await RenderList();
        }
        catch (Exception e) { Toast(e.Message); }
    }
}
