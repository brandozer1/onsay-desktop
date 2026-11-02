// Updates.cs - keeping Onsay up to date, and removing it. Built with Velopack (release.ps1 makes the installer): a
// minute after he starts, and every six hours after, he looks for a newer version, downloads it quietly, and it goes
// in when he next quits, so he never restarts on you. A build that was never installed (start.ps1's) does nothing here.
//
// The updater itself almost never changes: Velopack installs a tiny separate program that applies updates, and that is
// what updates everything else, so a new version of Onsay is a normal update, never a reinstall. Keep it that way:
// nothing the updater needs lives in the app's own update logic.
//
// 1. Start()                  the background checks, from startup
// 2. CheckNow()               looks now, on request, and says what it found, in plain words
// 3. Installed, Version       whether this copy was installed (so it updates and can be removed), and its version
// 4. Shortcut(where, on)      his desktop or Start menu shortcut: whether it is there, and making or removing it
// 5. Uninstall()              removes Onsay, its shortcuts and its startup, through the uninstaller Windows registered
//    Uninstalling()           as it is removed: no more starting with Windows, and what it keeps goes too if the person
//                             says so
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Windows;

namespace Onsay.Desktop;

static class Updates
{
    // Where the releases live: this repo's GitHub releases, which .github/workflows/release.yml makes. Velopack reads
    // them natively, so the repo must be public.
    const string Feed = "https://github.com/brandozer1/onsay-desktop";

    static UpdateManager Manager => new(new GithubSource(Feed, null, false));

    ////////// 1-2. LOOKING FOR A NEWER VERSION //////////

    // A newer version downloaded, to go in when he next quits: its version, or null when this one is the newest.
    static async Task<string?> Fetch()
    {
        var um = Manager;
        if (await um.CheckForUpdatesAsync() is not { } newer) return null;
        await um.DownloadUpdatesAsync(newer);
        um.WaitExitThenApplyUpdates(newer.TargetFullRelease, silent: true, restart: false);
        return newer.TargetFullRelease.Version.ToString();
    }

    // 1. A check that fails, offline or with nothing published yet, is tried again next time.
    public static async void Start()
    {
        if (!Installed) return;
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try { if (await Fetch() is not null) return; } catch (Exception) { }
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    // 2. Only for an installed copy. Never throws: the message says what happened.
    public static async Task<string> CheckNow()
    {
        try
        {
            var version = await Fetch();
            if (version is null) return "Onsay is up to date.";
            return $"Version {version} is ready, and goes in next time you quit Onsay.";
        }
        catch (Exception e)
        {
            return $"Couldn't check just now: {e.Message}";
        }
    }

    ////////// 3-4. THIS COPY //////////

    // 3.
    public static bool Installed
    {
        get
        {
            try { return Manager.IsInstalled; }
            catch { return false; }
        }
    }

    public static string Version
    {
        get
        {
            try { return Manager.CurrentVersion?.ToString() ?? ""; }
            catch { return ""; }
        }
    }

    // 4. Velopack's own shortcuts, so they point at the copy that updates and go when he is uninstalled. Only for an
    //    installed copy.
    public static bool Shortcut(ShortcutLocation where)
    {
        try
        {
            var links = new Shortcuts(VelopackLocator.Current);
            return links.FindShortcuts(VelopackLocator.Current.ThisExeRelativePath, where).Count > 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Shortcut(ShortcutLocation where, bool on)
    {
        try
        {
            var links = new Shortcuts(VelopackLocator.Current);
            if (on) links.CreateShortcutForThisExe(where);
            else links.RemoveShortcutForThisExe(where);
        }
        catch { }
    }

    ////////// 5. REMOVING IT //////////

    // 5. Through the uninstaller Velopack registered with Windows (the same one Windows' Installed apps uses), so it
    //    removes everything, startup included. If Windows has none for it, opens Windows' Installed apps instead.
    public static void Uninstall()
    {
        string? cmd = null;
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Onsay"))
            cmd = k?.GetValue("QuietUninstallString") as string ?? k?.GetValue("UninstallString") as string;
        if (cmd is null)
        {
            try { Native.Open("ms-settings:appsfeatures"); } catch { }
            return;
        }
        // cmd is like "C:\...\Update.exe --uninstall": the exe, quoted, then its arguments.
        var exe = cmd.StartsWith('"') ? cmd[1..cmd.IndexOf('"', 1)] : cmd.Split(' ')[0];
        var args = cmd[(cmd.IndexOf(exe) + exe.Length)..].TrimStart('"', ' ');
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); } catch { }
    }

    /* 5. However it is removed, from his settings or Windows' Installed apps. Everything he keeps, in Settings.Folder, stays
          unless the person says to delete it, since most who uninstall mean to install again. It is deleted once Onsay and
          the lingo client are closed, so nothing holds a file open. Velopack gives this half a minute: no answer by then
          keeps it all. */
    public static void Uninstalling()
    {
        Startup.Set(false);
        var answer = MessageBox.Show(
            "Delete your Onsay data too? Your settings, sign in, AI keys, apps and chats on this computer will be gone.\n\nChoose No to keep them if you'll install Onsay again.",
            "Uninstall Onsay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        foreach (var p in Process.GetProcessesByName("onsay-core").Concat(Process.GetProcessesByName("Onsay")))
        {
            if (p.Id == Environment.ProcessId) continue;
            try
            {
                p.Kill();
                p.WaitForExit(3000);
            }
            catch { }
        }
        try { Directory.Delete(Settings.Folder, recursive: true); } catch { }
    }
}
