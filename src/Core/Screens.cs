// Screens.cs - the screens lingos draw on, as the onsay lingo on this computer streams them from /onsay/screens: a
// WebSocket to the lingo client that gives every screen first, then each batch of instructions as it is drawn, kept
// here for the to-do list and the Screens window (ScreensWindow.cs), and drawn here (Drawing.cs). Never video.
//
// 1. All, Of(session)     every open screen, oldest first, or those of one to-do, and Get(id) one by its id
//    Changed(id)          raised on the UI thread: with an id when only what that screen shows changed, so only its
//                         pictures need painting again, and null when one opened, closed or moved to a to-do
// 2. Watch(key)           keeps the stream open while the app runs, back again after the lingo client restarts. key
//                         gives the prompt key, read again before each try
//    Connect, Message     the WebSocket to the lingo client on this computer (RFC 6455), written here since Windows 7
//                         has none of its own: the handshake, and each whole text message, pings answered
// 3. Close(id, key)       the person closing one
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace Onsay.Desktop;

// A screen: its picture as drawn here, null before anything is drawn on it, its size, and its instructions since its
// last clear.
sealed record Screen(long Id, string Session, string Title, ImageSource? Picture, double Width, double Height, List<JsonNode?> Drawn);

static class Screens
{
    static readonly Dictionary<long, Screen> open = new();
    static bool watching;

    ////////// 1. WHAT IS OPEN //////////

    // 1.
    public static IReadOnlyList<Screen> All => open.Values.OrderBy(s => s.Id).ToList();
    public static IReadOnlyList<Screen> Of(string session) => All.Where(s => s.Session == session).ToList();
    public static Screen? Get(long id) => open.GetValueOrDefault(id);
    public static event Action<long?>? Changed;

    ////////// 2. THE STREAM //////////

    /* 2. Each message is {screens: [...]}, each {screen, session, title, draw, fresh}, draw a batch of instructions, in
          place of what was drawn when fresh, else after it, or a closed one {screen, closed: true}. The first has every
          screen, so what was kept from before is let go. */
    public static async void Watch(Func<Task<string?>> key)
    {
        if (watching) return;
        watching = true;
        for (;;)
        {
            try
            {
                using var tcp = new TcpClient();
                using var ws = await Connect(tcp, "/onsay/screens", await key());
                bool first = true;
                while (await Message(ws) is byte[] message)
                {
                    var said = JsonNode.Parse(message)?["screens"] as JsonArray ?? new();
                    bool moved = first;   // whether one opened, closed or changed to-do, not only what it shows
                    var painted = new List<long>();
                    if (first) open.Clear();
                    first = false;
                    foreach (var s in said)
                    {
                        long id = (long?)s?["screen"] ?? 0;
                        if ((bool?)s?["closed"] == true)
                        {
                            moved |= open.Remove(id);
                            continue;
                        }
                        var drawn = new List<JsonNode?>();
                        if ((bool?)s?["fresh"] != true && open.TryGetValue(id, out var was)) drawn.AddRange(was.Drawn);
                        drawn.AddRange(s?["draw"] as JsonArray ?? new());
                        var (picture, width, height) = await Task.Run(() => Drawing.Draw(drawn));
                        var now = new Screen(id, $"{s?["session"]}", $"{s?["title"]}", drawn.Count > 0 ? picture : null, width, height, drawn);
                        if (open.TryGetValue(id, out var before) && before.Session == now.Session && before.Title == now.Title) painted.Add(id);
                        else moved = true;
                        open[id] = now;
                    }
                    if (moved) Changed?.Invoke(null);
                    else foreach (var id in painted) Changed?.Invoke(id);
                }
            }
            catch (Exception) { }
            if (open.Count > 0)
            {
                open.Clear();
                Changed?.Invoke(null);
            }
            await Task.Delay(2000);
        }
    }

    // 2. A WebSocket to path on the lingo client here, its handshake read through to its 101, or a failure.
    static async Task<Stream> Connect(TcpClient tcp, string path, string? key)
    {
        var at = new Uri(Host.Url);
        await tcp.ConnectAsync(at.Host, at.Port);
        var s = tcp.GetStream();
        var nonce = new byte[16];
        Compat.Shared.NextBytes(nonce);
        var head = new StringBuilder();
        head.Append($"GET {path} HTTP/1.1\r\nHost: {at.Host}:{at.Port}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n");
        head.Append($"Sec-WebSocket-Key: {Convert.ToBase64String(nonce)}\r\nSec-WebSocket-Version: 13\r\nAuthorization: Bearer {key}\r\n");
        head.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(head.ToString());
        await s.WriteAsync(bytes, 0, bytes.Length);
        var answer = new StringBuilder();
        var one = new byte[1];
        while (!answer.ToString().EndsWith("\r\n\r\n"))
        {
            if (await s.ReadAsync(one, 0, 1) == 0) throw new IOException("closed");
            answer.Append((char)one[0]);
        }
        if (!answer.ToString().StartsWith("HTTP/1.1 101")) throw new IOException("refused");
        return s;
    }

    // 2. The next whole text message, its parts joined, a ping answered with a pong, or null once it closes.
    static async Task<byte[]?> Message(Stream s)
    {
        using var whole = new MemoryStream();
        for (;;)
        {
            var h = await Read(s, 2);
            if (h is null) return null;
            int op = h[0] & 0x0f;
            long n = h[1] & 0x7f;
            if (n == 126) n = await Read(s, 2) is { } b2 ? b2[0] << 8 | b2[1] : -1;
            else if (n == 127) n = await Read(s, 8) is { } b8 ? b8.Aggregate(0L, (x, b) => x << 8 | b) : -1;
            if (n < 0 || n > 64 << 20) return null;
            var body = await Read(s, (int)n);
            if (body is null || op == 8) return null;
            if (op == 9)
            {
                var mask = new byte[4];
                var pong = new byte[] { 0x8a, (byte)(0x80 | body.Length) }.Concat(mask).Concat(body).ToArray();   // a zero mask leaves it as it is
                await s.WriteAsync(pong, 0, pong.Length);
                continue;
            }
            if (op != 0 && op != 1) continue;
            whole.Write(body, 0, body.Length);
            if ((h[0] & 0x80) != 0) return whole.ToArray();
        }
    }

    // n bytes, or null when it closed first.
    static async Task<byte[]?> Read(Stream s, int n)
    {
        var b = new byte[n];
        for (int got = 0; got < n;)
        {
            int k = await s.ReadAsync(b, got, n - got);
            if (k == 0) return null;
            got += k;
        }
        return b;
    }

    ////////// 3. CLOSING ONE //////////

    // 3. The stream says it closed, so nothing changes here until it does.
    public static async void Close(long id, string? key)
    {
        try
        {
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{Host.Url}/onsay/screens/{id}");
            req.Headers.Authorization = new("Bearer", key);
            using var _ = await http.SendAsync(req);
        }
        catch (Exception) { }
    }
}
