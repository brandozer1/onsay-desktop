// Updates.cs - keeping Onsay up to date, and removing it. The installer (release.ps1, with Inno Setup) puts him in
// %LocalAppData%\Onsay: the launcher Onsay.exe, which never changes, version.txt naming the version that runs, a folder for
// each version, app-<version>, and the uninstaller. Half a minute after he starts, and every hour after, he asks Onsay for
// the newest release (onsay server routes/updates.js), checks Onsay signed it, and puts a newer one together beside this
// one: each file copied from this version when it is the same, else downloaded, and every one checked against its sha256
// in the signed release. It goes in by itself the next time he isn't busy: version.txt names it and he starts again
// through the launcher. A build that was never installed (start.ps1's) does nothing here.
//
// 1. Start(idle)              the checks, from startup. idle says whether nothing is happening that a restart would cut
//                             short
//    Now()                    the person's Update now: a newer version checked for, put in and started at once, or what
//                             came of it in words
// 2. Installed                whether this copy was installed, so it updates and can be removed
// 3. Shortcut(where, on)      his desktop or Start menu shortcut: whether it is there, and making or removing it
// 4. Uninstall()              removes Onsay, its shortcuts and its startup, through the uninstaller Windows registered
//    Uninstalling()           as it is removed: everything running from it stopped, so its folder goes whole, no more
//                             starting with Windows, and what it keeps goes too if the person says so
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Win32;

namespace Onsay.Desktop;

static class Updates
{
    public enum Place { Desktop, StartMenu }

