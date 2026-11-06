// Updates.cs - keeping Onsay up to date, and removing it. Built with Velopack (release.ps1 makes the installer). Half a
// minute after he starts, and every hour after, he asks Onsay's server for a newer version (api.onsay.dev/updates, which
// hands out this repo's newest GitHub release) and downloads it quietly. It goes in by itself the next time he isn't
// busy, his card put away and no to-do working: he blinks out and back on the newer version, and nobody presses
// anything. Quit before then, and it goes in as he next starts. A build that was never installed (start.ps1's) does
// nothing here.
//
// The updater itself almost never changes: Velopack installs a tiny separate program that applies updates, and that is
// what updates everything else, so a new version of Onsay is a normal update, never a reinstall. Keep it that way:
// nothing the updater needs lives in the app's own update logic.
//
// 1. Start(idle)              the checks, from startup. idle says whether nothing is happening that a restart would cut
//                             short
// 2. Installed, Version       whether this copy was installed (so it updates and can be removed), and its version
// 3. Shortcut(where, on)      his desktop or Start menu shortcut: whether it is there, and making or removing it
// 4. Uninstall()              removes Onsay, its shortcuts and its startup, through the uninstaller Windows registered
//    Uninstalling()           as it is removed: everything running from it stopped, so its folder goes whole, no more
//                             starting with Windows, and what it keeps goes too if the person says so
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Velopack;
using Velopack.Locators;
using Velopack.Windows;

namespace Onsay.Desktop;

static class Updates
{
    // Where installed copies ask for a newer version: Onsay's own server, which hands out this repo's GitHub releases
    // (onsay's server/routes/updates.js), so where they live can change without changing a copy already installed.
    const string Feed = "https://api.onsay.dev/updates";

    static UpdateManager Manager => new(Feed);

    ////////// 1. A NEWER VERSION //////////

    // 1. A check that fails, offline or with nothing published yet, is tried again the next hour. Once one is downloaded,
    //    whether he may go in is asked every minute.
    public static async void Start(Func<Task<bool>> idle)
    {
        if (!Installed) return;
        await Task.Delay(TimeSpan.FromSeconds(30));
        while (true)
        {
            try
            {
                var um = Manager;
                if (await um.CheckForUpdatesAsync() is { } newer)
                {
                    await um.DownloadUpdatesAsync(newer);
                    while (!await idle()) await Task.Delay(TimeSpan.FromMinutes(1));
                    um.ApplyUpdatesAndRestart(newer.TargetFullRelease);
                    return;
                }
            }
            catch (Exception) { }
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    ////////// 2-3. THIS COPY //////////

    // 2.
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

    // 3. Velopack's own shortcuts, so they point at the copy that updates and go when he is uninstalled. Only for an
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

    ////////// 4. REMOVING IT //////////

    // 4. Through the uninstaller Velopack registered with Windows (the same one Windows' Installed apps uses), so it
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

    // The browser lingo's own folder, where it keeps LibreWolf and its profile, unless its folder setting moved it.
    static readonly string BrowserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lingo-browser");

    /* 4. However it is removed, from his settings or Windows' Installed apps. First everything running from Onsay is
          stopped, the lingo client and its browser included, so no file in its folder is held open and the folder goes
          whole. Everything he keeps, in Settings.Folder and the browser's folder, stays unless the person says to delete
          it, since most who uninstall mean to install again. Velopack gives this half a minute: no answer by then keeps it
          all. */
    public static void Uninstalling()
    {
        Startup.Set(false);
        var mine = new[] { AppContext.BaseDirectory, BrowserFolder };
        foreach (var p in Process.GetProcesses())
        {
            if (p.Id == Environment.ProcessId) continue;
            try
            {
                var file = p.MainModule?.FileName ?? "";
                if (!mine.Any(folder => file.StartsWith(folder, StringComparison.OrdinalIgnoreCase))) continue;
                p.Kill(entireProcessTree: true);
                p.WaitForExit(3000);
            }
            catch { }   // another person's, or gone already
        }
        var answer = MessageBox.Show(
            "Delete your Onsay data too? Your settings, sign in, AI keys, apps, chats and the browser he uses on this computer will be gone.\n\nChoose No to keep them if you'll install Onsay again.",
            "Uninstall Onsay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        foreach (var folder in new[] { Settings.Folder, BrowserFolder })
            try { Directory.Delete(folder, recursive: true); } catch { }
    }
}
