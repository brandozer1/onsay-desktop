// Requests.cs - the log of every request between this computer and Onsay, kept here so anyone can read it: the app's own
// messages (Link.cs) and the core's (Core.cs runs it with --sent, and it prints each as it goes). Written as it happens
// to %AppData%\Onsay\requests.log, one JSON line each, oldest first, the sign in token the one thing masked. Pings are
// not kept. The file is cut to its newest half when it passes Limit, so it never grows without end. Shown under
// Settings, Requests (SettingsWindow.cs).
//
// 1. Keep(json)      one message, as JSON text, from any thread
// 2. Read()          every message kept, newest first: [(at, kind, json)]
// 3. Kinds           what each kind of message is, in plain words
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Onsay.Desktop;

static class Requests
{
    public static readonly string File_ = Path.Combine(Settings.Folder, "requests.log");
    const long Limit = 4 * 1024 * 1024;
    const int Most = 500;   // the most messages read back
    static readonly object writing = new();
    static readonly Regex Token = new("\"token\":\"[^\"]*\"");

    // 3.
    public static readonly Dictionary<string, string> Kinds = new()
    {
        ["hello"] = "This computer connected: its name and what its lingos can do, never their code",
        ["say"] = "A prompt you sent, with the chat before it and what was on screen around it",
        ["stop"] = "Stop",
        ["halt"] = "The stop key",
        ["catalog"] = "Its lingos changed: the routes it offers now",
        ["lingo"] = "A lingo asked for something while it ran: lingo(...), use(...) or a setting it needs",
        ["result"] = "A lingo's answer, sent back through Onsay to whoever asked",
    };

    // 1.
    public static void Keep(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is "ping" or "pong") return;
            var line = JsonSerializer.Serialize(new { at = DateTime.UtcNow, message = JsonDocument.Parse(Token.Replace(json, "\"token\":\"(your sign in token)\"")).RootElement });
            lock (writing)
            {
                Directory.CreateDirectory(Settings.Folder);
                File.AppendAllText(File_, line + "\n");
                var f = new FileInfo(File_);
                if (f.Length > Limit)
                {
                    var lines = File.ReadAllLines(File_);
                    File.WriteAllLines(File_, lines.Skip(lines.Length / 2));
                }
            }
        }
        catch { }   // the log is never worth breaking a message over
    }

    // 2.
    public static List<(DateTime At, string Kind, string Json)> Read()
    {
        var out_ = new List<(DateTime, string, string)>();
        try
        {
            string[] lines;
            lock (writing) lines = File.Exists(File_) ? File.ReadAllLines(File_) : Array.Empty<string>();
            foreach (var line in lines.Reverse().Take(Most))
            {
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var m = doc.RootElement.GetProperty("message");
                    var kind = m.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                    out_.Add((doc.RootElement.GetProperty("at").GetDateTime().ToLocalTime(), kind, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true })));
                }
                catch { }
            }
        }
        catch { }
        return out_;
    }
}
