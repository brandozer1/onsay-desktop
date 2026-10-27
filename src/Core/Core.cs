// Core.cs - runs the lingo client, onsay-core.exe: a small program that answers web requests on this computer, each
// lingo at its own route, and is his hands here. It runs whether or not the person is signed in.
//
// 1. Start(settings)    writes core.json and starts the lingo client with it, ending the one running first: on startup,
//                       a sign in or out, a change of permissions, and the stop key. It runs with --sent, so every
//                       message it sends to Onsay comes on its output too, for the request log (Requests.cs)
// 2. Stop()             ends it, and keeps it ended
// 3. Permissions        every Lingo permission, in words, and Allowed(settings), the ones ticked
// 4. Provide()          the lingos this app ships, like windows and agent, put in the lingos folder
// 5. Lingos()           every lingo on this computer: its route, what it does, and whether it is open
// 6. SettingsOf(lingo), Set(lingo, key, text)   a lingo's settings as its manifest declares them, read and changed
//                       through the lingo client, wherever the lingo keeps them
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Onsay.Desktop;

static class Core
{
    // Each .lingo file in the lingos folder is a lingo, and so is a folder with a lingo.json, a native one like windows,
    // see ../lingo-client/core/README.md.
    static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "onsay-core.exe");
    static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "lingos");
    static readonly string Home = Path.Combine(Settings.Folder, "core");
    public static readonly string LingosFolder = Path.Combine(Home, "lingos");
    static readonly string Config = Path.Combine(Settings.Folder, "core.json");
    static readonly string Log = Path.Combine(Settings.Folder, "core.log");
    public const int Port = 6969;   // the lingo client's own default, answered for this computer only
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

    // 3. Seeing and acting on the screen are on by default: without them he can't help with anything on it.
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
    static bool wanted;

    ////////// 1-2. RUNNING IT //////////

    /* 1. Signed in, it also takes web requests from Onsay's proxy (inbound), so an open lingo is reachable at its proxy
          address with nothing for the person to set up. Signed out it runs bare, for this computer alone. From any
          thread: the stop key calls it from its own. */
    public static void Start(Settings s)
    {
        Stop();
        Directory.CreateDirectory(LingosFolder);
        if (!File.Exists(Exe)) return;
        var config = new Dictionary<string, object> { ["name"] = Environment.MachineName, ["home"] = Home, ["allow"] = Allowed(s) };
        if (s.Token is not null) (config["token"], config["inbound"]) = (s.Token, "onsay");
        File.WriteAllText(Config, JsonSerializer.Serialize(config));
        wanted = true;
        Run();
    }

    // Its errors go to core.log, and what it asks to be drawn comes on its output, see Overlay.cs. It comes back if it
    // ever stops on its own.
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

    // 2. Waits for it to end, so its port and its lingos' libraries are let go before anything takes them.
    public static void Stop()
    {
        wanted = false;
        try { process?.Kill(); process?.WaitForExit(3000); } catch { }
        process = null;
        Overlay.Rest();
    }

    ////////// 4. PROVIDE //////////

    // 4. Copied before it starts, while nothing has their libraries open, and only the files that differ.
    static void Provide()
    {
        if (!Directory.Exists(Shipped)) return;
        foreach (var from in Directory.EnumerateFiles(Shipped, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(LingosFolder, Path.GetRelativePath(Shipped, from));
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
        foreach (var dir in Directory.EnumerateDirectories(Shipped))
            try { File.Delete(Path.Combine(LingosFolder, Path.GetFileName(dir) + ".lingo")); } catch (IOException) { }
    }

    ////////// 5. THE LINGOS //////////

    // One lingo, installed at Route on this computer. Open is reachable from outside it too. Shipped comes with Onsay.
    public sealed record Lingo(string Name, string Route, string About, bool Open, bool Shipped);

    /* 5. As the lingo client says: GET / from this computer lists every lingo it runs, with its route. While it isn't
          answering, the lingos folder is read instead, each at the route its manifest names or /<name>. What a lingo
          does is its manifest's about, or the top lines of its .lingo file, "// name - what it is". */
    public static List<Lingo> Lingos()
    {
        var found = new List<(string name, string? route, bool? open)>();
        try
        {
            using var res = Http.Send(new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{Port}/"));
            using var doc = JsonDocument.Parse(res.Content.ReadAsStream());
            foreach (var e in doc.RootElement.GetProperty("lingos").EnumerateArray())
                found.Add((e.GetProperty("name").GetString()!, e.GetProperty("route").GetString(),
                    e.TryGetProperty("open", out var o) ? o.ValueKind == JsonValueKind.True : null));
        }
        catch (Exception)
        {
            found.Clear();
            if (Directory.Exists(LingosFolder))
                foreach (var path in Directory.EnumerateFileSystemEntries(LingosFolder))
                    if (path.EndsWith(".lingo", StringComparison.OrdinalIgnoreCase)) found.Add((Path.GetFileNameWithoutExtension(path), null, null));
                    else if (File.Exists(Path.Combine(path, "lingo.json"))) found.Add((Path.GetFileName(path), null, null));
        }
        return found.Select(f =>
        {
            var folder = Path.Combine(LingosFolder, f.name);
            string about = "", route = "";
            bool open = false;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "lingo.json")));
                var m = doc.RootElement;
                about = m.TryGetProperty("about", out var a) ? a.GetString() ?? "" : "";
                route = m.TryGetProperty("route", out var r) ? r.GetString() ?? "" : "";
                open = m.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
            }
            catch (Exception) { }   // a bare .lingo file has no manifest
            if (about.Length == 0)
                try
                {
                    var code = File.Exists(folder + ".lingo") ? folder + ".lingo" : Directory.EnumerateFiles(folder, "*.lingo").FirstOrDefault();
                    var top = code is null ? "" : string.Join(" ", File.ReadLines(code).TakeWhile(l => l.StartsWith("//") && l.Trim().Length > 2).Select(l => l[2..].Trim()));
                    about = top.IndexOf(" - ", StringComparison.Ordinal) is var at and >= 0 ? top[(at + 3)..] : top;
                }
                catch (Exception) { }
            return new Lingo(f.name, f.route ?? (route.StartsWith('/') && route.Length > 1 ? route.TrimEnd('/') : "/" + f.name), about,
                f.open ?? open, Directory.Exists(Path.Combine(Shipped, f.name)));
        }).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    ////////// 6. A LINGO'S SETTINGS //////////

    /* One setting of a lingo, as its manifest declares it, with its value now: Value and Default are JSON, or null when
       there is none. Kept wherever the lingo keeps them, on this computer, never on Onsay's servers. */
    public sealed record Field(string Name, string Title, string About, string? Default, bool Required, bool Secret, string? Value);

    // 6. Through the lingo client, so the values are read and written wherever the lingo keeps them. Null with why, in
    //    words, when it can't say.
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
        catch { return (null, "The lingo client's answer couldn't be read."); }
    }

    // 6. Empty clears it, so its default applies again. Gives why it failed, in words, or null.
    public static string? Set(string lingo, string key, string text) =>
        Ask("--set", lingo, key, text.Length == 0 ? "null" : JsonSerializer.Serialize(text)) is (false, var why) ? why : null;

    // The lingo client run once with its settings file and these, its output or its error.
    static (bool ok, string said) Ask(params string[] args)
    {
        if (!File.Exists(Exe) || !File.Exists(Config)) return (false, "This copy of Onsay has no lingo client, so lingo settings can't be changed here.");
        var info = new ProcessStartInfo(Exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
        info.ArgumentList.Add(Config);
        foreach (var a in args) info.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(20_000)) { p.Kill(); return (false, "The lingo client took too long to answer."); }
            // What it said last is why: the lines before it are the lingos it loaded.
            return p.ExitCode == 0 ? (true, output.Result) : (false, error.Trim().Split('\n').Last().Trim() is { Length: > 0 } last ? last : "The lingo client couldn't do that.");
        }
        catch (Exception e) { return (false, e.Message); }
    }
}
