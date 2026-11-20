using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Echo.Core;

// Self-update from music.maxsim.space: a newer build is downloaded and unpacked in the background, then copied over
// this folder when the app closes (or right away with «Перезапустить»). The archive's SHA-256 is checked first.
public static class Updater
{
    public static int Current => Assembly.GetExecutingAssembly().GetName().Version?.Build ?? 0;
    public static int ReadyBuild { get; private set; }
    static string? readyDir;
    static bool applied, busy;
    public static event Action<int>? Ready;

    static string Key => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "windows-arm" : "windows";
    static string Root => Path.Combine(Paths.Dir, "update");

    /// <returns>the newer build number, or 0 when this one is the latest</returns>
    public static async Task<int> Check()
    {
        if (ReadyBuild > 0) return ReadyBuild;
        if (busy || Current == 0) return 0; // local developer builds never update themselves
        busy = true;
        try
        {
            var all = await Api.Get<JsonElement>("/api/apps");
            if (!all.TryGetProperty(Key, out var info)) return 0;
            var tag = info.GetProperty("tag").GetString() ?? "";
            if (!int.TryParse(tag.Replace("apps-", ""), out var build) || build <= Current) return 0;
            var sha = info.TryGetProperty("sha256", out var s) ? s.GetString() ?? "" : "";

            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            Directory.CreateDirectory(Root);
            var zip = Path.Combine(Root, $"Echo-{build}.zip");
            using (var r = await Api.Http.GetAsync(Api.Base + "/dl/" + Key, HttpCompletionOption.ResponseHeadersRead))
            {
                r.EnsureSuccessStatusCode();
                await using var f = File.Create(zip);
                await r.Content.CopyToAsync(f);
            }
            string got;
            await using (var f = File.OpenRead(zip)) got = Convert.ToHexString(await SHA256.HashDataAsync(f)).ToLowerInvariant();
            if (sha.Length > 0 && got != sha) { Logger.Write("update: checksum mismatch"); return 0; }
            var dir = Path.Combine(Root, build.ToString());
            ZipFile.ExtractToDirectory(zip, dir);
            File.Delete(zip);
            if (!File.Exists(Path.Combine(dir, "Echo.exe"))) return 0;
            readyDir = dir;
            ReadyBuild = build;
            Bus.Ui(() => Ready?.Invoke(build));
            return build;
        }
        catch (Exception e)
        {
            Logger.Write("update check: " + e.Message);
            return 0;
        }
        finally { busy = false; }
    }

    /// Copies the new files over this folder after the app exits; with restart the new version starts again.
    public static void Apply(bool restart)
    {
        if (readyDir == null || applied) return;
        applied = true;
        var app = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var pid = Environment.ProcessId;
        var cmd = Path.Combine(Root, "apply.cmd");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine(":wait");
        sb.AppendLine($"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)");
        sb.AppendLine($"robocopy \"{readyDir}\" \"{app}\" /E /R:5 /W:1 /NFL /NDL /NJH /NJS >nul");
        if (restart) sb.AppendLine($"start \"\" \"{Path.Combine(app, "Echo.exe")}\"");
        sb.AppendLine($"rmdir /s /q \"{readyDir}\"");
        File.WriteAllText(cmd, sb.ToString(), new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }
}
