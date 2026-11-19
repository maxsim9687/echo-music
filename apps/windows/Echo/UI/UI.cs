using FontWeight = Windows.UI.Text.FontWeight;
using Echo.Core;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.UI;

namespace Echo.UI;

// Segoe Fluent Icons code points.
public static class Glyph
{
    public const string Play = "", Pause = "", Prev = "", Next = "", Shuffle = "";
    public const string Heart = "", HeartFill = "", Download = "", Upload = "", Search = "";
    public const string Home = "", Settings = "", Library = "", Chart = "", Wave = "";
    public const string Ban = "", Open = "", Folder = "", Delete = "", Close = "";
    public const string ChevronDown = "", ChevronRight = "", Volume = "", Spark = "", Person = "";
    public const string Check = "", Music = "", FullScreen = "", List = "", Mail = "", Refresh = "";
    public const string Tune = "";
}

public static class U
{
    // ---------------- text ----------------
    public static TextBlock Txt(string text, double size = 15, FontWeight? weight = null, Brush? fg = null, bool wrap = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, FontFamily = Theme.BodyFont, Foreground = fg ?? Theme.Fg,
            FontWeight = weight ?? FontWeights.Normal,
            TextWrapping = wrap ? TextWrapping.WrapWholeWords : TextWrapping.NoWrap,
            TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
        };
        return t;
    }
    public static TextBlock Muted(string text, double size = 13, bool wrap = false) => Txt(text, size, null, Theme.Muted, wrap);
    public static TextBlock Bold(string text, double size = 15) => Txt(text, size, FontWeights.SemiBold);

    public static TextBlock Display(string text, double size)
    {
        return new TextBlock
        {
            Text = Theme.Head(text), FontSize = size, FontFamily = Theme.Display, FontWeight = Theme.HeroWeight, FontStyle = Theme.HeroStyle,
            Foreground = Theme.Fg, CharacterSpacing = -30, TextWrapping = TextWrapping.WrapWholeWords, TextTrimming = TextTrimming.None,
        };
    }

    public static TextBlock H2(string text) => Display(text, 28);

    public static FrameworkElement Eyebrow(string text, bool dot = true)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (dot) sp.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = Theme.Fg, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new TextBlock { Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, CharacterSpacing = 160, Foreground = Theme.Muted, FontFamily = Theme.BodyFont, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }


    public static FontIcon Icon(string glyph, double size = 16, Brush? fg = null) =>
        new() { Glyph = glyph, FontSize = size, FontFamily = Theme.Icons, Foreground = fg ?? Theme.Fg };

    // ---------------- buttons (all go through Btns: web .btn / .mood / .seg / .chip-btn / .ib / .like) ----------------
    public static void Style(Control b, Brush bg, Brush fg, Brush border, Brush hoverBg, Brush hoverFg, Brush hoverBorder, string kind = "Button")
    {
        if (b is Button btn) Btns.Custom(btn, bg, fg, border, hoverBg, hoverFg, hoverBorder);
        else { b.Background = bg; b.Foreground = fg; b.BorderBrush = border; }
    }

    static object Content(string? text, string? glyph, double size = 14, bool semibold = false)
    {
        var w = semibold ? FontWeights.SemiBold : FontWeights.Normal;
        if (glyph == null) return new TextBlock { Text = text ?? "", FontFamily = Theme.BodyFont, FontSize = size, FontWeight = w };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        sp.Children.Add(new FontIcon { Glyph = glyph, FontFamily = Theme.Icons, FontSize = size + 2, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(text)) sp.Children.Add(new TextBlock { Text = text, FontFamily = Theme.BodyFont, FontSize = size, FontWeight = w, VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }

    // `.btn` / `.btn.solid`
    public static Button Pill(string text, Action? click = null, bool solid = false, string? glyph = null)
    {
        var b = new Button { Content = Content(text, glyph, 14, true) };
        Btns.Make(b, solid ? BtnKind.Solid : BtnKind.Btn);
        if (click != null) b.Click += (s, e) => click();
        return b;
    }

    // `.chip-btn` («Нравится», «Не то», «Настроить»)
    public static Button ChipBtn(string text, string? glyph, Action click)
    {
        var b = new Button { Content = Content(text, glyph, 13) };
        Btns.Make(b, BtnKind.Chip);
        b.Click += (s, e) => click();
        return b;
    }
    public static void SetChipBtnOn(Button b, bool on) => Btns.SetOn(b, on);

    // `.mood` toggle chip
    public static Button Chip(string text, bool on, Action<Button> click)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontFamily = Theme.BodyFont, FontSize = 14 } };
        Btns.Make(b, BtnKind.Mood);
        Btns.SetOn(b, on);
        b.Click += (s, e) => click(b);
        return b;
    }

    // `.seg button`: segmented choice, pressed = fg background
    public static Button Seg(string text, bool on, Action<Button> click)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontFamily = Theme.BodyFont, FontSize = 13 } };
        Btns.Make(b, BtnKind.Seg);
        Btns.SetOn(b, on);
        b.Click += (s, e) => click(b);
        return b;
    }

    public static void SetChip(Button b, bool on) => Btns.SetOn(b, on);
    public static bool IsOn(Button b) => Btns.IsOn(b);

    // `.ib` (44 px, player bar), `.ib.main` (filled), `.like` / `.dl` (36 px, rows)
    public static Button IconBtn(string glyph, string tip, Action click, double size = 40, double iconSize = 16, bool main = false)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontFamily = Theme.Icons, FontSize = iconSize }, Width = size, Height = size };
        Btns.Make(b, main ? BtnKind.IbMain : size <= 36 ? BtnKind.Icon : BtnKind.Ib);
        ToolTipService.SetToolTip(b, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, tip);
        b.Click += (s, e) => click();
        return b;
    }

    public static void SetGlyph(Button b, string glyph) { if (b.Content is FontIcon f) f.Glyph = glyph; }

    // `.linkbtn`
    public static Button Link(string text, Action click)
    {
        var b = new Button { Content = new TextBlock { Text = text, FontFamily = Theme.BodyFont, FontSize = 14, TextDecorations = Windows.UI.Text.TextDecorations.Underline } };
        Btns.Make(b, BtnKind.Link);
        b.Click += (s, e) => click();
        return b;
    }

    // ---------------- covers ----------------
    public static Brush Pattern(int i)
    {
        var fg = Theme.FgC; var c2 = Theme.Card2C;
        var g = new LinearGradientBrush();
        var angles = new[] { (0.0, 0.0, 1.0, 1.0), (1.0, 0.0, 0.0, 1.0), (0.5, 0.0, 0.5, 1.0), (0.2, 0.0, 0.8, 1.0), (0.0, 0.5, 1.0, 0.5), (0.0, 1.0, 1.0, 0.0), (0.5, 1.0, 0.5, 0.0), (1.0, 1.0, 0.0, 0.0) };
        var a = angles[((i % 8) + 8) % 8];
        g.StartPoint = new Point(a.Item1, a.Item2); g.EndPoint = new Point(a.Item3, a.Item4);
        g.GradientStops.Add(new GradientStop { Color = Color.FromArgb((byte)(i % 2 == 0 ? 210 : 150), fg.R, fg.G, fg.B), Offset = 0 });
        g.GradientStops.Add(new GradientStop { Color = c2.A == 0 ? Color.FromArgb(40, fg.R, fg.G, fg.B) : c2, Offset = 0.72 });
        return g;
    }

    public static Border Cover(string? url, double size, int i = 0, CornerRadius? radius = null, bool border = true)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = radius ?? Theme.Cover(size), Background = Pattern(i),
            BorderBrush = Theme.Line, BorderThickness = border ? new Thickness(Math.Min(Theme.T.Bw, size > 100 ? Theme.T.Bw : 1)) : new Thickness(0),
        };
        SetCover(b, url, size);
        return b;
    }

    public static void SetCover(Border b, string? url, double size, int i = 0)
    {
        if (string.IsNullOrEmpty(url)) { b.Child = null; b.Background = Pattern(i); return; }
        try
        {
            var bmp = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = (int)Math.Max(48, size) };
            bmp.UriSource = Api.Abs(url);
            b.Child = new Image { Source = bmp, Stretch = Stretch.UniformToFill };
        }
        catch { b.Child = null; }
    }

    // ---------------- layout ----------------
    public static StackPanel V(double spacing = 0, params UIElement[] kids)
    {
        var s = new StackPanel { Spacing = spacing };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    public static StackPanel H(double spacing = 0, params UIElement[] kids)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var k in kids) s.Children.Add(k);
        return s;
    }

    public static WrapPanel Wrap(double gap = 8, IEnumerable<UIElement>? kids = null)
    {
        var w = new WrapPanel { HSpacing = gap, VSpacing = gap };
        if (kids != null) foreach (var k in kids) w.Children.Add(k);
        return w;
    }

    // Section with a header row (title + small note + optional right side).
    public static TextBlock Note(string text = "") => Muted(text, 12);

    public static StackPanel Section(string title, string? note, UIElement? content, UIElement? right = null) =>
        SectionN(title, note == null ? null : Note(note), content, right);

    public static StackPanel SectionN(string title, TextBlock? n, UIElement? content, UIElement? right = null)
    {
        var head = new Grid { ColumnSpacing = 14 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var h = H2(title); h.TextWrapping = TextWrapping.NoWrap; h.TextTrimming = TextTrimming.CharacterEllipsis;
        head.Children.Add(h);
        if (n != null)
        {
            n.VerticalAlignment = VerticalAlignment.Bottom; n.Margin = new Thickness(0, 0, 0, 5);
            Grid.SetColumn(n, 1); head.Children.Add(n);
        }
        if (right != null) { Grid.SetColumn((FrameworkElement)right, 2); ((FrameworkElement)right).VerticalAlignment = VerticalAlignment.Center; head.Children.Add(right); }
        var s = new StackPanel { Spacing = 16 };
        s.Children.Add(head);
        if (content != null) s.Children.Add(content);
        return s;
    }

    public static ScrollViewer HScroll(UIElement content) => new()
    {
        Content = content, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 14),
    };

    public static TextBlock Status(string text, bool err = false)
    {
        var t = Txt(text, 14, null, err ? Theme.Fg : Theme.Muted, true);
        t.Margin = new Thickness(8, 20, 8, 20);
        return t;
    }

    public static Border Skeleton(double w, double h, double r = 6) => new()
    {
        Width = w, Height = h, CornerRadius = new CornerRadius(r), Background = Theme.Card2, Opacity = 0.8,
    };

    public static FrameworkElement Loading(int rows = 6)
    {
        var s = new StackPanel { Spacing = 0 };
        for (var i = 0; i < rows; i++)
        {
            var g = new Grid { Padding = new Thickness(8, 10, 8, 10), ColumnSpacing = 14 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var c = Skeleton(44, 44); Grid.SetColumn(c, 1); g.Children.Add(c);
            var t = Skeleton(220, 14, 4); t.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetColumn(t, 2); g.Children.Add(t);
            s.Children.Add(g);
        }
        return s;
    }

    // Card (`.card`): cover on top, title and subtitle under it.
    public static Button Card(string? cover, string title, string sub, Action click, double size = 172, int i = 0, bool round = false)
    {
        var cv = Cover(cover, size, i, round ? new CornerRadius(size / 2) : null);
        var sp = new StackPanel { Spacing = 6, Width = size };
        sp.Children.Add(cv);
        var tt = Bold(title, 15); tt.Margin = new Thickness(0, 6, 0, 0); if (round) tt.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(tt);
        if (!string.IsNullOrEmpty(sub)) { var s = Muted(sub, 13); if (round) s.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(s); }
        var b = new Button { Content = sp, Padding = new Thickness(8), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(Math.Min(Theme.T.R, 12)), VerticalAlignment = VerticalAlignment.Top };
        Style(b, Theme.Transparent, Theme.Fg, Theme.Transparent, Theme.Card, Theme.Fg, Theme.Transparent);
        b.Click += (s, e) => click();
        b.Tag = cv;
        return b;
    }

    // Subscribe while the element is in the tree.
    public static void Bind(FrameworkElement el, Action on, Action off)
    {
        var attached = false;
        el.Loaded += (s, e) => { if (!attached) { attached = true; on(); } };
        el.Unloaded += (s, e) => { if (attached) { attached = false; off(); } };
    }

    public static bool InsideButton(object? src, DependencyObject stop)
    {
        var d = src as DependencyObject;
        while (d != null && d != stop)
        {
            if (d is ButtonBase) return true;
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    public static string Fmt(double s) => s > 0 ? $"{(int)(s / 60)}:{(int)(s % 60):00}" : "—";

    public static TextBox Input(string placeholder, string? text = null)
    {
        var t = new TextBox { PlaceholderText = placeholder, Text = text ?? "", FontFamily = Theme.BodyFont, FontSize = 15, Padding = new Thickness(14, 9, 14, 9), CornerRadius = Theme.Btn(40), BorderThickness = Theme.Bw };
        return t;
    }

    public static Slider Slider(double min, double max, double step, double value)
    {
        return new Slider { Minimum = min, Maximum = max, StepFrequency = step, SmallChange = step, Value = value, IsThumbToolTipEnabled = false };
    }
}

// Simple flow layout for chips (WinUI has no WrapPanel).
public sealed class WrapPanel : Panel
{
    public double HSpacing { get; set; } = 8;
    public double VSpacing { get; set; } = 8;

    protected override Size MeasureOverride(Size avail)
    {
        double x = 0, y = 0, rowH = 0, maxW = 0;
        var w = double.IsInfinity(avail.Width) ? double.MaxValue : avail.Width;
        foreach (var c in Children)
        {
            c.Measure(new Size(w, double.PositiveInfinity));
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > w) { y += rowH + VSpacing; x = 0; rowH = 0; }
            x += d.Width + HSpacing;
            rowH = Math.Max(rowH, d.Height);
            maxW = Math.Max(maxW, x - HSpacing);
        }
        return new Size(double.IsInfinity(avail.Width) ? maxW : Math.Min(avail.Width, maxW), y + rowH);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, rowH = 0;
        foreach (var c in Children)
        {
            var d = c.DesiredSize;
            if (x > 0 && x + d.Width > final.Width) { y += rowH + VSpacing; x = 0; rowH = 0; }
            c.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width + HSpacing;
            rowH = Math.Max(rowH, d.Height);
        }
        return final;
    }
}
