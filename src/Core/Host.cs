// Host.cs - the app's own lingos, which the floor calls like any lingo offered at an address, and the to-dos, all on one
// small web server on this computer (127.0.0.1, a port of its own each run): the chat, which asks the person questions
// and shows what is being done, settings, which keeps each lingo's settings for it (Core.cs), and screen, canvases lingos
// draw on (Screens.cs). A call from the floor
// counts only when the floor signed it (RFC 9421) with its own key. /onsay is the to-dos (ToDos.cs), for the chat window
// and any program given the prompt key.
//
// 1. Start(settings), Url        the server, started once, and where it answers
//    Lingos                      the app's own lingos as the floor reads them, {Name, Code}
// 2. Answer(method, path, ...)   one request: from the floor, checked and answered by the lingo it names, or for /onsay
//    FromFloor(...)              whether the floor signed it
// 3. Serve(client)               one connection: its request read whole, answered, closed
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Host
{
    static TcpListener? listener;
    static Settings settings = null!;
    public static string Url { get; private set; } = "";
    public static string PromptKey => settings?.PromptKey ?? "";   // what a program sends /onsay with, made once

    // 1. Each written as its signatures alone: the floor sends each call here.
    public static readonly (string Name, string Code)[] Lingos =
    {
        ("chat", "\"The person's chat in the Onsay app on this computer, where they see what is being done and answer questions.\"\n" +
            "fn ask(questions: list|text \"each a text, answered by typing, or \\{question, choices}, answered by picking one\", seconds: number \"how long to wait\" = 120) -> list \"Asks the person questions in Onsay's chat on this computer and waits for their answers, in order, so the work goes on with them. Nothing when nobody answered in time.\" {}\n" +
            "fn say(text: text \"what to tell them\") \"Tells the person something in Onsay's chat while the work goes on.\" {}\n" +
            "fn step(text: text \"what is being done now\") \"Shows what is being done now as a step under the person's to-do. Say one as each part of a long job starts.\" {}\n"),
        ("settings", "\"Each lingo's settings on this computer, kept by the Onsay app.\"\n" +
            "fn get(key: text \"the setting's name\", reference: text \"the lingo whose setting it is\") \"A lingo's setting as the person set it, or nothing.\" {}\n"),
        ("screen", "\"Screens in the Onsay app on this computer: canvases a lingo draws on, which the person watches beside the to-do the work is for. Never video.\"\n" +
            "fn open(title: text \"what it shows, like Browser tab 1\") -> number \"A new screen, or the one of that title already open for the to-do, so a lingo called again and again draws on one.\" {}\n" +
            "fn draw(screen: number \"from screen.open\", instructions: list \"Canvas 2D instructions as in a web page's canvas, each a record with one key, like \\{fillRect: [0, 0, 120, 12]}: clear [w, h] starts it over at a size, and save, restore, translate, scale, rotate, beginPath, closePath, moveTo, lineTo, bezierCurveTo, quadraticCurveTo, arc, rect, roundRect, fill, stroke, clip, fillRect, strokeRect, clearRect, fillText, strokeText, drawImage, fillStyle, strokeStyle, lineWidth, font, textAlign, textBaseline and globalAlpha draw after what was drawn before. drawImage takes only a picture inside it, a data:image/ address\") \"Draws on a screen.\" {}\n" +
            "fn show(screen: number \"from screen.open\", picture: text \"a PNG or JPEG as base64, or a data: address\") \"Puts a whole picture on a screen, in place of what was there.\" {}\n" +
            "fn close(screen: number \"from screen.open\") \"Takes a screen away.\" {}\n"),
    };

    // 1. Before the floor starts, so its address is known when the lingos are offered.
    public static void Start(Settings s)
    {
        if (listener is not null) return;
        settings = s;
        settings.PromptKey ??= Convert.ToBase64String(Guid.NewGuid().ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        settings.Save();
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        Task.Run(async () =>
        {
            for (;;)
            {
                var client = await listener.AcceptTcpClientAsync();
                _ = Task.Run(() => Serve(client));
            }
        });
    }

    ////////// 2. ANSWERING //////////

    /* 2. Its status and JSON. A lingo's call has its values in order, as the floor sends them, and Onsay-Run names the
          to-do it is for. */
    static async Task<(int status, JsonNode? body)> Answer(string method, string path, Dictionary<string, string> headers, byte[] body)
    {
        JsonNode? given = null;
        try { given = body.Length > 0 ? JsonNode.Parse(body) : null; }
        catch (Exception) { return (400, Error("Its body isn't JSON")); }

        if (path.StartsWith("/onsay"))
        {
            if (headers.GetValueOrDefault("authorization") != $"Bearer {settings.PromptKey}") return (401, Error("That needs the prompt key, in Settings, Advanced"));
            var rest = path.Substring(6).Split('?')[0].Trim('/').Split('/');
            if (method == "POST" && rest is [""])
            {
                var (answer, why) = await Task.Run(() => ToDos.Prompt(given as JsonObject ?? new()));
                return answer is null ? (500, Error(why!)) : (200, answer);
            }
            if (method == "GET" && rest is ["sessions"]) return (200, ToDos.List());
            if (method == "GET" && rest is ["sessions", var id]) return (200, ToDos.Turns(id));
            if (method == "GET" && rest is ["sessions", var live, "live"]) return (200, ToDos.Live(live));
            if (method == "POST" && rest is ["sessions", var asked, "answers"])
            {
                ToDos.Answer(null, asked, given?["answers"] as JsonArray ?? new());
                return (200, new JsonObject());
            }
            return (404, Error("Nothing is there"));
        }

        if (method != "POST" || !FromFloor(path, headers, body)) return (401, Error("Only the floor on this computer calls these"));
        var values = given as JsonArray ?? new JsonArray();
        var run = headers.GetValueOrDefault("onsay-run");
        switch (path)
        {
            case "/chat/ask":
                return (200, await ToDos.Ask(run, values.ElementAtOrDefault(0) ?? new JsonArray(), (double?)values.ElementAtOrDefault(1) ?? 120));
            case "/chat/say":
            case "/chat/step":
                ToDos.Step(run, path.Substring(6), $"{values.ElementAtOrDefault(0)}");
                return (200, null);
            case "/settings/get":
                return (200, Core.Setting($"{values.ElementAtOrDefault(1)}", $"{values.ElementAtOrDefault(0)}"));
            case "/screen/open":
                return (200, Screens.Open(run ?? "", $"{values.ElementAtOrDefault(0)}"));
            case "/screen/draw":
            case "/screen/show":
            {
                long id = (long?)(double?)values.ElementAtOrDefault(0) ?? 0;
                var why = path == "/screen/draw" ? Screens.Draw(id, values.ElementAtOrDefault(1)) : Screens.Show(id, $"{values.ElementAtOrDefault(1)}");
                return why is null ? (200, null) : (400, Error(why));
            }
            case "/screen/close":
                Screens.Close((long?)(double?)values.ElementAtOrDefault(0) ?? 0);
                return (200, null);
            default:
                return (404, Error("Nothing is there"));
        }
    }

    static JsonObject Error(string why) => new() { ["error"] = why };

    /* 2. Signed by the floor's own key, which it says at GET / and names as its keyid: over the method, the whole address
          and the body's digest, made in the last 5 minutes. */
    static string? floorKey;
    static bool FromFloor(string path, Dictionary<string, string> headers, byte[] body)
    {
        try
        {
            if (floorKey is null)
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                floorKey = $"{JsonNode.Parse(http.GetStringAsync(Core.Url + "/").GetAwaiter().GetResult())?["key"]}";
            }
            var input = headers.GetValueOrDefault("signature-input") ?? "";
            var sig = headers.GetValueOrDefault("signature") ?? "";
            var digest = headers.GetValueOrDefault("content-digest") ?? "";
            var parameters = input.StartsWith("sig=") ? input.Substring(4) : "";
            var keyid = parameters.Split(';').FirstOrDefault(p => p.StartsWith("keyid="))?.Substring(6).Trim('"');
            var created = parameters.Split(';').FirstOrDefault(p => p.StartsWith("created="))?.Substring(8);
            if (keyid != floorKey || !long.TryParse(created, out var at) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - at) > 300) return false;
            using var sha = SHA256.Create();
            if (digest != $"sha-256=:{Convert.ToBase64String(sha.ComputeHash(body))}:") return false;
            var basis = $"\"@method\": POST\n\"@target-uri\": http://{headers.GetValueOrDefault("host")}{path}\n\"content-digest\": {digest}\n\"@signature-params\": {parameters}";
            var raw = Convert.FromBase64String(sig.Substring(5).TrimEnd(':'));
            return Ed25519.Check(Convert.FromBase64String(keyid!.Substring(8)), Encoding.UTF8.GetBytes(basis), raw);
        }
        catch (Exception)
        {
            floorKey = null;
            return false;
        }
    }

    ////////// 3. SERVING //////////

    // 3. Its first line, its headers by lower case name, and a body of Content-Length, at most 20 MB, a screen's picture.
    static async Task Serve(TcpClient client)
    {
        using var _ = client;
        try
        {
            var s = client.GetStream();
            var head = new List<byte>();
            var one = new byte[1];
            while (head.Count < 65536 && !(head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n'))
            {
                if (await s.ReadAsync(one, 0, 1) == 0) return;
                head.Add(one[0]);
            }
            var lines = Encoding.UTF8.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            var first = lines.ElementAtOrDefault(0)?.Split(' ') ?? Array.Empty<string>();
            if (first.Length < 2) return;
            var headers = new Dictionary<string, string>();
            foreach (var line in lines.Skip(1))
            {
                var colon = line.IndexOf(':');
                if (colon > 0) headers[line.Substring(0, colon).Trim().ToLowerInvariant()] = line.Substring(colon + 1).Trim();
            }
            int length = int.TryParse(headers.GetValueOrDefault("content-length"), out var n) ? Math.Min(n, 20 << 20) : 0;
            var body = new byte[length];
            for (int got = 0; got < length;)
            {
                int k = await s.ReadAsync(body, got, length - got);
                if (k == 0) return;
                got += k;
            }
            var (status, answer) = await Answer(first[0], first[1], headers, body);
            var text = Encoding.UTF8.GetBytes(answer?.ToJsonString() ?? "null");
            var top = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} \r\nContent-Type: application/json\r\nContent-Length: {text.Length}\r\nConnection: close\r\n\r\n");
            await s.WriteAsync(top, 0, top.Length);
            await s.WriteAsync(text, 0, text.Length);
        }
        catch (Exception) { }
    }
}
