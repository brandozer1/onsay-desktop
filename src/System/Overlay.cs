// Overlay.cs - what the core asks to be drawn while Onsay acts, read from its output a line of JSON at a time (Core.cs).
// The windows lingo writes them, see ../onsay/lingos/windows/windows.c, the hand:
//   {"show":"glide","from":[x,y],"to":[x,y],"ms":n,"bend":b,"real":r}   his pointer glides there along that curve,
//                                                               the real mouse with it when r
//   {"show":"press","button":"left","times":1}                  it clicks
//   {"show":"point","x":..,"y":..,"w":..,"h":..,"say":"..","seconds":n}   a ring around that box, him beside it
//   {"show":"clear"}                                            the ring goes
// Positions are real pixels on the whole desktop. Both windows are made once, on first use, and kept.
//
// 1. Line(text)    one line from the core, on any thread
// 2. Hand          where his pointer is while it shows, for him to watch instead of the mouse
// 3. Rest()        the core stopped or restarted: the pointer fades where it is and the ring goes
using System.Text.Json;
using System.Windows;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

static class Overlay
{
    static HandWindow? hand;
    static RingWindow? ring;

    // 1.
    public static void Line(string text)
    {
        JsonElement m;
        try { m = JsonDocument.Parse(text).RootElement; } catch (JsonException) { return; }
        Application.Current?.Dispatcher.BeginInvoke(() => Handle(m));
    }

    static POINT Pair(JsonElement p) => new(p[0].GetInt32(), p[1].GetInt32());
    static int Int(JsonElement m, string key) => m.TryGetProperty(key, out var v) && v.TryGetInt32(out var n) ? n : 0;

    static void Handle(JsonElement m)
    {
        switch (m.TryGetProperty("show", out var what) ? what.GetString() : null)
        {
            case "glide":
                _ = (hand ??= new HandWindow()).Glide(Pair(m.GetProperty("from")), Pair(m.GetProperty("to")), Int(m, "ms"), m.GetProperty("bend").GetDouble(),
                    m.TryGetProperty("real", out var real) && real.ValueKind == JsonValueKind.True);
                break;
            case "press":
                (hand ??= new HandWindow()).Press(Int(m, "times"));
                break;
            case "point":
                int x = Int(m, "x"), y = Int(m, "y");
                (ring ??= new RingWindow()).Point(new RECT { Left = x, Top = y, Right = x + Int(m, "w"), Bottom = y + Int(m, "h") },
                    m.TryGetProperty("say", out var say) ? say.GetString() ?? "" : "", m.TryGetProperty("seconds", out var s) ? s.GetDouble() : 10);
                break;
            case "clear":
                ring?.Clear();
                break;
        }
    }

    // 2.
    public static POINT? Hand => hand is { Awake: true } ? hand.At : null;

    // 3.
    public static void Rest() => Application.Current?.Dispatcher.BeginInvoke(() => { hand?.Rest(); ring?.Clear(); });
}
