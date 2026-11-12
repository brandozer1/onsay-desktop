// Access.cs - who besides the person may use Onsay on this computer. A program here that the floor refused, one that signs
// its calls with a key of its own, is asked about by its name, its file and who signed it, in the chat, and Allow gives
// its key a grant for that kind of call. The grants, to people and programs, show on the Access page (SettingsWindow.cs),
// each with what it lets them do and a way to take it back. A program that doesn't sign its calls can't be told apart
// from any other, so it is never asked about and never let in.
//
// 1. Watch()           reads the floor's requests.log as it grows, asking once about each program and kind of call refused
// 2. Program(path)     a program's name, from its file, and who signed it, or null when nobody did
//    Kind(route)       the kind of call a route is, its group, like files for /files/read
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Access
{
    static readonly HashSet<string> asked = new();   // each program's key and kind of call asked about while the app runs
    static bool watching;

    ////////// 1. WATCHING //////////

    // 1. From where the log ends as the app starts, so nothing refused before is asked about again.
    public static void Watch()
    {
        if (watching) return;
        watching = true;
        var log = Path.Combine(Core.Home, "requests.log");
        long seen = File.Exists(log) ? new FileInfo(log).Length : 0;
        Task.Run(async () =>
        {
            for (;;)
            {
                await Task.Delay(2000);
                try
                {
                    long size = File.Exists(log) ? new FileInfo(log).Length : 0;
                    if (size < seen) seen = 0;   // the floor cut it to its newest half
                    if (size == seen) continue;
                    using var file = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    file.Seek(seen, SeekOrigin.Begin);
                    var bytes = new byte[size - seen];
                    int got = await file.ReadAsync(bytes, 0, bytes.Length);
                    var text = Encoding.UTF8.GetString(bytes, 0, got);
                    int whole = text.LastIndexOf('\n') + 1;   // a line still being written waits for next time
                    seen += Encoding.UTF8.GetByteCount(text.Substring(0, whole));
                    foreach (var line in text.Substring(0, whole).Split('\n'))
                        if (line.Length > 0 && JsonNode.Parse(line) is JsonObject l) Refused(l);
                }
                catch (Exception) { }
            }
        });
    }

    /* A call a script made that no grant let in, from a program here that signed it: asked about once, and allowed for
       that kind of call from then on when the person says so. It runs again when the program next tries. */
    static void Refused(JsonObject line)
    {
        var caller = $"{line["caller"]}";
        var program = $"{line["program"]}";
        var kind = Kind($"{line["route"]}");
        if ($"{line["way"]}" != "call" || (bool?)line["allowed"] != false || !caller.StartsWith("ed25519:") || program.Length == 0 || kind.Length == 0) return;
        lock (asked)
            if (!asked.Add($"{caller} {kind}")) return;
        _ = Ask(caller, kind, program);
    }

    static async Task Ask(string key, string kind, string program)
    {
        var (name, signer) = Program(program);
        var who = signer is null ? "Nobody signed it, so there's no telling who made it." : $"It's signed by {signer}.";
        var question = new JsonObject
        {
            ["question"] = $"{name} wants to {Core.Wants(kind)} on this computer. Allow it?",
            ["why"] = $"{program}. {who} Allowed, it can from now on, and you can take it back in Settings, Access.",
            ["choices"] = new JsonArray("Allow", "Not now"),
            ["other"] = false,
            ["skip"] = false,
        };
        var answers = await ToDos.Ask(null, new JsonArray(question), 3600);
        if ($"{answers?.FirstOrDefault()}" != "Allow") return;
        var policy = Core.Policy();
        var grants = policy["grants"] as JsonArray ?? new JsonArray();
        grants.Add(new JsonObject { ["to"] = key, ["allow"] = new JsonArray($"/{kind}"), ["data"] = new JsonObject { ["program"] = program, ["name"] = name } });
        policy["grants"] = grants.DeepClone();
        Core.Keep(policy);
    }

    ////////// 2. A PROGRAM IN WORDS //////////

    // 2. Its file's own description, else its file name without .exe, and the name on its signature.
    public static (string name, string? signer) Program(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string? signer = null;
        try
        {
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription)) name = info.FileDescription.Trim();
        }
        catch (Exception) { }
        try
        {
            var subject = X509Certificate.CreateFromSignedFile(path).Subject;
            signer = subject.Split(',').Select(p => p.Trim()).FirstOrDefault(p => p.StartsWith("CN="))?.Substring(3).Trim('"') ?? subject;
        }
        catch (Exception) { }
        return (name, signer);
    }

    // 2.
    public static string Kind(string route) => route.TrimStart('/').Split('/')[0];
}
