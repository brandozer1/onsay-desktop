// Core.cs - runs onsay-core.exe, his hands on this computer, while signed in, and provides the lingos this app ships.
//
// 1. Start(settings)    writes core.json and runs the core, restarting it if it ever stops
// 2. Stop()             ends it, and keeps it ended
// 3. Restart()          ends whatever it is running at once, then starts it again: the stop key's guarantee
// 4. Permissions        every Lingo permission, in words, and Allowed(settings), the ones ticked
// 5. Provide()          the lingos this app ships, like windows, put in the lingos folder
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Onsay.Desktop;

static class Core
{
    // Any folder in Lingos with a lingo.json is a lingo the core loads, see ../onsay/core/README.md.
    static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "onsay-core.exe");
    static readonly string Home = Path.Combine(Settings.Folder, "core");
    public static readonly string Lingos = Path.Combine(Home, "lingos");
    public static readonly string Store = Path.Combine(Home, "store");   // the store phrases' own folder, see core.c
    static readonly string Config = Path.Combine(Settings.Folder, "core.json");
    static readonly string Log = Path.Combine(Settings.Folder, "core.log");

    // 4. Seeing and acting on the screen are on by default: without them he can't help with anything on it.
    public static readonly (string Name, string Words, bool Default)[] Permissions =
    {
        ("screen.read", "See windows and what is in them", true),
        ("screen.act", "Click, type and open apps", true),
        ("files.read", "Read your files", true),
        ("files.write", "Change, move and delete files", false),
        ("run", "Run commands", false),
        ("net", "Reach the internet", false),
    };

    public static List<string> Allowed(Settings s) => s.Allow ?? Permissions.Where(p => p.Default).Select(p => p.Name).ToList();

    static Process? process;
    static Settings? settings;
    static bool wanted;

    ////////// 1-3. RUNNING IT //////////

    // 1.
    public static void Start(Settings s)
    {
        settings = s;
        if (s.Token is null || !File.Exists(Exe)) return;
        Directory.CreateDirectory(Home);
        File.WriteAllText(Config, JsonSerializer.Serialize(new { token = s.Token, name = Environment.MachineName, home = Home, allow = Allowed(s) }));
        wanted = true;
        Run();
    }

    // Its errors go to core.log, and what it asks to be drawn comes on its output, see Overlay.cs.
    static void Run()
    {
        if (!wanted || process is { HasExited: false }) return;
        Provide();
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(Exe, $"\"{Config}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            },
            EnableRaisingEvents = true,
        };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) try { File.AppendAllText(Log, $"{DateTime.Now:s} {e.Data}\n"); } catch { } };
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) Overlay.Line(e.Data); };
        p.Exited += async (_, _) => { await Task.Delay(2000); Run(); };
        p.Start();
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        process = p;
    }

    // 2. Waits for it to end, so its lingos' libraries are let go before anything replaces them.
    public static void Stop()
    {
        wanted = false;
        try { process?.Kill(); process?.WaitForExit(3000); } catch { }
        process = null;
        Overlay.Rest();
    }

    // 3.
    public static void Restart()
    {
        Stop();
        if (settings is not null) Start(settings);
    }

    ////////// 5. PROVIDE //////////

    // 5. Copied before the core starts, while nothing has their libraries open, and only the files that differ.
    static void Provide()
    {
        var shipped = Path.Combine(AppContext.BaseDirectory, "lingos");
        if (!Directory.Exists(shipped)) return;
        foreach (var from in Directory.EnumerateFiles(shipped, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(Lingos, Path.GetRelativePath(shipped, from));
            var a = new FileInfo(from);
            var b = new FileInfo(to);
            if (b.Exists && b.Length == a.Length && b.LastWriteTimeUtc == a.LastWriteTimeUtc) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                a.CopyTo(to, overwrite: true);
            }
            catch (IOException) { }   // still open somewhere: the one there stays until next time
        }
    }
}
