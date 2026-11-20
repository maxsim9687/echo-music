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
        Updater.Ready += build =>
        {
            var t = Echo.UI.Toasts.Show($"Обновление 1.0.{build} скачано. Установится при закрытии Эхо.", ms: 12000);
            t.Action("Перезапустить сейчас", () => { Updater.Apply(restart: true); window?.Close(); });
        };
        _ = Task.Run(async () => { while (true) { await Updater.Check(); await Task.Delay(TimeSpan.FromHours(6)); } });
        await Account.RefreshMe();
        if (window == null) return;
        // a signed-in listener without taste goes to the taste setup first
        if (Account.Me != null && !Recs.HasTaste() && window.View == "home") window.Go("welcome");
        else if (window.View == "home") window.Go("home");
    }
}
