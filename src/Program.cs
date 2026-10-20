// Program.cs - Onsay on the desktop. One of him, sitting on the taskbar.
using System.Windows;
using Onsay.Desktop;

namespace Onsay;

static class Program
{
    [STAThread]
    static void Main()
    {
        // One of him. A second launch is somebody double clicking the shortcut again, not asking for twins.
        using var only = new Mutex(true, @"Local\Onsay.Desktop", out var first);
        if (!first) return;

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Run(new BuddyWindow());
    }
}
