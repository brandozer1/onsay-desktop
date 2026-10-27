// Core.cs - runs onsay-core.exe, his hands on this computer, while signed in, and provides the lingos this app ships.
//
// 1. Start(settings)    writes core.json and runs the core, restarting it if it ever stops. It runs with --sent, so every
//                       message it sends to Onsay comes on its output too, for the request log (Requests.cs)
// 6. Routes(computer)   the lingos installed on this computer, each a route at its address under the handle
// 2. Stop()             ends it, and keeps it ended
// 3. Restart()          ends whatever it is running at once, then starts it again: the stop key's guarantee
// 4. Permissions        every Lingo permission, in words, and Allowed(settings), the ones ticked
// 5. Provide()          the lingos this app ships, like windows and agent, put in the lingos folder
// 7. SettingsOf(lingo), Set(lingo, key, text)   a lingo's settings as its manifest declares them, read and changed
//                       through the core, wherever the lingo keeps them
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Onsay.Desktop;

static class Core
{
    // Each .lingo file in Lingos is a lingo the core loads, and so is a folder with a lingo.json, a native one like windows,
    // see ../lingo-client/core/README.md.
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

    /* 1. Signed in, the lingo client takes web requests from Onsay's proxy too (inbound), so this computer is reachable
          at its proxy address with nothing for the person to set up. Its own port answers this computer only. */
    public static void Start(Settings s)
    {
        settings = s;
        if (s.Token is null || !File.Exists(Exe)) return;
        Directory.CreateDirectory(Home);
        File.WriteAllText(Config, JsonSerializer.Serialize(new { token = s.Token, name = Environment.MachineName, home = Home, allow = Allowed(s), inbound = "onsay" }));
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
            StartInfo = new ProcessStartInfo(Exe, $"\"{Config}\" --sent")
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
        // A lingo this app now ships as a folder replaces the one file of it an older version put there, like agent.lingo.
        foreach (var dir in Directory.EnumerateDirectories(shipped))
            try { File.Delete(Path.Combine(Lingos, Path.GetFileName(dir) + ".lingo")); } catch (IOException) { }
    }

    ////////// 7. A LINGO'S SETTINGS //////////

    /* One setting of a lingo, as its manifest declares it, with its value now: Value and Default are JSON, or null when
       there is none. Kept wherever the lingo keeps them, on this computer, never on Onsay's servers. */
    public sealed record Field(string Name, string Title, string About, string? Default, bool Required, bool Secret, string? Value);

    // 7. Through the core, so the values are read and written wherever the lingo keeps them. Null with why, in words, when
    //    the core can't say: it needs this computer to be signed in.
    public static (List<Field>? fields, string? why) SettingsOf(string lingo)
    {
        var (ok, said) = Ask("--settings", lingo);
        if (!ok) return (null, said);
        try
        {
            using var doc = JsonDocument.Parse(said);
            static string? Json(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText() : null;
            return (doc.RootElement.EnumerateArray().Select(e => new Field(Json(e, "name") ?? "", Json(e, "title") ?? Json(e, "name") ?? "", Json(e, "about") ?? "",
                Json(e, "default"), e.TryGetProperty("required", out var r) && r.ValueKind == JsonValueKind.True, e.TryGetProperty("secret", out var s) && s.ValueKind == JsonValueKind.True, Json(e, "value"))).ToList(), null);
        }
        catch { return (null, "The core's answer couldn't be read"); }
    }

    // 7. Empty clears it, so its default applies again. Gives why it failed, in words, or null.
    public static string? Set(string lingo, string key, string text) =>
        Ask("--set", lingo, key, text.Length == 0 ? "null" : JsonSerializer.Serialize(text)) is (false, var why) ? why : null;

    // The core run once with its settings file and these, its output or its error.
    static (bool ok, string said) Ask(params string[] args)
    {
        if (!File.Exists(Exe) || !File.Exists(Config)) return (false, "Sign in to Onsay first, from his right click menu, to change a lingo's settings.");
        var info = new ProcessStartInfo(Exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        info.ArgumentList.Add(Config);
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(20_000)) { p.Kill(); return (false, "The core took too long to answer"); }
            // What it said last is why: the lines before it are the lingos it loaded.
            return p.ExitCode == 0 ? (true, output.Result) : (false, error.Trim().Split('\n').Last().Trim() is { Length: > 0 } last ? last : "The core couldn't do that");
        }
        catch (Exception e) { return (false, e.Message); }
    }

    ////////// 6. ROUTES //////////

    public sealed record Route(string Name, string Address, string About, bool Mine);

    // 6. Each .lingo file and each native lingo folder in the lingos folder is a route the core offers. computer is this
    //    computer's name on Onsay, like laptop.brandon, so the address reads the way a caller reaches it. Mine is a lingo
    //    the person added themselves, not one the app ships (windows, agent). The core is the one that decides what loads; this
    //    reads the folder, so a lingo it set aside still shows here.
    public static List<Route> Routes(string computer)
    {
        var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var from = Path.Combine(AppContext.BaseDirectory, "lingos");
        if (Directory.Exists(from))
            foreach (var d in Directory.EnumerateDirectories(from)) shipped.Add(Path.GetFileName(d));
        var routes = new List<Route>();
        if (!Directory.Exists(Lingos)) return routes;
        foreach (var file in Directory.EnumerateFiles(Lingos, "*.lingo"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            routes.Add(new Route(name, $"{computer}/lingos/{name}.lingo", FirstLine(file), true));
        }
        foreach (var dir in Directory.EnumerateDirectories(Lingos))
        {
            var manifest = Path.Combine(dir, "lingo.json");
            if (!File.Exists(manifest)) continue;
            var name = Path.GetFileName(dir);
            string about = "";
            try { using var doc = JsonDocument.Parse(File.ReadAllText(manifest)); about = doc.RootElement.TryGetProperty("about", out var a) ? a.GetString() ?? "" : ""; } catch { }
            routes.Add(new Route(name, $"{computer}/lingos/{name}", about, !shipped.Contains(name)));
        }
        return routes.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // A Lingo lingo's first line is "// name - what it is.", so the words after the first " - " are its description.
    static string FirstLine(string file)
    {
        try
        {
            var line = File.ReadLines(file).FirstOrDefault() ?? "";
            var at = line.IndexOf(" - ", StringComparison.Ordinal);
            return at < 0 ? "" : line[(at + 3)..].Trim();
        }
        catch { return ""; }
    }
}
