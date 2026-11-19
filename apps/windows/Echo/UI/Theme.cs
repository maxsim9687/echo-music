using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace Echo.UI;

// The 10 looks from web styles.css `:root[data-v]` (same names and colours). Fonts approximated with system fonts.
public sealed record ThemeDef(
    int Id, string Name, string Bg, string Fg, string Muted, string Line, string Card, string Card2, byte P,
    double R, double RBtn, double CoverR, double Bw, bool Light, string Display, ushort HeroWeight, bool Italic, bool Upper,
    string Body = "Segoe UI Variable Text, Segoe UI", bool RoundCovers = false, bool Glass = false);

public static class Theme
{
    public static readonly ThemeDef[] All =
    {
        new(1, "Монолит", "#050505", "#f2f2f2", "#8a8a8a", "#232323", "#111111", "#1a1a1a", 242, 14, 999, 10, 1, false, "Segoe UI Variable Display, Segoe UI", 700, false, false),
        new(2, "Бумага", "#f5f5f4", "#121212", "#6b6b6b", "#d6d6d4", "#ececea", "#e2e2e0", 18, 2, 2, 0, 1, true, "Georgia, Cambria", 400, true, false),
        new(3, "Брутализм", "#ffffff", "#000000", "#444444", "#000000", "#ffffff", "#f0f0f0", 0, 0, 0, 0, 3, true, "Arial Black, Segoe UI Black", 900, false, true),
        new(4, "Плёнка", "#141414", "#e6e6e6", "#8d8d8d", "#2c2c2c", "#1b1b1b", "#222222", 230, 4, 4, 2, 1, false, "Segoe UI Variable Display, Segoe UI", 700, false, true),
        new(5, "Созвездие", "#000000", "#fafafa", "#7d7d7d", "#1d1d1d", "#0b0b0b", "#151515", 250, 22, 999, 18, 1, false, "Segoe UI Variable Display, Segoe UI", 800, false, false),
        new(6, "Осциллограф", "#0a0a0a", "#ededed", "#858585", "#242424", "#121212", "#1a1a1a", 237, 8, 999, 6, 1, false, "Segoe UI Black, Segoe UI", 900, false, false),
        new(7, "Снег", "#0e0e0f", "#f0f0f0", "#8e8e90", "#252527", "#151516", "#1d1d1f", 245, 24, 999, 20, 1, false, "Georgia, Cambria", 400, true, false),
        new(8, "Сетка", "#ffffff", "#0a0a0a", "#6f6f6f", "#e3e3e3", "#f4f4f4", "#ebebeb", 10, 0, 0, 0, 1, true, "Segoe UI Black, Segoe UI", 900, false, false),
        new(9, "Винил", "#101010", "#eeeeee", "#8b8b8b", "#262626", "#171717", "#1f1f1f", 238, 16, 999, 16, 1, false, "Georgia, Cambria", 400, false, false, RoundCovers: true),
        new(10, "Стекло", "#161616", "#f4f4f4", "#9a9a9a", "#1fffffff", "#0fffffff", "#1affffff", 255, 20, 999, 14, 1, false, "Segoe UI Variable Display, Segoe UI", 300, false, false, Glass: true),
    };

    public static ThemeDef T { get; private set; } = All[9];
    public static event Action? Changed;

    public static Color BgC, FgC, MutedC, LineC, CardC, Card2C;
    public static SolidColorBrush Bg = null!, Fg = null!, Muted = null!, Line = null!, Card = null!, Card2 = null!, Transparent = null!, BarBg = null!, HeroBg = null!;
    public static FontFamily Display = null!, BodyFont = null!, Icons = null!;

    public static void Apply(int id)
    {
        T = All.FirstOrDefault(x => x.Id == id) ?? All[9];
        BgC = Hex(T.Bg); FgC = Hex(T.Fg); MutedC = Hex(T.Muted); LineC = Hex(T.Line); CardC = Hex(T.Card); Card2C = Hex(T.Card2);
        Bg = new SolidColorBrush(T.Glass ? Color.FromArgb(0x8c, BgC.R, BgC.G, BgC.B) : BgC);
        Fg = new SolidColorBrush(FgC); Muted = new SolidColorBrush(MutedC); Line = new SolidColorBrush(LineC);
        Card = new SolidColorBrush(CardC); Card2 = new SolidColorBrush(Card2C);
        Transparent = new SolidColorBrush(Colors.Transparent);
        BarBg = new SolidColorBrush(T.Glass ? Color.FromArgb(0x99, 22, 22, 22) : BgC);
        HeroBg = new SolidColorBrush(T.Glass ? CardC : Color.FromArgb((byte)(CardC.A * 0.35), CardC.R, CardC.G, CardC.B));
        Display = new FontFamily(T.Display);
        BodyFont = new FontFamily(T.Body);
        Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        Changed?.Invoke();
    }

