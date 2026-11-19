using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;

namespace Echo.Core;

// Email login + profile sync with /api/profile (pull + merge on login/start, debounced push on change).
public static class Account
{
    public static User? Me { get; private set; }
    public static bool IsAdmin => Me?.Role == "admin";
    public static event Action? Changed;
    static DispatcherQueueTimer? pushTimer;
    static bool syncWarned;

    public static void Init()
    {
        Recs.Changed += PushProfile;
    }

    public static async Task RefreshMe()
    {
        try
        {
            var d = await Api.Get<MeResponse>("/api/me");
            Me = d.User; Catalog.Jamendo = d.Jamendo;
        }
        catch (ApiException e) when (e.Status is 401 or 403) { Me = null; }
        catch (Exception e) { Logger.Write("me: " + e.Message); /* offline: keep whatever we had */ }
        Changed?.Invoke();
        if (Me != null) await PullProfile();
    }

    static async Task PullProfile()
    {
        try
        {
            var d = await Api.Get<ProfileResponse>("/api/profile");
            if (d.Data != null) Recs.Merge(d.Data);
            PushProfile();
        }
        catch (Exception e) { Logger.Write("profile pull: " + e.Message); }
    }

    public static void PushProfile()
    {
        if (Me == null || Bus.Dq == null) return;
        pushTimer ??= Bus.Dq.CreateTimer();
        pushTimer.Stop();
        pushTimer.Interval = TimeSpan.FromMilliseconds(2500);
        pushTimer.IsRepeating = false;
        pushTimer.Tick -= OnPush;
        pushTimer.Tick += OnPush;
        pushTimer.Start();
    }

    static async void OnPush(DispatcherQueueTimer t, object o)
    {
        t.Stop();
        if (Me == null) return;
        try
        {
            await Api.Put("/api/profile", new { data = Recs.P });
            syncWarned = false;
        }
        catch (Exception e)
        {
            if (!syncWarned) { syncWarned = true; Bus.Toast($"Не удалось сохранить вкус в аккаунт: {e.Message}"); }
        }
    }

    public static Task RequestCode(string email) => Api.Post<object>("/api/auth/code", new { email });

    public static async Task<User?> Verify(string email, string code)
    {
        var d = await Api.Post<MeResponse>("/api/auth/verify", new { email, code });
        Api.SaveCookies();
        Settings.Current.LastEmail = email; Settings.Save();
        await RefreshMe();
        return d.User ?? Me;
    }

    public static async Task Logout()
    {
        try { await Api.Post<object>("/api/auth/logout"); } catch { }
        pushTimer?.Stop();
        Api.ClearCookies();
        Me = null;
        Recs.Clear();
        Wave.ResetPool();
        Changed?.Invoke();
    }
}

// Spotify chart rows matched against Audius (same rules as web matchChart).
public static class ChartMatch
{
    public enum Kind { Orig, Ver, None }
    public sealed record Result(List<Track> List, Kind Kind);
    static readonly Dictionary<string, Result> Cache = new();
    public static readonly Dictionary<Kind, string> Label = new() { [Kind.Orig] = "Оригинал", [Kind.Ver] = "Версия", [Kind.None] = "Нет на Audius" };

    static string Norm(string? x)
    {
        var s = (x ?? "").ToLowerInvariant();
        s = Regex.Replace(s, @"\(.*?\)|\[.*?\]", " ");
        s = Regex.Replace(s, @"feat\.?.*$", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"[^a-z0-9а-яёäöüéèáíóúñç ]", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public static Result? Cached(string country, ChartItem it) => Cache.TryGetValue($"{country}:{it.Pos}", out var r) ? r : null;

    public static async Task<Result> Match(string country, ChartItem it)
    {
        var key = $"{country}:{it.Pos}";
        if (Cache.TryGetValue(key, out var c)) return c;
        var list = new List<Track>();
        try { list = await Catalog.AuSearch($"{it.Artist} {it.Title}", 15); } catch { }
        var nt = Norm(it.Title); var na = Norm(it.Artist);
        var scored = list.Select(t =>
        {
            var tt = Norm(t.Title); var ta = Norm(t.Artist);
            var sc = 0;
            if (nt.Length > 0 && tt.Contains(nt)) sc += 4;
            if (ta == na) sc += 5; else if (na.Length > 0 && (tt.Contains(na) || ta.Contains(na))) sc += 2;
            return (t, sc, orig: ta == na && nt.Length > 0 && tt.Contains(nt));
        }).Where(x => x.sc >= 4).OrderByDescending(x => x.orig).ThenByDescending(x => x.sc).ToList();
        var res = new Result(scored.Select(x => x.t).ToList(), scored.Count == 0 ? Kind.None : scored[0].orig ? Kind.Orig : Kind.Ver);
        Cache[key] = res;
        return res;
    }
}
