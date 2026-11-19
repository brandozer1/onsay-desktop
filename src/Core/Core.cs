// Core.cs - this computer's port: Onsay's port for Node (../onsay-ports/node) running the agent (agent/agent.js), which
// makes this computer a device of the person's Onsay account. The device key is made here, its private half sealed for
// this Windows user and never sent anywhere; signing in is adding its public half on onsay.dev's Devices page. The app
// calls the port on this computer alone, with the owner's local key it keeps in its folder.
//
// 1. Start()                  starts the port with the agent, and starts it again if it ever stops: on startup and the
//                             stop key. Stop(), Running ends it, keeps it ended, and whether it runs
// 2. Ask(path, body, step)    one call on the port, like /agent/ask, its answer read a line at a time: each { step }
//                             given to step as it comes, and the last line the answer, or why not in words
// 3. Status()                 { signed_in, connected, handle, device, key }: whether Onsay knows this computer's key,
//                             as the port last heard, and its name there. SignedIn fires the first time it is
//    AddUrl(), Name           onsay.dev's Devices page with this computer's key and name filled in, and that name
//    SignOut()                this computer's key forgotten, so it is no device until it is added again. Why not, or null
// 4. Title(name), Version     a name as a title, the first letter capital, and this app's version
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Core
{
    static int port;   // where the port answers, on this computer alone: a free one, picked each time it starts
    public static readonly string Home = Path.Combine(Settings.Folder, "port");
    public static readonly string Log = Path.Combine(Settings.Folder, "port.log");
    static readonly string KeyFile = Path.Combine(Settings.Folder, "keys", "device.key");
    static readonly string Agent = Path.Combine(AppContext.BaseDirectory, "agent", "agent.js");
    // Node, as shipped beside the app, else the one on the path.
    static readonly string Node = File.Exists(Path.Combine(AppContext.BaseDirectory, "node", "node.exe"))
        ? Path.Combine(AppContext.BaseDirectory, "node", "node.exe")
        : "node";
    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    static Process? process;
    static bool wanted;
    static bool signedIn;
    public static event Action? SignedIn;

    ////////// 1. RUNNING IT //////////

    // 1. From any thread: the stop key calls it from its own.
    public static void Start()
    {
        Stop();
        Directory.CreateDirectory(Home);
        var free = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        free.Start();
        port = ((System.Net.IPEndPoint)free.LocalEndpoint).Port;
        free.Stop();
        wanted = true;
        Run();
    }

    // Its errors go to port.log.
    static void Run()
    {
        if (!wanted || process is { HasExited: false } || !File.Exists(Agent)) return;
        var info = new ProcessStartInfo(Node, $"\"{Agent}\"")
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true,
            WorkingDirectory = Home,
        };
        info.EnvironmentVariables["ONSAY_HOME"] = Home;
        info.EnvironmentVariables["ONSAY_PORT"] = $"{port}";
        info.EnvironmentVariables["ONSAY_KEY"] = $"onsay_device_{Base64Url(Seed())}";
        var p = new Process { StartInfo = info, EnableRaisingEvents = true };
        void Write(string? line)
        {
            if (line is null) return;
            try { File.AppendAllText(Log, $"{DateTime.Now:s} {line}\n"); } catch { }
        }
        p.ErrorDataReceived += (_, e) => Write(e.Data);
        p.OutputDataReceived += (_, e) => Write(e.Data);
        p.Exited += async (_, _) =>
        {
            await Task.Delay(2000);
            Run();
        };
        try
        {
            p.Start();
        }
        catch (Exception e)
        {
            Write($"The port didn't start: {e.Message}");
            return;
        }
        Native.EndsWithThis(p);
        p.BeginErrorReadLine();
        p.BeginOutputReadLine();
        process = p;
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
    }

    public static bool Running => process is { HasExited: false };

    ////////// 2. CALLS //////////

    // 2. The owner's local key, made by the port the first time it runs.
    public static (JsonObject? answer, string? why) Ask(string path, JsonObject body, Action<string>? step = null)
    {
        try
        {
            var local = File.ReadAllText(Path.Combine(Home, "local.key")).Trim();
            using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}{path}")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            request.Headers.Authorization = new("Bearer", local);
            using var res = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            using var reader = new StreamReader(res.Content.ReadAsStreamAsync().GetAwaiter().GetResult());
            JsonObject? last = null;
            while (reader.ReadLine() is { } line)
            {
                if (line.Trim().Length == 0 || JsonNode.Parse(line) is not JsonObject said) continue;
                if (said["step"] is JsonValue words) step?.Invoke($"{words}");
                else last = said;
            }
            if (!res.IsSuccessStatusCode || last?["error"] is not null) return (null, $"{last?["error"] ?? "The port couldn't do that."}");
            return (last ?? new JsonObject(), null);
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            return (null, "Onsay's helper on this computer isn't running. It starts again by itself, so try again in a moment.");
        }
    }

    ////////// 3. THE ACCOUNT //////////

    /* 3. What Onsay said of this computer's key in the answer to the port's last post, me.json: who it is on Onsay. Fresh
          for a minute after, so connected means Onsay heard from it lately. */
    public static JsonObject Status()
    {
        var file = Path.Combine(Home, "me.json");
        JsonNode? me = null;
        try { me = JsonNode.Parse(File.ReadAllText(file)); }
        catch (Exception) { }
        var known = me?["handle"] is not null;
        var fresh = File.Exists(file) && DateTime.Now - File.GetLastWriteTime(file) < TimeSpan.FromMinutes(1);
        if (known && !signedIn) SignedIn?.Invoke();
        signedIn = known;
        return new JsonObject
        {
            ["signed_in"] = known,
            ["connected"] = known && fresh && Running,
            ["handle"] = me?["handle"]?.DeepClone(),
            ["device"] = me?["device"]?.DeepClone(),
            ["key"] = KeyText(),
        };
    }

    // 3. Lower case letters, digits and _, as a device's name must be: DESKTOP-7Q is desktop_7q.
    public static string Name
    {
        get
        {
            var name = new string(Environment.MachineName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            return name.Length > 0 && char.IsLetter(name[0]) ? name : $"computer_{name}";
        }
    }

    // 3.
    public static string AddUrl() => $"https://onsay.dev/devices?key={Uri.EscapeDataString(KeyText())}&name={Uri.EscapeDataString(Name)}";

    // 3. A new key is made the next time the port starts.
    public static string? SignOut()
    {
        try
        {
            File.Delete(KeyFile);
            File.Delete(Path.Combine(Home, "me.json"));
            lock (gate) seed = null;
            signedIn = false;
            Start();
            return null;
        }
        catch (Exception) { return "This computer couldn't be signed out. Try again in a moment."; }
    }

    /* The device key's private seed, sealed for this Windows user (DPAPI) in keys\device.key, made the first time. */
    static readonly object gate = new();
    static byte[]? seed;
    static byte[] Seed()
    {
        lock (gate)
        {
            if (seed is not null) return seed;
            try
            {
                return seed = ProtectedData.Unprotect(File.ReadAllBytes(KeyFile), null, DataProtectionScope.CurrentUser);
            }
            catch (Exception)
            {
                var made = new byte[32];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(made);
                Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);
                File.WriteAllBytes(KeyFile, ProtectedData.Protect(made, null, DataProtectionScope.CurrentUser));
                return seed = made;
            }
        }
    }

    // The public half as Onsay writes keys.
    static string KeyText() => $"ed25519:{Convert.ToBase64String(Ed25519.Key(Seed()))}";

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    ////////// 4. WORDS //////////

    // 4.
    public static string Title(string name) => name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;

    // 4. As the build gave it, like 0.27.0.
    public static string Version => typeof(Core).Assembly.GetName().Version?.ToString(3) ?? "";
}
