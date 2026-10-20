// Settings.cs - what he remembers between runs, in %AppData%\Onsay\desktop.json, and starting with Windows.
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

    // This app's sign in to Onsay, from Sign in to Onsay in his right click menu, and who it is. Null is signed out. The core
    // signs in with the same token, see Core.cs.
    public string? Token { get; set; }
    public string? User { get; set; }
    // What his programs may do on this computer, as Lingo permissions, from Permissions under the chat. Null is the defaults
    // in Core.cs. The core refuses any phrase that needs one not listed.
    public List<string>? Allow { get; set; }
    // Whether the steps under his replies start open. Null is closed, so the chat reads as messages first.
    public bool? ShowSteps { get; set; }
    // This app's own id for itself, made the first time it signs in, so signing in again is the same instance on the server.
    public string? Install { get; set; }

    // Onsay's server. Built in, never a setting. A debug build can point at another with ONSAY_API, for development.
#if DEBUG
    [JsonIgnore] public static string Api => (Environment.GetEnvironmentVariable("ONSAY_API") ?? "https://api.onsay.dev").TrimEnd('/');
#else
    [JsonIgnore] public static string Api => "https://api.onsay.dev";
#endif

    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Onsay");
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
