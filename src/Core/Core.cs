// Core.cs - runs the floor, floor.exe, the lingo client made for Windows, and is how this app, one of its lingos, calls it:
// each call signed with the app's own key and carrying a badge from the person, its owner, so it is checked like any call
// from anywhere (../onsay/docs/lingo-function.md, Lingos beside the floor). The lingos this app ships, the agent and ai
// among them, are offered to it with their code and kept there; the app's own, the chat and settings, with the address
// they answer at (Host.cs).
//
// 1. Start(settings)          writes floor.json, starts the floor and offers it the lingos: on startup and the stop key.
//                             It comes back by itself if it ever stops
//    Stop(), Running          ends it, and keeps it ended, and whether it runs
// 2. Call(path, values, run)  one function on the floor as the app, like /agent/ask with its values in order, run the
//                             to-do it is for, passed on with each call it makes: its value, or why not in words
//    Run(script, values, run) what the person typed, given to the floor's compiler as a script, with values by name, like
//                             the conversation, for the lingo search when it isn't Lingo, which finds what does it
// 3. SettingsFor(lingo)       each setting a lingo's code reads, with its value, kept in the floor's home
//    Setting(lingo, key), Set(lingo, key, value)   one of them, and keeping one
//    Policy(), Keep(policy)   the grants in policy.json, which the floor reads at every call
// 4. Lingos()                 every lingo offered on the floor, an app on screen. Title(name) is its name on screen
//    Switch(name, on)         an app turned on, offered again, or off, taken away
//    Needs, Wants(call), Who(who, handle)   what an app uses, what a refused call does, and who a caller is, in words
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Core
{
    static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "floor.exe");
    static readonly string Shipped = Path.Combine(AppContext.BaseDirectory, "lingos");   // the lingos this app ships, a .ls each
    public static readonly string Home = Path.Combine(Settings.Folder, "floor");
    public static readonly string Config = Path.Combine(Settings.Folder, "floor.json");
    public static readonly string Log = Path.Combine(Settings.Folder, "floor.log");
    static readonly string Keys = Path.Combine(Settings.Folder, "keys");
    // Where it answers on this computer: the port in floor.json, 6969 when it names none.
    static int port = 6969;
    public static string Url => $"http://127.0.0.1:{port}";
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };   // each call has its own, below
    // JSON as the floor writes it, so what is signed here checks out there: nothing escaped that needn't be, like +.
    static readonly JsonSerializerOptions Plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static Process? process;
    static bool wanted;
    static Settings settings = null!;

    ////////// 1. RUNNING IT //////////

    /* 1. floor.json: its home, its port, kept when one is written there, its owner, the person's key, and room for an
          agent's to-do, 10 minutes. From any thread: the stop key calls it from its own. */
    public static void Start(Settings s)
    {
        Stop();
        Host.Start(s);
        if (!File.Exists(Exe)) return;
        try
        {
            if ((int?)JsonNode.Parse(File.ReadAllText(Config))?["port"] is int kept) port = kept;
        }
        catch (Exception) { }
        var config = new JsonObject
        {
            ["home"] = Home,
            ["port"] = port,
            ["owner"] = KeyText(Seed("owner")),
            ["ms"] = 600_000,
            ["steps"] = 1_000_000_000,
        };
        Directory.CreateDirectory(Home);
        File.WriteAllText(Config, config.ToJsonString(Plain));
        settings = s;
        wanted = true;
        Run();
    }

    /* Its errors go to floor.log, and what the Windows lingos ask to be drawn comes on its output, see Overlay.cs. Once it
       answers, the lingos are offered: the app's own at Host's address, and each shipped one the person hasn't turned
       off, so a newer version's code replaces the old. One that won't compile is noted in floor.log. */
    static void Run()
    {
        if (!wanted || process is { HasExited: false }) return;
        // One left behind by an app that crashed before this one had its job would hold the port.
        foreach (var other in Process.GetProcessesByName("floor"))
            try
            {
                if (string.Equals(other.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase))
                {
                    other.Kill();
                    other.WaitForExit(3000);
                }
            }
            catch (Exception) { }
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(Exe, Compat.Arguments(new[] { Config }))
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8, WorkingDirectory = Home,
            },
            EnableRaisingEvents = true,
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            try { File.AppendAllText(Log, $"{DateTime.Now:s} {e.Data}\n"); } catch { }
        };
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null) Overlay.Line(e.Data);
        };
        p.Exited += async (_, _) =>
        {
            await Task.Delay(2000);
            Run();
        };
        p.Start();
        Native.EndsWithThis(p);
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        process = p;

        Task.Run(async () =>
        {
            var until = DateTime.Now.AddSeconds(15);
            while (Lingos() is null && DateTime.Now < until) await Task.Delay(250);
            foreach (var (name, code) in Host.Lingos) Call("/lingo/offer", new JsonArray(name, code, $"{Host.Url}/{name}"));
            foreach (var file in Directory.Exists(Shipped) ? Directory.GetFiles(Shipped, "*.ls") : Array.Empty<string>())
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if ((settings.Off ?? new()).Contains(name)) continue;
                var (_, why) = Call("/lingo/offer", new JsonArray(name, File.ReadAllText(file)));
                if (why is null) continue;
                try { File.AppendAllText(Log, $"{DateTime.Now:s} The {name} lingo wasn't offered: {why}\n"); } catch { }
            }
            Access.Watch();
        });
    }

    // 1. Waits for it to end, so its port is let go before anything takes it.
    public static void Stop()
    {
        wanted = false;
        try
        {
            process?.Kill();
            process?.WaitForExit(3000);
        }
        catch { }
        process = null;
        Overlay.Rest();
    }

    public static bool Running => process is { HasExited: false };

    ////////// 2. CALLS //////////

    // 2. values in order. A call may take as long as the floor lets it, 10 minutes, like a to-do's agent or an update.
    public static (JsonNode? answer, string? why) Call(string path, JsonArray? values = null, string? run = null) =>
        Send(path, (values ?? new JsonArray()).ToJsonString(Plain), run);

    // 2.
    public static (JsonNode? answer, string? why) Run(string script, JsonObject? values = null, string? run = null) =>
        Send("/", values is null ? script : new JsonObject { ["script"] = script, ["values"] = values }.ToJsonString(Plain), run);

    static (JsonNode? answer, string? why) Send(string path, string body, string? run)
    {
        try
        {
            using var request = Signed(path, body);
            if (run is not null) request.Headers.Add("Onsay-Run", run);
            using var res = Http.SendAsync(request, new CancellationTokenSource(TimeSpan.FromMinutes(11)).Token).GetAwaiter().GetResult();
            var said = JsonNode.Parse(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            if (res.IsSuccessStatusCode) return (said, null);
            // The search looked and found nothing: what it says about that is written for the person, so it is the answer.
            if ($"{said?["code"]}" == "nothing" && said?["error"] is JsonValue words) return (words.DeepClone(), null);
            return (null, $"{said?["error"] ?? "Onsay's helper on this computer couldn't do that."}");
        }
        catch (HttpRequestException) { return (null, "Onsay's helper on this computer isn't running. It starts again by itself, so try again in a moment."); }
        catch (Exception) { return (null, "Onsay's helper on this computer took too long to answer. Try again in a moment."); }
    }

    /* A POST to the floor signed by the app's key (RFC 9421): its method, its whole address and its body's digest, made
       now, with a nonce never used before, the key naming itself as keyid; and the badge the owner gave the app. */
    static HttpRequestMessage Signed(string path, string body)
    {
        var url = $"{Url}{path}";
        var bytes = Encoding.UTF8.GetBytes(body);
        using var sha = SHA256.Create();
        var digest = $"sha-256=:{Convert.ToBase64String(sha.ComputeHash(bytes))}:";
        var nonce = new byte[8];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(nonce);
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hex = BitConverter.ToString(nonce).Replace("-", "").ToLower();
        var signature = $"(\"@method\" \"@target-uri\" \"content-digest\");created={created};nonce=\"{hex}\";keyid=\"{KeyText(Seed("app"))}\"";
        var basis = $"\"@method\": POST\n\"@target-uri\": {url}\n\"content-digest\": {digest}\n\"@signature-params\": {signature}";
        var sig = Ed25519.Sign(Seed("app"), Encoding.UTF8.GetBytes(basis));
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(bytes) };
        request.Headers.Add("Content-Digest", digest);
        request.Headers.Add("Signature-Input", $"sig={signature}");
        request.Headers.Add("Signature", $"sig=:{Convert.ToBase64String(sig)}:");
        request.Headers.Add("Onsay-Badge", Badge());
        return request;
    }

    /* The app's two keys, each a private seed sealed for this Windows user (DPAPI) in keys\, made the first time: owner,
       the person's own, which only signs the app's badge, and app, the app's, which signs each call. */
    static readonly Dictionary<string, byte[]> seeds = new();
    static byte[] Seed(string name)
    {
        lock (seeds)
        {
            if (seeds.TryGetValue(name, out var known)) return known;
            var file = Path.Combine(Keys, name + ".key");
            byte[] seed;
            try
            {
                seed = ProtectedData.Unprotect(File.ReadAllBytes(file), null, DataProtectionScope.CurrentUser);
            }
            catch (Exception)
            {
                seed = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(seed);
                Directory.CreateDirectory(Keys);
                File.WriteAllBytes(file, ProtectedData.Protect(seed, null, DataProtectionScope.CurrentUser));
            }
            return seeds[name] = seed;
        }
    }

    static string KeyText(byte[] seed) => $"ed25519:{Convert.ToBase64String(Ed25519.Key(seed))}";

    /* The owner's grant to the app, everything on this computer for a day, made again an hour before it ends: {allow, by,
       to, until}, written in that order, as Onsay signs, signed by the owner, and sent as the base64 of the list it is the
       one grant of. */
    static string? badge;
    static DateTime badgeMade;
    static string Badge()
    {
        if (badge is not null && DateTime.Now - badgeMade < TimeSpan.FromHours(23)) return badge;
        var until = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 86_400;
        var grant = new JsonObject { ["allow"] = new JsonArray("/"), ["by"] = KeyText(Seed("owner")), ["to"] = KeyText(Seed("app")), ["until"] = until };
        var sig = Ed25519.Sign(Seed("owner"), Encoding.UTF8.GetBytes(grant.ToJsonString(Plain)));
        grant["sig"] = Convert.ToBase64String(sig);
        badge = Convert.ToBase64String(Encoding.UTF8.GetBytes(new JsonArray(grant).ToJsonString(Plain)));
        badgeMade = DateTime.Now;
        return badge;
    }

    ////////// 3. SETTINGS AND THE POLICY //////////

    // 3. Each in the order its code reads them, as the floor's table lists them, with its value or null.
    public static JsonArray SettingsFor(string lingo)
    {
        var names = Table()?[lingo]?["settings"] as JsonArray ?? new();
        var kept = SettingsOf(lingo);
        return new JsonArray(names.Select(n => (JsonNode)new JsonObject { ["name"] = $"{n}", ["value"] = kept[$"{n}"]?.DeepClone() }).ToArray());
    }

    // 3. What the app's settings lingo (Host.cs) answers a lingo's setting(...) with.
    public static JsonNode? Setting(string lingo, string key) => SettingsOf(lingo)[key]?.DeepClone();

    // 3. null takes it away. Why not, or null.
    public static string? Set(string lingo, string key, JsonNode? value)
    {
        try
        {
            var kept = SettingsOf(lingo);
            if (value is null) kept.Remove(key);
            else kept[key] = value.DeepClone();
            Directory.CreateDirectory(Path.Combine(Home, "settings"));
            File.WriteAllText(SettingsFile(lingo), kept.ToJsonString(Plain));
            return null;
        }
        catch (Exception) { return "That setting couldn't be kept."; }
    }

    static string SettingsFile(string lingo) => Path.Combine(Home, "settings", $"{lingo}.json");

    static JsonObject SettingsOf(string lingo)
    {
        try { return JsonNode.Parse(File.ReadAllText(SettingsFile(lingo))) as JsonObject ?? new(); }
        catch (Exception) { return new(); }
    }

    // 3. {grants: [...]}, empty when there is none yet.
    public static JsonObject Policy()
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(Path.Combine(Home, "policy.json"))) is JsonObject kept) return kept;
        }
        catch (Exception) { }
        return new() { ["grants"] = new JsonArray() };
    }

    // 3. Why not, or null.
    public static string? Keep(JsonObject policy)
    {
        try
        {
            File.WriteAllText(Path.Combine(Home, "policy.json"), policy.ToJsonString(Plain));
            return null;
        }
        catch (Exception) { return "The rules couldn't be kept."; }
    }

    ////////// 4. THE LINGOS //////////

    // 4. Each in words. What an app says it uses is never a permission: this computer's rules decide.
    public static readonly (string Name, string Words)[] Needs =
    {
        ("screen.read", "Sees windows and what is in them"),
        ("screen.act", "Clicks, types and opens apps"),
        ("files.read", "Reads your files"),
        ("files.write", "Changes, moves and deletes files"),
        ("run", "Runs commands"),
        ("lingos", "Makes and changes apps"),
    };

    // 4. What one of this computer's calls an app was refused does, in words, by its group, like files for files.read.
    public static string Wants(string call) => call.Split('.')[0] switch
    {
        "files" => "use your files",
        "input" or "controls" => "use your mouse and keyboard",
        "window" => "open and move windows",
        "clipboard" => "use your clipboard",
        "look" => "see your screen",
        "commands" => "run a program",
        "lingo" => "make or change an app",
        "http" or "socket" or "connection" => "reach the internet",
        "store" => "keep things",
        "native" => "use the system directly",
        _ => "do that",
    };

    /* 4. handle is this computer's own, so its other devices read as "Your desktop". A device signs as desktop.brandon,
          and this app, or the person, by their key. */
    public static string Who(string who, string? handle) => who switch
    {
        "this computer" => "You, on this computer",
        "public" => "Anyone",
        _ when who == KeyText(Seed("app")) || who == KeyText(Seed("owner")) => "You, on this computer",
        _ when who.StartsWith("ip ") => $"A computer at {who[3..]}",
        _ when who.Split('.') is [var device, var h] => h == handle ? $"Your {device}" : h,
        _ => who,
    };

    /* One lingo offered on the floor: whether it runs, why it can't, what it is, built into this app, what it needs, its
       parts, its route, what it lacks, and what it can be asked. */
    public sealed record Lingo(string Name, bool Running, string? Error, string About, bool BuiltIn, string[] Needs, string[] Parts, string Route, string[] Missing, Function[] Calls);

    // One thing an app can be asked for: its path, its name and what it does in words, and Group, the part of the app it
    // is in, or empty.
    public sealed record Function(string Path, string Name, string Does, string Group);

    // 4.
    public static string Title(string name) => name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;

    // The floor's table, home/offered.json, or null while the floor isn't answering.
    static JsonObject? Table()
    {
        try
        {
            using var res = Http.GetAsync(Url + "/", new CancellationTokenSource(3000).Token).GetAwaiter().GetResult();
            if (!res.IsSuccessStatusCode) return null;
        }
        catch (Exception) { return null; }
        try { return JsonNode.Parse(File.ReadAllText(Path.Combine(Home, "offered.json"))) as JsonObject ?? new(); }
        catch (Exception) { return new(); }
    }

    /* 4. From the table: each function a call, by its OpenAPI, its helpers left out. The app's own, the chat and
          settings, are built in. Null while the floor isn't answering. */
    public static List<Lingo>? Lingos()
    {
        var table = Table();
        if (table is null) return null;
        return table.Select(e =>
        {
            var calls = new List<Function>();
            foreach (var (path, ops) in e.Value?["openapi"]?["paths"]?.AsObject() ?? new JsonObject())
            {
                var op = ops?["post"];
                if ((bool?)op?["x-lingo-internal"] == true) continue;
                calls.Add(new Function(path, Title(path.Split('/').Last().Replace('_', ' ')), $"{op?["description"] ?? op?["summary"]}", ""));
            }
            var builtIn = Host.Lingos.Any(l => l.Name == e.Key);
            return new Lingo(e.Key, true, null, $"{e.Value?["about"]}", builtIn, Array.Empty<string>(), Array.Empty<string>(), "/" + e.Key, Array.Empty<string>(), calls.ToArray());
        }).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // 4. On offers a shipped lingo's code again, off takes it away. Why not, or null.
    public static string? Switch(string name, bool on)
    {
        var file = Path.Combine(Shipped, name + ".ls");
        if (on && !File.Exists(file)) return "Only the apps that come with Onsay can be turned back on here.";
        var why = Call("/lingo/offer", new JsonArray(name, on ? File.ReadAllText(file) : "")).why;
        if (why is not null) return why;
        settings.Off ??= new();
        settings.Off.Remove(name);
        if (!on) settings.Off.Add(name);
        settings.Save();
        return null;
    }
}
