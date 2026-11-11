// Screens.cs - the screen lingo, one of the app's own lingos (Host.cs): canvases a lingo draws on with Canvas 2D
// instructions, kept here with the to-do they are for, shown in the to-do list and the Screens window (ScreensWindow.cs)
// and drawn here (Drawing.cs). Never video, and nothing drawn reaches the web.
//
// 1. All, Of(session)               every open screen, oldest first, or those of one to-do, and Get(id) one by its id
//    Changed(id)                    raised on the UI thread: with an id when only what that screen shows changed, so
//                                   only its pictures need painting again, and null when one opened or closed
// 2. Open(session, title)           the screen lingo's calls, each its answer, or why not in words
//    Draw(id, instructions)
//    Show(id, picture)
//    Close(id)                      also the person closing one
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Onsay.Desktop;

// A screen: its picture as drawn here, null before anything is drawn on it, its size, and its instructions since its
// last clear.
sealed record Screen(long Id, string Session, string Title, ImageSource? Picture, double Width, double Height, List<JsonNode?> Drawn);

static class Screens
{
    const int Most = 16;                 // open at once, the oldest let go for a new one
    const long MostDrawn = 16 << 20;     // bytes of instructions since a screen's last clear

    static readonly Dictionary<long, Screen> open = new();
    static long made;

    ////////// 1. WHAT IS OPEN //////////

    // 1.
    public static IReadOnlyList<Screen> All
    {
        get { lock (open) return open.Values.OrderBy(s => s.Id).ToList(); }
    }
    public static IReadOnlyList<Screen> Of(string session) => All.Where(s => s.Session == session).ToList();
    public static Screen? Get(long id)
    {
        lock (open) return open.GetValueOrDefault(id);
    }
    public static event Action<long?>? Changed;

    static void Tell(long? id) => Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke(id));

    ////////// 2. THE SCREEN LINGO //////////

    // 2. One of that title already open for the to-do is given back, so a lingo called again and again draws on one.
    public static long Open(string session, string title)
    {
        long id;
        lock (open)
        {
            var same = open.Values.FirstOrDefault(s => s.Session == session && s.Title == title);
            if (same is not null) return same.Id;
            if (open.Count >= Most) open.Remove(open.Keys.Min());
            id = ++made;
            open[id] = new Screen(id, session, title, null, 0, 0, new());
        }
        Tell(null);
        return id;
    }

    /* 2. Each instruction a record with one key, like {fillRect: [0, 0, 10, 10]}. A clear starts it over at a size, and
          anything else draws after what was drawn before. drawImage takes only a picture inside it, a data:image/ address. */
    public static string? Draw(long id, JsonNode? instructions)
    {
        if (instructions is not JsonArray list) return "Draw a list of instructions, like [{\"fillRect\": [0, 0, 10, 10]}]";
        var batch = new List<JsonNode?>();
        bool fresh = false;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is not JsonObject o || o.Count != 1) return $"Instruction {i + 1} isn't one: each is a record with one key, like {{\"fillRect\": [0, 0, 10, 10]}}";
            var (key, value) = o.First();
            if (key == "drawImage" && !$"{(value as JsonArray)?.ElementAtOrDefault(0)}".StartsWith("data:image/"))
                return "drawImage takes a picture inside it, a data:image/ address, never one on the web";
            if (key == "clear")
            {
                batch.Clear();
                fresh = true;
            }
            batch.Add(o.DeepClone());
        }
        Screen? was = Get(id);
        if (was is null) return $"There's no screen {id} open: it was closed, or never opened";
        var drawn = fresh ? batch : was.Drawn.Concat(batch).ToList();
        if (drawn.Sum(d => (long)(d?.ToJsonString().Length ?? 0)) > MostDrawn) return "That is more than 16 MB drawn since the screen's last clear. Start again with a clear";
        var (picture, width, height) = Drawing.Draw(drawn);
        lock (open)
        {
            if (!open.ContainsKey(id)) return $"There's no screen {id} open: it was closed, or never opened";
            open[id] = was with { Picture = picture, Width = width, Height = height, Drawn = drawn };
        }
        Tell(id);
        return null;
    }

    // 2. A clear the picture's size and the picture drawn whole. A data: address or plain base64.
    public static string? Show(long id, string picture)
    {
        int comma = picture.IndexOf(',');
        var data = picture.StartsWith("data:") && comma > 0 ? picture.Substring(comma + 1) : picture;
        int width, height;
        string kind;
        try
        {
            using var bytes = new MemoryStream(Convert.FromBase64String(data));
            var decoder = BitmapDecoder.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            if (decoder is not (PngBitmapDecoder or JpegBitmapDecoder)) throw new FormatException();
            kind = decoder is PngBitmapDecoder ? "png" : "jpeg";
            width = decoder.Frames[0].PixelWidth;
            height = decoder.Frames[0].PixelHeight;
        }
        catch (Exception) { return "A screen shows a PNG or JPEG picture, as base64"; }
        return Draw(id, new JsonArray(
            new JsonObject { ["clear"] = new JsonArray(width, height) },
            new JsonObject { ["drawImage"] = new JsonArray($"data:image/{kind};base64,{data}", 0, 0, width, height) }));
    }

    // 2.
    public static void Close(long id)
    {
        bool gone;
        lock (open) gone = open.Remove(id);
        if (gone) Tell(null);
    }
}
