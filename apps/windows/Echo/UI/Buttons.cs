using System.Runtime.CompilerServices;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Echo.UI;

// Web button classes from public/styles.css, all drawn by one shared template.
public enum BtnKind
{
    Btn,      // .btn        pill, border var(--bw) solid var(--fg), weight 600, padding 16×10
    Solid,    // .btn.solid  fg background, bg text
    Mood,     // .mood       filter chip: line border, muted; hover fg; pressed fg border + card-2
    Seg,      // .seg button pressed = fg background, bg text
    Chip,     // .chip-btn   small pill with icon, pressed = fg
    Find,     // .crow .find chart badge (variants orig / ver / none)
    Ib,       // .ib         round icon button, muted → fg
    IbMain,   // .ib.main    filled fg, radius = --r-btn
    Icon,     // .like / .dl 36 px round, disabled 30 %
    Link,     // .linkbtn    muted underlined text → fg
    Knob,     // .vibe .knob 88 px filled circle
    Custom,   // cards, tiles, theme swatches: explicit brushes
}

public static class Btns
{
    sealed class State
    {
        public BtnKind Kind;
        public bool On, Hover, Down, Hooked;
        public string Variant = "";
        public Brush? Bg, Fg, Border, HBg, HFg, HBorder;
    }

    static readonly ConditionalWeakTable<Button, State> States = new();
    static ControlTemplate? plain, hard;

    // One template for every button: optional hard offset shadow (theme 3), face border, content.
    // Hover / pressed are driven from code (Refresh), so per-kind looks stay in one place.
    static ControlTemplate Template(bool shadow)
    {
        const string ns = "xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'";
        var sh = shadow ? "<Border Margin='6,6,-6,-6' Background='{TemplateBinding BorderBrush}' CornerRadius='{TemplateBinding CornerRadius}' IsHitTestVisible='False'/>" : "";
        var xaml =
            "<ControlTemplate " + ns + " TargetType='Button'>" +
            "<Grid Background='Transparent'>" + sh +
            "<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}'>" +
            "<Border.BackgroundTransition><BrushTransition Duration='0:0:0.15'/></Border.BackgroundTransition>" +
            "<ContentPresenter Margin='{TemplateBinding Padding}' Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' " +
            "Foreground='{TemplateBinding Foreground}' FontSize='{TemplateBinding FontSize}' FontWeight='{TemplateBinding FontWeight}' FontFamily='{TemplateBinding FontFamily}' " +
            "HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}'/>" +
            "</Border></Grid></ControlTemplate>";
        return (ControlTemplate)XamlReader.Load(xaml);
    }

    static ControlTemplate T(bool shadow)
    {
        return shadow ? hard ??= Template(true) : plain ??= Template(false);
    }

    static bool Brutal => Theme.T.Id == 3;
    static SolidColorBrush BgSolid => new(Theme.BgC);

    // Turns a Button into the given web class. Safe to call once per button.
    public static Button Make(Button b, BtnKind kind)
    {
        var st = States.GetValue(b, _ => new State());
        st.Kind = kind;
        var shadow = Brutal && kind is BtnKind.Btn or BtnKind.Solid or BtnKind.IbMain;
        b.Template = T(shadow);
        b.UseSystemFocusVisuals = true;
        b.FontFamily = Theme.BodyFont;
        if (kind != BtnKind.Custom) { b.VerticalContentAlignment = VerticalAlignment.Center; b.HorizontalContentAlignment = HorizontalAlignment.Center; }
        var bw = Theme.T.Bw;
        switch (kind)
        {
            case BtnKind.Btn:
            case BtnKind.Solid:
                b.Padding = new Thickness(16, 10, 16, 10); b.BorderThickness = new Thickness(bw); b.FontWeight = FontWeights.SemiBold; b.FontSize = 14;
                b.CornerRadius = Theme.Btn(40); break;
            case BtnKind.Mood:
                b.Padding = new Thickness(14, 9, 14, 9); b.BorderThickness = new Thickness(bw); b.FontSize = 14; b.CornerRadius = Theme.Btn(40); break;
            case BtnKind.Seg:
                b.Padding = new Thickness(12, 7, 12, 7); b.BorderThickness = new Thickness(bw); b.FontSize = 13; b.CornerRadius = Theme.Btn(34); break;
            case BtnKind.Chip:
                b.Padding = new Thickness(10, 6, 10, 6); b.BorderThickness = new Thickness(1); b.FontSize = 13; b.CornerRadius = Theme.Btn(32); break;
            case BtnKind.Find:
                b.Padding = new Thickness(12, 6, 12, 6); b.BorderThickness = new Thickness(bw); b.FontSize = 13; b.CornerRadius = Theme.Btn(32); break;
            case BtnKind.Ib:
            case BtnKind.Icon:
                b.Padding = new Thickness(0); b.BorderThickness = new Thickness(0); b.CornerRadius = new CornerRadius(Math.Max(b.Width, 36) / 2); break;
            case BtnKind.IbMain:
                b.Padding = new Thickness(0); b.BorderThickness = new Thickness(Brutal ? 3 : 0); b.CornerRadius = Theme.Btn(double.IsNaN(b.Width) ? 48 : b.Width); break;
            case BtnKind.Link:
                b.Padding = new Thickness(0, 4, 0, 4); b.BorderThickness = new Thickness(0); b.FontSize = 14; b.CornerRadius = new CornerRadius(0);
                b.HorizontalContentAlignment = HorizontalAlignment.Left; break;
            case BtnKind.Knob:
                b.Padding = new Thickness(0); b.BorderThickness = new Thickness(Brutal ? 3 : 0); b.CornerRadius = new CornerRadius(b.Width / 2);
                b.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5); b.RenderTransform = new ScaleTransform(); break;
        }

