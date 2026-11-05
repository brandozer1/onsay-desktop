// Screens.cs - the screens lingos draw on, as the onsay lingo on this computer streams them from /onsay/screens: a
// WebSocket to the lingo client that gives every screen first, then each change, kept here for the to-do list and the
// Screens window (ScreensWindow.cs). Each screen is a picture a lingo drew, never video.
//
// 1. All, Of(session)     every open screen, oldest first, or those of one to-do
//    Changed              raised on the UI thread whenever one opens, shows a new picture or closes
// 2. Watch(key)           keeps the stream open while the app runs, back again after the lingo client restarts. key
//                         gives the prompt key, read again before each try
// 3. Close(id, key)       the person closing one
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace Onsay.Desktop;

sealed record Screen(long Id, string Session, string Title, BitmapSource? Picture);

static class Screens
{
    static readonly Dictionary<long, Screen> open = new();
    static bool watching;

    ////////// 1. WHAT IS OPEN //////////

    // 1.
    public static IReadOnlyList<Screen> All => open.Values.OrderBy(s => s.Id).ToList();
    public static IReadOnlyList<Screen> Of(string session) => All.Where(s => s.Session == session).ToList();
    public static event Action? Changed;

    ////////// 2. THE STREAM //////////

    // 2. Each message is {screens: [...]}, a closed one {screen, closed: true}. The first has every screen, so what was
    //    kept from before is let go.
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
                    var drawn = await Task.Run(() => said.Select(Read).ToList());
                    if (first) open.Clear();
                    first = false;
                    foreach (var (id, screen) in drawn)
                    {
                        if (screen is null) open.Remove(id);
                        else open[id] = screen with { Picture = screen.Picture ?? open.GetValueOrDefault(id)?.Picture };
                    }
                    Changed?.Invoke();
                }
            }
            catch (Exception) { }
            if (open.Count > 0)
            {
                open.Clear();
                Changed?.Invoke();
            }
            await Task.Delay(2000);
        }
    }

    // 2. One screen as the stream says it, its picture made ready to draw here, or null for one closed.
    static (long, Screen?) Read(JsonNode? s)
    {
        long id = (long?)s?["screen"] ?? 0;
        if ((bool?)s?["closed"] == true) return (id, null);
        BitmapSource? picture = null;
        try
        {
            if (s?["picture"] is JsonValue p && $"{p}".Length > 0)
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(Convert.FromBase64String($"{p}"));
                image.EndInit();
                image.Freeze();
                picture = image;
            }
        }
        catch (Exception) { }
        return (id, new Screen(id, $"{s?["session"]}", $"{s?["title"]}", picture));
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
