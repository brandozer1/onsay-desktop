// Core.cs - runs the lingo client, onsay-core.exe: a small program that answers web requests on this computer, each
// lingo at its own route, and is his hands here. It runs whether or not the person is signed in.
//
// 1. Start(settings)          writes core.json, puts the lingos this app ships in the lingos folder and starts the lingo
//                             client with them, ending the one running first: on startup, a sign in or out, and the stop
//                             key. It runs with --sent, so every message it sends to Onsay comes on its output too, for
//                             the request log (Requests.cs). The first time a shipped lingo appears it is started, with
//                             its Grants
// 2. Stop()                   ends it, and keeps it ended. Running says whether it runs
// 3. Needs                    what a lingo may do on this computer, in words: for Settings, Permissions, and for asking
// 4. Control(verb, values)    one of the lingo client's verbs, like start, set, ceiling or asks: through its control
//                             routes, or its command line when it isn't answering. Its answer, or why not, in words
// 5. Lingos()                 every lingo on this computer: whether it runs, what it is, or why it can't run
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Core
{
    // Each .lingo file in the lingos folder is a lingo, see ../lingo-client/core/README.md.
    static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "onsay-core.exe");
    static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "lingos");
    static readonly string Home = Path.Combine(Settings.Folder, "core");
    public static readonly string LingosFolder = Path.Combine(Home, "lingos");
    static readonly string Config = Path.Combine(Settings.Folder, "core.json");
    static readonly string Log = Path.Combine(Settings.Folder, "core.log");
    const string Url = "http://127.0.0.1:6969";   // the lingo client's own default port, on this computer
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(3) };

    // 3. Each need, what it is in Settings, Permissions, and what a lingo asking for it wants to do.
    public static readonly (string Name, string Words, string Wants)[] Needs =
    {
        ("screen.read", "See windows and what is in them", "see your screen"),
        ("screen.act", "Click, type and open apps", "use your mouse and keyboard"),
        ("files.read", "Read your files", "read your files"),
        ("files.write", "Change, move and delete files", "change your files"),
        ("run", "Run commands", "run a program"),
        ("net", "Reach the internet", "reach the internet"),
    };

    // What Onsay's own lingos may do without asking every time, given the first time each appears: what Settings,
    // Permissions allowed every program before. Anything else they need is asked.
    static readonly Dictionary<string, string[]> Grants = new() { ["windows"] = new[] { "screen.read", "screen.act", "files.read" } };

    static Process? process;
    static bool wanted;
    static Settings settings = null!;

    ////////// 1-2. RUNNING IT //////////

    /* 1. Signed in, it keeps its own connection to Onsay with the same token, so Onsay can reach its lingos with nothing
          for the person to set up. Signed out it runs bare, for this computer alone. From any thread: the stop key calls it
          from its own. */
    public static void Start(Settings s)
    {
        Stop();
        Directory.CreateDirectory(LingosFolder);
        if (!File.Exists(Exe)) return;
        var config = new JsonObject();
        if (s.Token is not null) config["token"] = s.Token;
        (config["name"], config["home"]) = (Environment.MachineName, Home);
        File.WriteAllText(Config, config.ToJsonString());
        settings = s;
        wanted = true;
        Run();
    }

    /* Its errors go to core.log, and what it asks to be drawn comes on its output, see Overlay.cs. It comes back if it ever
       stops on its own. The lingos are copied before it starts, while nothing has their libraries open, and only those
       that differ. What an older version put there for one, its folder like windows\, goes, so it is never there twice. */
    static void Run()
    {
        if (!wanted || process is { HasExited: false }) return;
        var shipped = Directory.Exists(Shipped) ? Directory.GetFiles(Shipped, "*.lingo") : Array.Empty<string>();
        foreach (var from in shipped)
            try
            {
                var to = new FileInfo(Path.Combine(LingosFolder, Path.GetFileName(from)));
                var older = Path.Combine(LingosFolder, Path.GetFileNameWithoutExtension(from));
                if (Directory.Exists(older)) Directory.Delete(older, recursive: true);
                var a = new FileInfo(from);
                if (!to.Exists || to.Length != a.Length || to.LastWriteTimeUtc != a.LastWriteTimeUtc) a.CopyTo(to.FullName, overwrite: true);
            }
            catch (IOException) { }   // still open somewhere: the one there stays until next time
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(Exe, $"\"{Config}\" --sent")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
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

        // A lingo newly in the folder is stopped, so the ones this app ships are started the first time they appear, once
        // it answers. Started remembers them, so one the person stops stays stopped.
        var fresh = shipped.Select(f => Path.GetFileNameWithoutExtension(f)).Where(n => !(settings.Started ?? new()).Contains(n)).ToList();
        if (fresh.Count > 0) Task.Run(async () =>
        {
            for (var until = DateTime.Now.AddSeconds(15); Lingos() is null && DateTime.Now < until;) await Task.Delay(250);
            foreach (var name in fresh)
            {
                if (Control("start", new() { ["lingo"] = name }).why is not null) continue;
                foreach (var need in Grants.GetValueOrDefault(name, Array.Empty<string>())) Control("grant", new() { ["lingo"] = name, ["need"] = need });
                (settings.Started ??= new()).Add(name);
            }
            settings.Save();
        });
    }

    // 2. Waits for it to end, so its port and its lingos' libraries are let go before anything takes them.
    public static void Stop()
    {
        wanted = false;
        try { process?.Kill(); process?.WaitForExit(3000); } catch { }
        process = null;
        Overlay.Rest();
    }

    public static bool Running => process is { HasExited: false };

    ////////// 4. CONTROL //////////

    /* 4. values go by name to its control route, and in order to its command line, a list as its words. A setting's value
          is written as JSON there, so text stays text. asks and answer only mean something to the one running. */
    public static (JsonNode? answer, string? why) Control(string verb, JsonObject? values = null)
    {
        values ??= new();
        try
        {
            using var res = Http.Send(new HttpRequestMessage(HttpMethod.Post, $"{Url}/.lingo/{verb}") { Content = new StringContent(values.ToJsonString(), Encoding.UTF8, "application/json") });
            var said = JsonNode.Parse(res.Content.ReadAsStream());
            return res.IsSuccessStatusCode ? (said, null) : (null, $"{said?["error"]}");
        }
        catch (Exception) when (verb is "asks" or "answer") { return (null, "Onsay's helper on this computer isn't running."); }
        catch (Exception) { }
        if (!File.Exists(Exe) || !File.Exists(Config)) return (null, "This copy of Onsay has no lingo client, so this can't be changed here.");
        var info = new ProcessStartInfo(Exe) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        info.ArgumentList.Add(Config);
        info.ArgumentList.Add(verb);
        foreach (var (key, v) in values)
            if (v is JsonArray list) foreach (var x in list) info.ArgumentList.Add($"{x}");
            else info.ArgumentList.Add(key != "value" && v is JsonValue j && j.TryGetValue(out string? text) ? text : v?.ToJsonString() ?? "null");
        try
        {
            using var p = Process.Start(info)!;
            var output = p.StandardOutput.ReadToEndAsync();
            var error = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(20_000)) { p.Kill(); return (null, "The lingo client took too long to answer."); }
            // What it said last is why: the lines before it are the lingos it loaded.
            return p.ExitCode == 0 ? (JsonNode.Parse(output.Result), null) : (null, error.Trim().Split('\n').Last().Trim() is { Length: > 0 } last ? last : "The lingo client couldn't do that.");
        }
        catch (Exception e) { return (null, e.Message); }
    }

    ////////// 5. THE LINGOS //////////

    // One lingo on this computer. Error is why it can't run, null when it can. Shipped comes with Onsay.
    public sealed record Lingo(string Name, bool Running, string? Error, string About, bool Shipped);

    /* 5. As the lingo client says: GET / from this computer lists every lingo, running or not. What one is comes from its
          openapi.json, in its .lingo file or its folder. Null while the lingo client isn't answering. */
    public static List<Lingo>? Lingos()
    {
        JsonArray all;
        try
        {
            using var res = Http.Send(new HttpRequestMessage(HttpMethod.Get, $"{Url}/"));
            all = JsonNode.Parse(res.Content.ReadAsStream())!["lingos"]!.AsArray();
        }
        catch (Exception) { return null; }
        return all.Select(e =>
        {
            var name = $"{e?["name"]}";
            var file = Path.Combine(LingosFolder, name + ".lingo");
            string about = "";
            try
            {
                string manifest;
                if (File.Exists(file))
                {
                    using var zip = ZipFile.OpenRead(file);
                    using var read = new StreamReader(zip.GetEntry("openapi.json")!.Open());
                    manifest = read.ReadToEnd();
                }
                else manifest = File.ReadAllText(Path.Combine(LingosFolder, name, "openapi.json"));
                about = $"{JsonNode.Parse(manifest)?["info"]?["description"]}";
            }
            catch (Exception) { }   // one being written may have no openapi.json yet
            return new Lingo(name, (bool?)e?["running"] == true, e?["error"] is { } why ? $"{why}" : null, about, File.Exists(Path.Combine(Shipped, name + ".lingo")));
        }).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
