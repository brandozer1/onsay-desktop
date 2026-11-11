// Updates.cs - keeping Onsay up to date, and removing it, on Windows 7 and later. The installer (release.ps1, with Inno
// Setup) puts him in %LocalAppData%\Onsay: the launcher Onsay.exe, which never changes, version.txt naming the version
// that runs, a folder for each version, app-<version>, and the uninstaller. Half a minute after he starts, and every hour
// after, he asks the update lingo, built into the floor (Core.cs), for a newer version, which it puts together beside
// this one, each file checked against the release Onsay signed. It goes in by itself the next time he isn't busy, his card put away and no to-do working: the update
// lingo names it in version.txt and he starts again through the launcher, on the newer version. Quit before then, and it
// goes in as he next starts. A build that was never installed (start.ps1's) does nothing here.
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
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Win32;

namespace Onsay.Desktop;

static class Updates
{
    public enum Place { Desktop, StartMenu }

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
            var (ready, _) = await Task.Run(() => Core.Call("/update/check", new JsonArray(Root)));
            if (ready?["version"] is JsonNode newer && $"{newer}" != Version)
            {
                while (!await idle()) await Task.Delay(TimeSpan.FromMinutes(1));
                var (went, _) = await Task.Run(() => Core.Call("/update/apply", new JsonArray(Root, $"{newer}")));
                if (went is not null)
                {
                    Process.Start(new ProcessStartInfo(Launcher, "--restarted") { UseShellExecute = true });
                    Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
                    return;
                }
            }
            await Task.Delay(TimeSpan.FromHours(1));
        }
    }

    ////////// 2-3. THIS COPY //////////

    // 2. Installed when the launcher and version.txt sit in the folder above this version's.
    public static bool Installed => File.Exists(Launcher) && File.Exists(Path.Combine(Root, "version.txt"));

    public static string Version => typeof(Updates).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

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

    // The browser lingo's own folder, where it keeps LibreWolf and its profile, unless its folder setting moved it.
    static readonly string BrowserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "lingo-browser");

    /* 4. However it is removed, from his settings or Programs and Features: the uninstaller starts him with --uninstalling
          first. Everything running from Onsay is stopped, the lingo client and its browser included, so no file in its
          folder is held open and the folder goes whole. Everything he keeps, in Settings.Folder and the browser's folder,
          stays unless the person says to delete it, since most who uninstall mean to install again. */
    public static void Uninstalling()
    {
        Startup.Set(false);
        var mine = new[] { Root, BrowserFolder };
        var me = Process.GetCurrentProcess().Id;
        foreach (var p in Process.GetProcesses())
        {
            if (p.Id == me) continue;
            try
            {
                var file = p.MainModule?.FileName ?? "";
                if (!mine.Any(folder => file.StartsWith(folder, StringComparison.OrdinalIgnoreCase))) continue;
                p.Kill();
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
