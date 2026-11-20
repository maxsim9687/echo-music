using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;
using Echo.Core;
using Echo.Pages;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace Echo.UI;

// Main window: NavigationView on the left, pages in the middle, player bar at the bottom, spots behind everything.
public sealed class Shell : Window
{
    public static Shell Main { get; private set; } = null!;
    public static FrameworkElement? WaveAnchor { get; set; }

    readonly Grid root;
    readonly Spots spots;
    Grid? layer;
    NavigationView? nav;
    Grid? host, overlay;
    NavigationViewItem? uploadItem;
    string view = "home";
    bool suppressNav;
    readonly DispatcherQueueTimer centerTimer;

    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);
    public string View => view;

    public Shell()
    {
        Main = this;
        Title = "Эхо";
        ExtendsContentIntoTitleBar = true;
        root = new Grid();
        spots = new Spots();
        root.Children.Add(spots);
        Content = root;

        try
        {
            var ico = Path.Combine(AppContext.BaseDirectory, "Assets", "Echo.ico");
            if (File.Exists(ico)) AppWindow.SetIcon(ico);
            AppWindow.Title = "Эхо";
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int w = Math.Min(1360, area.Width - 40), h = Math.Min(880, area.Height - 40);
            AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h));
            if (AppWindow.Presenter is OverlappedPresenter op) { op.PreferredMinimumWidth = 760; op.PreferredMinimumHeight = 560; }
        }
        catch (Exception e) { Logger.Write("window: " + e.Message); }

        Bus.ToastRequested += t => Bus.Ui(() => Toasts.Show(t));
        Account.Changed += () => Bus.Ui(RefreshAccount);
        Player.I.StateChanged += () => spots.Energy(Player.I.IsPlaying);

        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((s, e) =>
        {
            var p = e.GetCurrentPoint(root).Position; spots.Pointer((float)p.X, (float)p.Y);
        }), true);
        root.PointerExited += (s, e) => spots.PointerGone();
        root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKey), true);

        centerTimer = DispatcherQueue.CreateTimer();
        centerTimer.Interval = TimeSpan.FromMilliseconds(200);
        centerTimer.Tick += (s, e) => TrackCenter();
        centerTimer.Start();

        Closed += (s, e) =>
        {
            centerTimer.Stop();
            Settings.Save(); Api.SaveCookies();
            spots.Shutdown();
            Updater.Apply(restart: false); // a downloaded update is installed as the app closes
        };

        ApplyTheme();
    }

    // ---------------- theme ----------------
    public void ApplyTheme()
    {
        Theme.Apply(Settings.Current.Theme);
        root.RequestedTheme = Theme.Element;
        Theme.ApplyResources(root);
        try { SystemBackdrop = Theme.T.Glass ? new DesktopAcrylicBackdrop() : null; } catch { SystemBackdrop = null; }
        root.Background = Theme.Bg;
        try
        {
            var tb = AppWindow.TitleBar;
            tb.ButtonBackgroundColor = Colors.Transparent; tb.ButtonInactiveBackgroundColor = Colors.Transparent;
            tb.ButtonForegroundColor = Theme.FgC; tb.ButtonInactiveForegroundColor = Theme.MutedC;
            tb.ButtonHoverBackgroundColor = Theme.Card2C.A == 0 ? Theme.Alpha(Theme.FgC, 0.1).Color : Theme.Card2C; tb.ButtonHoverForegroundColor = Theme.FgC;
            tb.ButtonPressedBackgroundColor = Theme.LineC; tb.ButtonPressedForegroundColor = Theme.FgC;
        }
        catch { }
        spots.SetTheme(Theme.T.Id);
        BuildLayer();
        Go(view);
    }

    public void SetTheme(int v)
    {
        Settings.Current.Theme = v; Settings.Save();
        ApplyTheme();
    }

    void BuildLayer()
    {
        if (layer != null) root.Children.Remove(layer);
        if (Toasts.Host.Parent is Panel old) old.Children.Remove(Toasts.Host);
        Toasts.Host.Children.Clear();
        WaveAnchor = null;

        layer = new Grid();
        layer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        layer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // custom title bar
        var titleBar = new Grid { Background = Theme.Transparent, Padding = new Thickness(16, 0, 140, 0) };
        var logoSmall = U.H(8, LogoMark(14), U.Txt("Эхо", 12, FontWeights.SemiBold, Theme.Muted));
        logoSmall.VerticalAlignment = VerticalAlignment.Center;
        titleBar.Children.Add(logoSmall);
        layer.Children.Add(titleBar);
        SetTitleBar(titleBar);

        // navigation
        nav = new NavigationView
        {
            PaneDisplayMode = NavigationViewPaneDisplayMode.Auto,
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            OpenPaneLength = 248,
            ExpandedModeThresholdWidth = 1000,
            CompactModeThresholdWidth = 0,
            IsTitleBarAutoPaddingEnabled = false,
            AlwaysShowHeader = false,
            IsPaneToggleButtonVisible = true,
        };
        Theme.ApplyResources(nav);
        var logo = U.H(10, LogoMark(26), U.Display("Эхо", 28));
        logo.Margin = new Thickness(14, 4, 0, 12);
        nav.PaneHeader = logo;
        nav.MenuItems.Add(Item("Главная", Glyph.Home, "home"));
        nav.MenuItems.Add(Item("Поиск", Glyph.Search, "search"));
        nav.MenuItems.Add(Item("Чарты", Glyph.Chart, "charts"));
        nav.MenuItems.Add(Item("Коллекция", Glyph.Library, "library"));
        uploadItem = Item("Загрузка", Glyph.Upload, "upload");
        nav.MenuItems.Add(uploadItem);
        nav.FooterMenuItems.Add(Item("Настройки", Glyph.Settings, "prefs"));
        nav.SelectionChanged += (s, e) =>
        {
            if (suppressNav) return;
            if (e.SelectedItemContainer?.Tag is string tag) Go(tag);
        };
        nav.PaneOpening += (s, e) => { if (s.PaneFooter is FrameworkElement f) f.Visibility = Visibility.Visible; };
        nav.PaneClosing += (s, e) => { if (s.PaneFooter is FrameworkElement f) f.Visibility = Visibility.Collapsed; };
        nav.DisplayModeChanged += (s, e) => { if (s.PaneFooter is FrameworkElement f) f.Visibility = s.IsPaneOpen && e.DisplayMode != NavigationViewDisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed; };
        host = new Grid();
        nav.Content = host;
        Grid.SetRow(nav, 1);
        layer.Children.Add(nav);

        var bar = new PlayerBar();
        Grid.SetRow(bar, 2);
        layer.Children.Add(bar);

        overlay = new Grid { Visibility = Visibility.Collapsed };
        Grid.SetRow(overlay, 1);
        layer.Children.Add(overlay);

        Grid.SetRow(Toasts.Host, 1);
        layer.Children.Add(Toasts.Host);

        root.Children.Add(layer);
        RefreshAccount();
    }

    static FrameworkElement LogoMark(double size)
    {
        var g = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        g.Children.Add(new Border { CornerRadius = new CornerRadius(size / 2), BorderBrush = Theme.Fg, BorderThickness = new Thickness(Math.Max(1.5, size / 14)) });
        var inset = size / 4;
        g.Children.Add(new Border { CornerRadius = new CornerRadius(size / 2), Background = Theme.Fg, Margin = new Thickness(inset) });
        return g;
    }

    NavigationViewItem Item(string text, string glyph, string tag) => new()
    {
        Content = new TextBlock { Text = text, FontFamily = Theme.BodyFont, FontSize = 14, FontWeight = FontWeights.Medium },
        Icon = new FontIcon { Glyph = glyph, FontFamily = Theme.Icons },
        Tag = tag,
    };

    void RefreshAccount()
    {
        if (nav == null) return;
        if (uploadItem != null) uploadItem.Visibility = Account.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        var box = new StackPanel { Spacing = 10, Margin = new Thickness(16, 8, 16, 14) };
        if (Account.Me == null)
        {
            var login = U.Pill("Войти по почте", () => Go("login"));
            login.HorizontalAlignment = HorizontalAlignment.Stretch;
            box.Children.Add(login);
        }
        else
        {
            box.Children.Add(U.Bold(Account.Me.Email, 13));
            box.Children.Add(U.Muted(Account.IsAdmin ? "администратор" : "слушатель", 12));
            box.Children.Add(U.Link("Выйти", () => _ = Logout()));
        }
        var note = new StackPanel { Spacing = 2, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0) };
        note.Children.Add(U.Txt("Только для себя", 12, FontWeights.SemiBold));
        note.Children.Add(U.Muted("Полные треки независимых артистов из Audius и Jamendo, плюс ваша собственная музыка.", 12, true));
        box.Children.Add(note);
        box.Visibility = nav.IsPaneOpen && nav.DisplayMode != NavigationViewDisplayMode.Compact && nav.DisplayMode != NavigationViewDisplayMode.Minimal ? Visibility.Visible : Visibility.Collapsed;
        nav.PaneFooter = box;
        if (view == "upload" && !Account.IsAdmin) Go("home");
    }

    public async Task Logout()
    {
        await Account.Logout();
        Player.I.NotifyChanged();
        Toasts.Show("Вы вышли");
        Go("home");
    }

    // ---------------- navigation ----------------
    public void Go(string v)
    {
        if (v == "upload" && !Account.IsAdmin) v = Account.Me != null ? "home" : "login";
        view = v;
        CloseNowPlaying();
        if (host == null || nav == null) return;
        host.Children.Clear();
        WaveAnchor = null;
        FrameworkElement page = v switch
        {
            "search" => new SearchPage(),
            "charts" => new ChartsPage(),
            "library" => new LibraryPage(),
            "login" => new LoginPage(),
            "upload" => new UploadPage(),
            "prefs" => new SettingsPage(),
            "welcome" => new WelcomePage(),
            _ => new HomePage(),
        };
        host.Children.Add(page);
        suppressNav = true;
        try
        {
            var all = nav.MenuItems.Concat(nav.FooterMenuItems).OfType<NavigationViewItem>();
            nav.SelectedItem = all.FirstOrDefault(i => (string)i.Tag == v);
        }
        finally { suppressNav = false; }
    }

    public void OpenNowPlaying()
    {
        if (overlay == null) return;
        overlay.Children.Clear();
        overlay.Children.Add(new NowPlaying());
        overlay.Visibility = Visibility.Visible;
    }

    public void CloseNowPlaying()
    {
        if (overlay == null) return;
        overlay.Visibility = Visibility.Collapsed;
        overlay.Children.Clear();
    }

    // ---------------- shared actions ----------------
    public static void DoLike(Track t)
    {
        var on = Recs.Like(t);
        Toasts.Show(on ? "Добавлено в любимое" : "Убрано из любимого");
        Player.I.NotifyChanged();
    }

    public static void NotThis()
    {
        var t = Player.I.Current;
        if (t == null) return;
        Recs.NotThis(t);
        Toasts.Show("Больше не покажу похожее так часто");
        Player.I.Next("ban");
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && overlay?.Visibility == Visibility.Visible) { CloseNowPlaying(); e.Handled = true; return; }
        if (e.Key != VirtualKey.Space || e.Handled) return;
        var f = root.XamlRoot == null ? null : FocusManager.GetFocusedElement(root.XamlRoot);
        if (f is TextBox or AutoSuggestBox or PasswordBox or ButtonBase or Slider or ToggleSwitch or RichEditBox) return;
        Player.I.Toggle();
        e.Handled = true;
    }

    void TrackCenter()
    {
        try
        {
            var a = WaveAnchor;
            if (a == null || a.XamlRoot == null || Theme.T.Id != 9) { spots.Center(-1, -1); return; }
            var p = a.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(a.ActualWidth / 2, a.ActualHeight / 2));
            spots.Center((float)p.X, (float)p.Y);
        }
        catch { spots.Center(-1, -1); }
    }
}
