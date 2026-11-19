using Echo.Core;
using Echo.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Echo;

public partial class App : Application
{
    Shell? window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            Logger.Write("unhandled: " + e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (s, e) => { Logger.Write("unobserved: " + e.Exception); e.SetObserved(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Paths.Init();
        Settings.Load();
        Recs.Load();
        Downloads.Load();
        Api.Init();
        var dq = DispatcherQueue.GetForCurrentThread();
        Bus.Dq = dq;
        Player.Init(dq);
        Account.Init();

        window = new Shell();
        window.Activate();
        _ = Boot();
    }

    async Task Boot()
    {
        await Account.RefreshMe();
        if (window == null) return;
        // a signed-in listener without taste goes to the taste setup first
        if (Account.Me != null && !Recs.HasTaste() && window.View == "home") window.Go("welcome");
        else if (window.View == "home") window.Go("home");
    }
}
