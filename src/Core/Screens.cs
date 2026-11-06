// Screens.cs - the screens lingos draw on, as the onsay lingo on this computer streams them from /onsay/screens: a
// WebSocket to the lingo client that gives every screen first, then each batch of instructions as it is drawn, kept
// here for the to-do list and the Screens window (ScreensWindow.cs), and drawn here (Drawing.cs). Never video.
//
// 1. All, Of(session)     every open screen, oldest first, or those of one to-do, and Get(id) one by its id
//    Changed(id)          raised on the UI thread: with an id when only what that screen shows changed, so only its
//                         pictures need painting again, and null when one opened, closed or moved to a to-do
// 2. Watch(key)           keeps the stream open while the app runs, back again after the lingo client restarts. key
//                         gives the prompt key, read again before each try
// 3. Close(id, key)       the person closing one
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
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
                using var ws = new ClientWebSocket();
                ws.Options.SetRequestHeader("Authorization", $"Bearer {await key()}");
                await ws.ConnectAsync(new Uri($"{Core.Url.Replace("http://", "ws://")}/onsay/screens"), CancellationToken.None);
                bool first = true;
                var buffer = new byte[64 * 1024];
                while (ws.State == WebSocketState.Open)
                {
                    using var whole = new MemoryStream();
                    WebSocketReceiveResult got;
                    do
                    {
                        got = await ws.ReceiveAsync(buffer, CancellationToken.None);
                        whole.Write(buffer, 0, got.Count);
                    } while (!got.EndOfMessage && got.MessageType != WebSocketMessageType.Close);
                    if (got.MessageType == WebSocketMessageType.Close) break;
                    var said = JsonNode.Parse(whole.ToArray())?["screens"] as JsonArray ?? new();
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

    ////////// 3. CLOSING ONE //////////

    // 3. The stream says it closed, so nothing changes here until it does.
    public static async void Close(long id, string? key)
    {
        try
        {
            using var http = new HttpClient();
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{Core.Url}/onsay/screens/{id}");
            req.Headers.Authorization = new("Bearer", key);
            using var _ = await http.SendAsync(req);
        }
        catch (Exception) { }
    }
}
