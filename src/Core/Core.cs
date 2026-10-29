// Core.cs - runs the lingo client, onsay-core.exe: a small program that answers web requests on this computer, each
// lingo at its own route, and is his hands here. Its onsay lingo connects it to Onsay: signing in, and /onsay, where it
// works each message of the chat through with Onsay.
//
// 1. Start(settings)          writes core.json, puts the lingos this app ships in the lingos folder and starts the lingo
//                             client with them, ending the one running first: on startup and the stop key. The first
//                             time a shipped lingo appears it is started, with its Grants
// 2. Stop()                   ends it, and keeps it ended. Running says whether it runs
// 3. Needs                    what a lingo may do on this computer, in words: for Apps, Activity, Advanced and asking
//    Who(who, handle)         a caller or an access rule's who, in words, like "Your desktop" or "james"
// 4. Control(verb, values)    one of the lingo client's verbs, like start, set, ceiling or asks: through its control
//                             routes, or its command line when it isn't answering. Its answer, or why not, in words
//    Onsay(phrase, values)    one of the onsay lingo's phrases, like status, sign_in or devices, through call
// 5. Lingos()                 every lingo on this computer, an app on screen: whether it runs, what it is and needs, its
//                             parts, or why it can't run. Title(name) is its name on screen
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
    public static readonly string Home = Path.Combine(Settings.Folder, "core");
    public static readonly string LingosFolder = Path.Combine(Home, "lingos");
    public static readonly string Config = Path.Combine(Settings.Folder, "core.json");
    public static readonly string Log = Path.Combine(Settings.Folder, "core.log");
    // Where it answers on this computer: the port in core.json, its own default 6969 when it names none.
    static int port = 6969;
    public static string Url => $"http://127.0.0.1:{port}";
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };   // each request has its own, below

    // 3. Each need: what it is as a switch, what an app asking for it wants to do, and what one did, for Activity.
    public static readonly (string Name, string Words, string Wants, string Did)[] Needs =
    {
        ("screen.read", "See windows and what is in them", "see your screen", "saw your screen"),
        ("screen.act", "Click, type and open apps", "use your mouse and keyboard", "used your mouse and keyboard"),
        ("files.read", "Read your files", "read your files", "read your files"),
        ("files.write", "Change, move and delete files", "change your files", "changed your files"),
        ("run", "Run commands", "run a program", "ran a program"),
        ("net", "Reach the internet", "reach the internet", "reached the internet"),
    };

    /* 3. handle is this computer's own, so its other devices read as "Your desktop". A device signs as desktop.brandon,
          and me is the account itself, through Onsay. */
    public static string Who(string who, string? handle) => who switch
    {
        "this computer" => "You, on this computer",
        "me" => "Your Onsay account",
        "my devices" => "Your other devices",
        "public" => "Anyone",
        _ when who.StartsWith("ip ") => $"A computer at {who[3..]}",
        _ when who.Split('.') is [var device, var h] => h == handle ? $"Your {device}" : h,
        _ => who,
    };

    // What Onsay's own lingos may do without asking every time, given the first time each appears, and taken back in Apps.
    // Anything else they need is asked.
    static readonly Dictionary<string, string[]> Grants = new() { ["windows"] = new[] { "screen.read", "screen.act", "files.read" } };

    static Process? process;
    static bool wanted;
    static Settings settings = null!;

    ////////// 1-2. RUNNING IT //////////

    // 1. core.json holds its home, and keeps a port written there. From any thread: the stop key calls it from its own.
    public static void Start(Settings s)
    {
        Stop();
        Directory.CreateDirectory(LingosFolder);
        if (!File.Exists(Exe)) return;
        var config = new JsonObject { ["home"] = Home };
        try { if ((int?)JsonNode.Parse(File.ReadAllText(Config))?["port"] is int kept) config["port"] = port = kept; } catch (Exception) { }
        File.WriteAllText(Config, config.ToJsonString());
        settings = s;
        wanted = true;
        Run();
    }

    /* Its errors go to core.log, and what the windows lingo asks to be drawn comes on its output, see Overlay.cs. It comes
       back if it ever stops on its own. The lingos are copied before it starts, while nothing has their libraries open,
       and only those that differ. What an older version put there for one, its folder like windows\, goes, so it is never
       there twice. */
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
            StartInfo = new ProcessStartInfo(Exe, $"\"{Config}\"")
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
          is written as JSON there, so text stays text. The command line only when nothing answers the port: one that is
          slow to answer is not asked twice. asks and answer only mean something to the one running. */
    public static (JsonNode? answer, string? why) Control(string verb, JsonObject? values = null)
    {
        values ??= new();
        try
        {
            // A call, like signing in, may wait on Onsay. Anything else answers at once.
            using var res = Http.Send(new HttpRequestMessage(HttpMethod.Post, $"{Url}/.lingo/{verb}") { Content = new StringContent(values.ToJsonString(), Encoding.UTF8, "application/json") },
                new CancellationTokenSource(TimeSpan.FromSeconds(verb == "call" ? 40 : 3)).Token);
            var said = JsonNode.Parse(res.Content.ReadAsStream());
            return res.IsSuccessStatusCode ? (said, null) : (null, $"{said?["error"]}");
        }
        catch (Exception) when (verb is "asks" or "answer") { return (null, "Onsay's helper on this computer isn't running."); }
        catch (HttpRequestException) { }
        catch (Exception) { return (null, "Onsay's helper on this computer took too long to answer. Try again in a moment."); }
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

    // 4. Why not as a sentence, without the phrase's name in front.
    public static (JsonNode? answer, string? why) Onsay(string phrase, params JsonNode[] values)
    {
        var (answer, why) = Control("call", new() { ["fn"] = $"onsay.{phrase}", ["values"] = new JsonArray(values) });
        why = why?.Replace($"onsay.{phrase}: ", "").TrimEnd('.');
        return (answer, why is null ? null : why + ".");
    }

    ////////// 5. THE LINGOS //////////

    /* One lingo on this computer. Error is why it can't run, null when it can. Shipped comes with Onsay. Needs are what its
       routes say they need, and Parts the first step of each route after its own, like /window for /windows/window/list,
       each a part that can be shared on its own. */
    public sealed record Lingo(string Name, bool Running, string? Error, string About, bool Shipped, string[] Needs, string[] Parts);

    // 5.
    public static string Title(string name) => name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;

    /* 5. As the lingo client says: GET / from this computer lists every lingo, running or not. What one is comes from its
          openapi.json, in its .lingo file or its folder. Null while the lingo client isn't answering. */
    public static List<Lingo>? Lingos()
    {
        JsonArray all;
        try
        {
            using var res = Http.Send(new HttpRequestMessage(HttpMethod.Get, $"{Url}/"), new CancellationTokenSource(3000).Token);
            all = JsonNode.Parse(res.Content.ReadAsStream())!["lingos"]!.AsArray();
        }
        catch (Exception) { return null; }
        return all.Select(e =>
        {
            var name = $"{e?["name"]}";
            var file = Path.Combine(LingosFolder, name + ".lingo");
            string about = "";
            var (needs, parts) = (new SortedSet<string>(), new SortedSet<string>());
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
                var doc = JsonNode.Parse(manifest);
                about = $"{doc?["info"]?["description"]}";
                var route = $"{doc?["x-lingo"]?["route"] ?? "/" + name}";
                foreach (var (path, ops) in doc?["paths"]?.AsObject() ?? new JsonObject())
                {
                    if (path.StartsWith(route + "/") && path[(route.Length + 1)..].Split('/')[0] is { Length: > 0 } part) parts.Add("/" + part);
                    foreach (var (_, op) in ops?.AsObject() ?? new JsonObject())
                        foreach (var need in op?["x-lingo-needs"]?.AsArray() ?? new JsonArray()) needs.Add($"{need}");
                }
            }
            catch (Exception) { }   // one being written may have no openapi.json yet
            return new Lingo(name, (bool?)e?["running"] == true, e?["error"] is { } why ? $"{why}" : null, about, File.Exists(Path.Combine(Shipped, name + ".lingo")), needs.ToArray(), parts.ToArray());
        }).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
