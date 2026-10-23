// Program.cs - Onsay on the desktop. One of him, sitting on the taskbar.
using System.Windows;
using Onsay.Desktop;
using Velopack;

namespace Onsay;

static class Program
{
    [STAThread]
    static void Main()
    {
        // The installer's and updater's turn first (src/System/Updates.cs). Uninstalling also stops him starting with Windows.
        VelopackApp.Build().OnBeforeUninstallFastCallback(_ => Startup.Set(false)).Run();

        // One of him. A second launch is somebody double clicking the shortcut again, not asking for twins.
        using var only = new Mutex(true, @"Local\Onsay.Desktop", out var first);
        if (!first) return;

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new BuddyWindow());
    }
}
