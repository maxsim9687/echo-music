using Echo.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Echo.Pages;

public abstract class PageBase : Grid
{
    protected readonly StackPanel Body = new() { Spacing = 44 };
    protected readonly ScrollViewer Scroll;
    protected bool Alive { get; private set; } = true;

    protected PageBase(double maxWidth = 1360)
    {
        var pad = new Border { Padding = new Thickness(32, 12, 32, 48), Child = Body, MaxWidth = maxWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        Scroll = new ScrollViewer { Content = pad, HorizontalScrollMode = ScrollMode.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(Scroll);
        Unloaded += (s, e) => Alive = false;
        Loaded += (s, e) => Alive = true;
    }

    protected static void Toast(string t) => Toasts.Show(t);
}