        if (!st.Hooked)
        {
            st.Hooked = true;
            b.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler((s, e) => { st.Hover = true; Refresh(b); }), true);
            b.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler((s, e) => { st.Hover = false; st.Down = false; Refresh(b); }), true);
            b.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((s, e) => { st.Down = true; Refresh(b); }), true);
            b.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((s, e) => { st.Down = false; Refresh(b); }), true);
            b.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((s, e) => { st.Down = false; Refresh(b); }), true);
            b.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((s, e) => { st.Hover = st.Down = false; Refresh(b); }), true);
            b.IsEnabledChanged += (s, e) => Refresh(b);
        }
        Refresh(b);
        return b;
    }

    public static Button Custom(Button b, Brush bg, Brush fg, Brush border, Brush hoverBg, Brush hoverFg, Brush hoverBorder)
    {
        var st = States.GetValue(b, _ => new State());
        st.Bg = bg; st.Fg = fg; st.Border = border; st.HBg = hoverBg; st.HFg = hoverFg; st.HBorder = hoverBorder;
        return Make(b, BtnKind.Custom);
    }

    public static void SetOn(Button b, bool on)
    {
        if (!States.TryGetValue(b, out var st)) return;
        st.On = on; Refresh(b);
    }
    public static bool IsOn(Button b) => States.TryGetValue(b, out var st) && st.On;

    public static void SetVariant(Button b, string v)
    {
        if (!States.TryGetValue(b, out var st)) return;
        st.Variant = v; Refresh(b);
    }

    static void Refresh(Button b)
    {
        if (!States.TryGetValue(b, out var st)) return;
        var hot = st.Hover && b.IsEnabled;
        var t = Theme.T;
        Brush bg = Theme.Transparent, fg = Theme.Fg, br = Theme.Transparent;
        double opacity = 1;
        var scale = 1.0;
        switch (st.Kind)
        {
            case BtnKind.Btn:
                bg = hot ? Theme.Card2 : t.Glass ? Theme.Card : Theme.Transparent; fg = Theme.Fg; br = Theme.Fg;
                if (st.On) { bg = Theme.Card2; }
                if (Brutal && bg == Theme.Transparent) bg = BgSolid; // box-shadow sits outside the box: keep the face opaque
                opacity = b.IsEnabled ? 1 : 0.5; break;
            case BtnKind.Solid:
                bg = hot ? Theme.Alpha(Theme.FgC, st.Down ? 0.78 : 0.88) : Theme.Fg; fg = BgSolid; br = Theme.Fg;
                opacity = b.IsEnabled ? 1 : 0.5; break;
            case BtnKind.Mood:
                if (st.On)
                {
                    bg = Brutal ? Theme.Fg : Theme.Card2; fg = Brutal ? BgSolid : Theme.Fg; br = Theme.Fg;
                }
                else
                {
                    bg = t.Glass ? Theme.Transparent : BgSolid; fg = hot ? Theme.Fg : Theme.Muted; br = hot ? Theme.Fg : Theme.Line;
                }
                break;
            case BtnKind.Seg:
                if (st.On) { bg = Theme.Fg; fg = BgSolid; br = Theme.Fg; }
                else { bg = Theme.Transparent; fg = hot ? Theme.Fg : Theme.Muted; br = hot ? Theme.Fg : Theme.Line; }
                break;
            case BtnKind.Chip:
                fg = st.On || hot ? Theme.Fg : Theme.Muted; br = st.On || hot ? Theme.Fg : Theme.Line;
                bg = t.Glass && hot ? Theme.Card : Theme.Transparent;
                opacity = b.IsEnabled ? 1 : 0.4; break;
            case BtnKind.Find:
                switch (st.Variant)
                {
                    case "orig": bg = Theme.Fg; fg = BgSolid; br = Theme.Fg; break;
                    case "ver": fg = Theme.Fg; br = Theme.Fg; break;
                    case "none": fg = Theme.Fg; br = hot ? Theme.Fg : Theme.Line; opacity = 0.5; break;
                    default: fg = Theme.Fg; br = hot ? Theme.Fg : Theme.Line; break;
                }
                break;
            case BtnKind.Ib:
            case BtnKind.Icon:
                fg = st.On || hot ? Theme.Fg : Theme.Muted;
                bg = hot && t.Glass ? Theme.Card : Theme.Transparent;
                opacity = b.IsEnabled ? 1 : 0.3; break;
            case BtnKind.IbMain:
                bg = hot ? Theme.Alpha(Theme.FgC, st.Down ? 0.78 : 0.88) : Theme.Fg; fg = BgSolid; br = Theme.Fg;
                opacity = b.IsEnabled ? 1 : 0.5; break;
            case BtnKind.Link:
                fg = hot ? Theme.Fg : Theme.Muted; break;
            case BtnKind.Knob:
                bg = Theme.Fg; fg = BgSolid; br = Theme.Fg;
                scale = st.Down ? 0.94 : hot ? 1.08 : 1; break;
            case BtnKind.Custom:
                bg = (hot ? st.HBg : st.Bg) ?? Theme.Transparent; fg = (hot ? st.HFg : st.Fg) ?? Theme.Fg; br = (hot ? st.HBorder : st.Border) ?? Theme.Transparent;
                opacity = b.IsEnabled ? 1 : 0.5; break;
        }
        b.Background = bg; b.Foreground = fg; b.BorderBrush = br; b.Opacity = opacity;
        if (b.RenderTransform is ScaleTransform sc) { sc.ScaleX = sc.ScaleY = scale; }
        // icon buttons: filled heart / glyph swap is handled by callers; keep the icon colour in sync
        if (b.Content is FontIcon fi) fi.Foreground = fg;
    }

    // ---------------- hero knob: 88 px fg circle, soft shadow, pulsing rings while playing ----------------
    public sealed class KnobView
    {
        public Grid Root { get; } = new();
        public Button Button { get; }
        readonly Storyboard rings = new();
        bool playing;

        public KnobView(Action click, double size = 88)
        {
            Root.Width = Root.Height = size;
            Root.VerticalAlignment = VerticalAlignment.Center;
            // soft shadow: 0 12px 40px fg at 18 %
            var glow = new Ellipse
            {
                Width = size + 64, Height = size + 64, Margin = new Thickness(-32, -20, -32, -44), IsHitTestVisible = false,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop { Color = Theme.Alpha(Theme.FgC, 0.18).Color, Offset = 0.45 },
                        new GradientStop { Color = Theme.Alpha(Theme.FgC, 0).Color, Offset = 1 },
                    },
                },
            };
            Root.Children.Add(glow);
            for (var i = 0; i < 2; i++)
            {
                var ring = new Ellipse
                {
                    Width = size + 16, Height = size + 16, Margin = new Thickness(-8), Stroke = Theme.Fg, StrokeThickness = 1, Opacity = 0, IsHitTestVisible = false,
                    RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                };
                var sc = new ScaleTransform { ScaleX = 0.95, ScaleY = 0.95 };
                ring.RenderTransform = sc;
                Root.Children.Add(ring);
                var begin = TimeSpan.FromSeconds(1.2 * i);
                var dur = new Duration(TimeSpan.FromSeconds(2.4));
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                foreach (var prop in new[] { "ScaleX", "ScaleY" })
                {
                    var a = new DoubleAnimation { From = 0.95, To = 1.45, Duration = dur, BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                    Storyboard.SetTarget(a, sc); Storyboard.SetTargetProperty(a, prop);
                    rings.Children.Add(a);
                }
                var o = new DoubleAnimation { From = 0.5, To = 0, Duration = dur, BeginTime = begin, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                Storyboard.SetTarget(o, ring); Storyboard.SetTargetProperty(o, "Opacity");
                rings.Children.Add(o);
            }
            Button = new Button { Width = size, Height = size, Content = new FontIcon { Glyph = Glyph.Play, FontFamily = Theme.Icons, FontSize = 30 } };
            Make(Button, BtnKind.Knob);
            Button.Click += (s, e) => click();
            Root.Children.Add(Button);
            Root.Unloaded += (s, e) => rings.Stop();
        }

        public void SetPlaying(bool on)
        {
            if (on == playing) return;
            playing = on;
            if (on) rings.Begin();
            else
            {
                rings.Stop();
                foreach (var r in Root.Children.OfType<Ellipse>().Skip(1)) r.Opacity = 0;
            }
        }
    }
}