    public static ElementTheme Element => T.Light ? ElementTheme.Light : ElementTheme.Dark;
    public static FontWeight HeroWeight => new() { Weight = T.HeroWeight };
    public static FontStyle HeroStyle => T.Italic ? FontStyle.Italic : FontStyle.Normal;
    public static string Head(string s) => T.Upper ? s.ToUpperInvariant() : s;
    public static CornerRadius Radius(double r) => new(r);
    public static CornerRadius R => new(T.R);
    public static CornerRadius R2 => new(T.R * 2);
    public static CornerRadius Btn(double height) => new(Math.Min(T.RBtn, height / 2));
    public static CornerRadius Cover(double size) => T.RoundCovers ? new(size / 2) : new(Math.Min(T.CoverR, size / 2));
    public static Thickness Bw => new(T.Bw);
    public static Color Particle => Color.FromArgb(255, T.P, T.P, T.P);

    public static Color Hex(string h)
    {
        h = h.TrimStart('#');
        if (h.Length == 6) return Color.FromArgb(255, Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
        if (h.Length == 8) return Color.FromArgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16), Convert.ToByte(h[6..8], 16));
        return Colors.Gray;
    }

    public static SolidColorBrush Alpha(Color c, double a) => new(Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B));

    // Lightweight styling: monochrome accents for every Fluent control under `root`.
    public static void ApplyResources(FrameworkElement root)
    {
        var r = root.Resources;
        void B(string key, Brush b) => r[key] = b;
        var fgBrush = Fg; var bgSolid = new SolidColorBrush(BgC);
        foreach (var k in new[] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "AccentFillColorSelectedTextBackgroundBrush",
                     "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                     "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed",
                     "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                     "CheckBoxCheckBackgroundFillChecked", "CheckBoxCheckBackgroundFillCheckedPointerOver", "CheckBoxCheckBackgroundFillCheckedPressed",
                     "CheckBoxCheckBackgroundStrokeChecked", "CheckBoxCheckBackgroundStrokeCheckedPointerOver",
                     "ProgressBarForeground", "ProgressBarIndeterminateForeground", "ProgressRingForegroundThemeBrush",
                     "NavigationViewSelectionIndicatorForeground", "TextControlBorderBrushFocused", "TextControlElevationBorderFocusedBrush",
                     "HyperlinkButtonForeground", "HyperlinkButtonForegroundPointerOver" })
            B(k, fgBrush);
        foreach (var k in new[] { "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush", "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
                     "CheckBoxCheckGlyphForegroundChecked", "CheckBoxCheckGlyphForegroundCheckedPointerOver", "CheckBoxCheckGlyphForegroundCheckedPressed" })
            B(k, bgSolid);
        B("SliderTrackFill", Line); B("SliderTrackFillPointerOver", Line); B("SliderTrackFillPressed", Line);
        B("SliderOuterThumbBackground", Card2);
        B("TextControlBackground", Card); B("TextControlBackgroundPointerOver", Card2); B("TextControlBackgroundFocused", Card2);
        B("TextControlBorderBrush", Line); B("TextControlBorderBrushPointerOver", Fg);
        B("TextControlForeground", Fg); B("TextControlForegroundFocused", Fg); B("TextControlForegroundPointerOver", Fg);
        B("TextControlPlaceholderForeground", Muted); B("TextControlPlaceholderForegroundFocused", Muted); B("TextControlPlaceholderForegroundPointerOver", Muted);
        B("NavigationViewDefaultPaneBackground", Transparent); B("NavigationViewExpandedPaneBackground", Transparent); B("NavigationViewTopPaneBackground", Transparent);
        B("NavigationViewContentBackground", Transparent); B("NavigationViewContentGridBorderBrush", Transparent);
        B("NavigationViewItemForeground", Muted); B("NavigationViewItemForegroundPointerOver", Fg); B("NavigationViewItemForegroundSelected", Fg);
        B("NavigationViewItemForegroundSelectedPointerOver", Fg); B("NavigationViewItemForegroundPressed", Fg); B("NavigationViewItemForegroundSelectedPressed", Fg);
        B("NavigationViewItemBackgroundSelected", Card2); B("NavigationViewItemBackgroundSelectedPointerOver", Card2); B("NavigationViewItemBackgroundPointerOver", Card);
        B("MenuFlyoutPresenterBackground", new SolidColorBrush(T.Glass ? Color.FromArgb(0xF0, 0x1e, 0x1e, 0x1e) : BgC));
        B("MenuFlyoutPresenterBorderBrush", Line);
        B("FlyoutPresenterBackground", new SolidColorBrush(T.Glass ? Color.FromArgb(0xF0, 0x1e, 0x1e, 0x1e) : BgC));
        B("ScrollBarThumbFill", Line);
        r["ContentControlThemeFontFamily"] = BodyFont;
    }
}
