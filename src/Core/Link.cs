// Link.cs - how this app reaches Onsay, which answers each message.
//
// 1. Connect(s, onCode, stop)     signs this app in through the website
// 2. Start(s, heard)              opens the live connection and keeps it open
//    Heartbeat(ws)                a ping every 20 seconds, and a connection silent for a minute dropped and made again
//    Close()                      drops it, when the person signs out
// 3. Say(id, text, chat, context) a message, run on the server as lingo(text), with the chat before it and what was
//                                 around it. Its reply comes back with id. False when not connected
// 4. Stop(id)                     stops that message's run on the server
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Onsay.Desktop;

static class Link
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    static ClientWebSocket? socket;
    static readonly SemaphoreSlim sending = new(1, 1);
    static bool looping;   // one connection loop at a time, even while it waits to reconnect

    public static bool Connected => socket?.State == WebSocketState.Open;

    ////////// 1. CONNECT //////////

    // 1. The approve page opens in their browser and this asks every two seconds until they press Connect. onCode gets the
    //    code that page shows, so they know it is theirs. Returns who signed in.
    public static async Task<string> Connect(Settings s, Action<string> onCode, CancellationToken stop)
    {
        s.Install ??= Guid.NewGuid().ToString("N");
        s.Save();
        var (status, start) = await Post("/connect", new JsonObject { ["name"] = Environment.MachineName, ["install"] = s.Install }, stop);
        if (status != 201) throw new Exception($"{start["error"] ?? $"the server answered {status}"}");
        onCode($"{start["code"]}");
        Native.Open($"{start["url"]}");
        while (true)
        {
            await Task.Delay(2000, stop);
            var (_, poll) = await Post("/connect/poll", new JsonObject { ["poll"] = $"{start["poll"]}" }, stop);
            if ($"{poll["status"]}" == "waiting") continue;
            if ($"{poll["status"]}" != "done") throw new Exception($"{poll["error"] ?? "that sign in did not finish"}");
            (s.Token, s.User) = ($"{poll["token"]}", $"{poll["account"]?["email"]}");
            s.Save();
            return s.User;
        }
    }

    ////////// 2. THE LIVE CONNECTION //////////

    /* 2. heard gets every message on the window's thread. A dropped connection comes back, slower each time. A sign out on
          the server closes with 4001, which forgets the token here too and sends heard { type: "signedout" }. */
    public static async void Start(Settings s, Action<JsonObject> heard)
    {
        if (looping) return;
        looping = true;
        var ui = System.Windows.Application.Current.Dispatcher;
        void Hear(JsonObject m) => ui.InvokeAsync(() => heard(m));
        try
        {
            for (int wait = 1000; s.Token is not null; wait = Math.Min(wait * 2, 30_000))
            {
                var ws = socket = new ClientWebSocket();
                try
                {
                    await ws.ConnectAsync(new Uri(Settings.Api.Replace("https://", "wss://").Replace("http://", "ws://") + "/live"), CancellationToken.None);
                    await Send(new JsonObject { ["type"] = "hello", ["token"] = s.Token });
                    wait = 1000;
                    (lastHeard, ponged) = (DateTime.UtcNow, false);
                    _ = Heartbeat(ws);
                    while (ws.State == WebSocketState.Open && await Receive(ws) is JsonObject m)
                    {
                        lastHeard = DateTime.UtcNow;
                        if ($"{m["type"]}" == "pong") ponged = true;
                        else Hear(m);
                    }
                }
                catch (Exception) { }
                socket = null;
                if (ws.CloseStatus == (WebSocketCloseStatus)4001)
                {
                    (s.Token, s.User) = (null, null);
                    s.Save();
                    Hear(new JsonObject { ["type"] = "signedout" });
                    return;
                }
                Hear(new JsonObject { ["type"] = "offline" });
                await Task.Delay(wait);
            }
        }
        finally { looping = false; }
    }

    // A connection can die silently (Wi-Fi change, sleep, a proxy), leaving a receive waiting forever, so silence is a drop.
    static DateTime lastHeard;
    static bool ponged;   // only a server that answers pings can be judged by its silence
    static async Task Heartbeat(ClientWebSocket ws)
    {
        while (ws.State == WebSocketState.Open)
        {
            await Task.Delay(20_000);
            if (ponged && DateTime.UtcNow - lastHeard > TimeSpan.FromSeconds(60)) { ws.Abort(); return; }
            await Send(new JsonObject { ["type"] = "ping" });
        }
    }

    public static void Close() => socket?.Abort();

    ////////// 3-4. SAYING AND STOPPING //////////

    /* 3. Nothing is kept by Onsay: the chat is only what the chat window shows, and it goes with each message, beside what
          was around it. */
    public static async Task<bool> Say(string id, string text, JsonArray chat, JsonObject context)
    {
        if (!Connected) return false;
        await Send(new JsonObject { ["type"] = "say", ["id"] = id, ["text"] = text, ["chat"] = chat, ["context"] = context });
        return true;
    }

    // 4. From any thread, since the stop key calls it from its own.
    public static async void Stop(string id) => await Send(new JsonObject { ["type"] = "stop", ["id"] = id });

    ////////// MESSAGES //////////

    static async Task<(int status, JsonObject reply)> Post(string path, JsonObject body, CancellationToken stop)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, Settings.Api + path) { Content = JsonContent.Create(body) };
        using var res = await Http.SendAsync(req, stop);
        JsonObject? reply = null;
        try { reply = JsonNode.Parse(await res.Content.ReadAsStringAsync(stop)) as JsonObject; } catch (System.Text.Json.JsonException) { }
        return ((int)res.StatusCode, reply ?? new JsonObject());
    }

    // Every message sent is kept in the request log, so the person can read what went.
    static async Task Send(JsonObject message)
    {
        var ws = socket;
        if (ws?.State != WebSocketState.Open) return;
        Requests.Keep(message.ToJsonString());
        await sending.WaitAsync();
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch (Exception) { }
        finally { sending.Release(); }
    }

    static async Task<JsonObject?> Receive(ClientWebSocket ws)
    {
        using var all = new System.IO.MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var got = await ws.ReceiveAsync(buffer, CancellationToken.None);
            if (got.MessageType == WebSocketMessageType.Close) return null;
            all.Write(buffer, 0, got.Count);
            if (got.EndOfMessage) break;
        }
        try { return JsonNode.Parse(all.ToArray()) as JsonObject; } catch (System.Text.Json.JsonException) { return new JsonObject(); }
    }
}
