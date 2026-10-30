// Settings.cs - what he remembers between runs, in desktop.json in his folder, and starting with Windows.
//
// 1. Settings.Load()     reads the file, or the defaults when there is none
// 2. settings.Save()     writes it back
// 3. Startup.IsOn        whether he starts with Windows
// 4. Startup.Set(on)     the per user Run key, so no installer and no admin rights
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Onsay.Desktop;

sealed class Settings
{
    ////////// FIELDS //////////

    // Where he sits: Along is 0 to 1 along the taskbar, 1 the tray end. MonitorX/Y is a point on his monitor, null primary.
    public double Along { get; set; } = 1;
    public int? MonitorX { get; set; }
    public int? MonitorY { get; set; }
    // Dropped somewhere off the taskbar, he stays there: the top left of his window, in real pixels. Null is on the bar.
    public int? FreeX { get; set; }
    public int? FreeY { get; set; }

    // The lingos this app ships that it has started once (Core.cs), so one the person stops stays stopped.
    public List<string>? Started { get; set; }
    // Whether setting him up, the window the first open shows (SetupWindow.cs), was finished or skipped. Null until then.
    // Welcomed is an older version's name for it, read so nobody who finished that is set up again.
    public bool? SetUp { get; set; }
    public bool? Welcomed { get => null; set { if (value == true) SetUp = true; } }
    // How he looks, picked in that window or Settings, Look. Null is the brand's own look.
    public Onsay.Character.OnsayView.Appearance? Look { get; set; }
    // Let me choose, on the chat: true asks the person for every pick he makes (list mode). Null is off.
    public bool? LetMeChoose { get; set; }
    // Whether a message's steps stay open under it once it is answered, rather than folded under one line.
    public bool? ShowSteps { get; set; }

    // His folder, %AppData%\Onsay. A test sets ONSAY_DATA to a folder of its own before he starts, so it never touches the
    // person's.
    public static readonly string Folder = Environment.GetEnvironmentVariable("ONSAY_DATA") is { Length: > 0 } test ? test
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Onsay");
    static readonly string File_ = Path.Combine(Folder, "desktop.json");

    ////////// FUNCTIONS //////////

    // 1.
    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(File_)) ?? new(); }
        catch { return new(); }
    }

    // 2. Forgetting where he sat is not worth crashing over.
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(File_, JsonSerializer.Serialize(this, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }));
        }
        catch { }
    }
}

static class Startup
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "Onsay";

    // 3.
    public static bool IsOn
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(Key); return k?.GetValue(Name) is string; }
    }

    // 4.
    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
}
