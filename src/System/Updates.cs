// Updates.cs - keeping Onsay up to date, and removing it. Built with Velopack (release.ps1 makes the installer): a
// minute after he starts, and every six hours after, he looks for a newer version, downloads it quietly, and it goes
// in when he next quits, so he never restarts on you. A build that was never installed (start.ps1's) does nothing here.
//
// The updater itself almost never changes: Velopack installs a tiny separate program that applies updates, and that is
// what updates everything else, so a new version of Onsay is a normal update, never a reinstall. Keep it that way:
// nothing the updater needs lives in the app's own update logic.
//
// 1. Start()        the background checks, from startup
// 2. CheckNow()     looks now, on request, and says what it found, in plain words
//    Shortcut(where, on)  his desktop or Start menu shortcut: whether it is there, and making or removing it
//    Installed      whether this copy was installed (so it updates and can be removed)
//    Version        the version running
// 3. Uninstall()    removes Onsay, its shortcuts and its startup, through the uninstaller Windows registered
using System.Diagnostics;
using Microsoft.Win32;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Windows;

namespace Onsay.Desktop;

static class Updates
{
    /* Where the releases live: this repo's GitHub releases, which .github/workflows/release.yml makes. Velopack reads
       them natively, so the repo must be public. */
    public const string Feed = "https://github.com/brandozer1/onsay-desktop";

    static UpdateManager Manager => new(new GithubSource(Feed, null, false));

    ////////// 1. IN THE BACKGROUND //////////

    // 1. A check that fails, offline or with nothing published yet, is tried again next time.
    public static async void Start()
    {
        if (!Installed) return;
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try
            {
                var um = Manager;
                if (await um.CheckForUpdatesAsync() is { } newer)
                {
                    await um.DownloadUpdatesAsync(newer);
                    um.WaitExitThenApplyUpdates(newer.TargetFullRelease, silent: true, restart: false);
                    return;
                }
            }
            catch (Exception) { }
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

    ////////// 2. ON REQUEST //////////

    public static bool Installed { get { try { return Manager.IsInstalled; } catch { return false; } } }

    // 2. Velopack's own shortcuts, so they point at the copy that updates and go when he is uninstalled. Only for an
    //    installed copy.
    public static bool Shortcut(ShortcutLocation where)
    {
        try { return Installed && new Shortcuts(VelopackLocator.Current).FindShortcuts(VelopackLocator.Current.ThisExeRelativePath, where).Count > 0; }
        catch { return false; }
    }

    public static void Shortcut(ShortcutLocation where, bool on)
    {
        if (!Installed) return;
        try
        {
            var links = new Shortcuts(VelopackLocator.Current);
            if (on) links.CreateShortcutForThisExe(where);
            else links.RemoveShortcutForThisExe(where);
        }
        catch { }
    }
    public static string Version { get { try { return Manager.CurrentVersion?.ToString() ?? ""; } catch { return ""; } } }

    // 2. Downloaded if there is one, to go in when he next quits. Never throws: the message says what happened.
    public static async Task<string> CheckNow()
    {
        var um = Manager;
        if (!um.IsInstalled) return "This copy was built on this computer, so it does not update itself.";
        try
        {
            if (await um.CheckForUpdatesAsync() is not { } newer) return "Onsay is up to date.";
            await um.DownloadUpdatesAsync(newer);
            um.WaitExitThenApplyUpdates(newer.TargetFullRelease, silent: true, restart: false);
            return $"Version {newer.TargetFullRelease.Version} is ready, and goes in next time you quit Onsay.";
        }
        catch (Exception e) { return $"Couldn't check just now: {e.Message}"; }
    }

    ////////// 3. REMOVING IT //////////

    // 3. Through the uninstaller Velopack registered with Windows (the same one Windows' Installed apps uses), so it
    //    removes everything, startup included. If this copy was not installed, opens Windows' Installed apps instead.
    public static void Uninstall()
    {
        string? cmd = null;
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Onsay"))
            cmd = k?.GetValue("QuietUninstallString") as string ?? k?.GetValue("UninstallString") as string;
        if (cmd is null) { try { Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true }); } catch { } return; }
        // cmd is like "C:\...\Update.exe --uninstall": the exe, quoted, then its arguments.
        var exe = cmd.StartsWith('"') ? cmd[1..cmd.IndexOf('"', 1)] : cmd.Split(' ')[0];
        var args = cmd[(cmd.IndexOf(exe) + exe.Length)..].TrimStart('"', ' ');
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true }); } catch { }
    }
}