    const string Api = "https://api.onsay.dev/updates";
    // Onsay's public key, which every release it hands out is signed with (onsay server lib/signing.js).
    static readonly byte[] OnsayKey = Convert.FromBase64String("4DnM32U1FzXfVh3ahkdILlt7yNwfM84Ofj7Aohoe1QE=");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };
    static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Where he was installed: the folder above this version's, with the launcher and version.txt in it.
    static readonly string Root = Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd('\\')) ?? "";
    static string Launcher => Path.Combine(Root, "Onsay.exe");

    ////////// 1. A NEWER VERSION //////////

    // 1. A check that fails, offline or with nothing published yet, is tried again the next hour. Once a newer version is
    //    ready, whether he may go in is asked every minute. The newer one is started with --restarted, so it waits for
    //    this one to end instead of handing over to it (Program.cs).
    public static async void Start(Func<Task<bool>> idle)
    {
        if (!Installed) return;
        await Task.Delay(TimeSpan.FromSeconds(30));
        while (true)
        {
            var newer = await Task.Run(Ready);
            if (newer is not null)
            {
                while (!await idle()) await Task.Delay(TimeSpan.FromMinutes(1));
                if (Restarted(newer)) return;
            }
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    // 1. Nothing to say when it goes in, since he starts again on the newer version.
    public static async Task<string> Now()
    {
        if (!Installed) return "This copy was built here, so it doesn't update itself.";
        var newer = await Task.Run(Ready);
        if (newer is null) return $"You have the newest version, {Core.Version}.";
        return Restarted(newer) ? "" : "The update couldn't go in. Try again in a moment.";
    }

    /* The newest release, when it is newer than this one, put together in its own folder and every file checked: its
       version, or null when there is none, or it couldn't be checked or put together. */
    static string? Ready()
    {
        try
        {
            var got = JsonNode.Parse(Http.GetStringAsync($"{Api}/release").GetAwaiter().GetResult());
            var signed = $"{got?["signed"]}";
            if (got?["release"] is not JsonObject release || !signed.StartsWith("onsay:")) return null;
            if (!Ed25519.Check(OnsayKey, Encoding.UTF8.GetBytes(Canonical(release)), Convert.FromBase64String(signed.Substring(6)))) return null;
            var newer = $"{release["version"]}";
            if (!Version.TryParse(newer, out var n) || !Version.TryParse(Core.Version, out var mine) || n <= mine) return null;

            var arch = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var folder = Path.Combine(Root, $"app-{newer}");
            foreach (var file in (release["files"] as JsonArray ?? new()).OfType<JsonObject>())
            {
                if (file["arch"] is JsonNode only && $"{only}" != arch) continue;
                var path = $"{file["path"]}".Replace('/', '\\');
                if (path.Contains("..")) return null;
                var target = Path.Combine(folder, path);
                var sha = $"{file["sha256"]}";
                if (File.Exists(target) && Sha(File.ReadAllBytes(target)) == sha) continue;
                var here = Path.Combine(AppContext.BaseDirectory, path);
                var bytes = File.Exists(here) && Sha(File.ReadAllBytes(here)) == sha
                    ? File.ReadAllBytes(here)
                    : Http.GetByteArrayAsync($"{Api}/{Uri.EscapeDataString($"{file["name"]}")}").GetAwaiter().GetResult();
                if (Sha(bytes) != sha) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllBytes(target, bytes);
            }
            return newer;
        }
        catch (Exception) { return null; }
    }

    // The newer version named in version.txt and started, this one ending, or false when it couldn't go in.
    static bool Restarted(string newer)
    {
        try
        {
            File.WriteAllText(Path.Combine(Root, "version.txt"), newer);
            Process.Start(new ProcessStartInfo(Launcher, "--restarted") { UseShellExecute = true });
        }
        catch (Exception) { return false; }
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        return true;
    }

    static string Sha(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    // JSON as Onsay signs it: keys sorted, no spaces (onsay server lib/signing.js canonical).
    static string Canonical(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => $"{JsonSerializer.Serialize(p.Key, Plain)}:{Canonical(p.Value)}")) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(Plain),
    };

    ////////// 2-3. THIS COPY //////////

    // 2. Installed when the launcher and version.txt sit in the folder above this version's.
    public static bool Installed => File.Exists(Launcher) && File.Exists(Path.Combine(Root, "version.txt"));

    // 3. A shortcut to the launcher, so it always opens the version that runs. Only for an installed copy.
    static string ShortcutPath(Place where) => Path.Combine(Environment.GetFolderPath(where == Place.Desktop
        ? Environment.SpecialFolder.DesktopDirectory : Environment.SpecialFolder.Programs), "Onsay.lnk");

    public static bool Shortcut(Place where) => File.Exists(ShortcutPath(where));

    public static void Shortcut(Place where, bool on)
    {
        try
        {
            if (!on)
            {
                File.Delete(ShortcutPath(where));
                return;
            }
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            dynamic link = shell.CreateShortcut(ShortcutPath(where));
            link.TargetPath = Launcher;
            link.WorkingDirectory = Root;
            link.IconLocation = Launcher;
            link.Save();
        }
        catch { }
    }

    ////////// 4. REMOVING IT //////////

    // 4. Through the uninstaller the installer registered with Windows (the one Programs and Features uses), so it removes
    //    everything, startup included. If Windows has none for it, opens Programs and Features instead.
    public static void Uninstall()
    {
        string? cmd;
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Onsay_is1"))
            cmd = k?.GetValue("UninstallString") as string;
        try
        {
            if (cmd is null) Process.Start(new ProcessStartInfo("control.exe", "appwiz.cpl") { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo(cmd.Trim('"')) { UseShellExecute = true });
        }
        catch { }
    }

    /* 4. However it is removed, from his settings or Programs and Features: the uninstaller starts him with --uninstalling
          first. Everything running from Onsay is stopped, the port included, so no file in its folder is
          held open and the folder goes whole. Everything he keeps, in Settings.Folder, stays unless the person says to
          delete it, since most who uninstall mean to install again. */
    public static void Uninstalling()
    {
        Startup.Set(false);
        var me = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcesses())
        {
            if (p.Id == me) continue;
            try
            {
                if (!(p.MainModule?.FileName ?? "").StartsWith(Root, StringComparison.OrdinalIgnoreCase)) continue;
                p.Kill();
                p.WaitForExit(3000);
            }
            catch { }   // another person's, or gone already
        }
        var answer = MessageBox.Show(
            "Delete your Onsay data too? Your settings, sign in and chats on this computer will be gone.\n\nChoose No to keep them if you'll install Onsay again.",
            "Uninstall Onsay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try { Directory.Delete(Settings.Folder, recursive: true); } catch { }
    }
}
