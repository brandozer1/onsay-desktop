// Program.cs - Onsay on the desktop. One of him, sitting on the taskbar. Every crash is written down in crash.log, in
// %AppData%\Onsay, with its whole stack.
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Onsay.Desktop;

namespace Onsay;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // TLS 1.2 for every web request, which Windows 7 leaves off unless asked.
        System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

        // The uninstaller's turn first (src/System/Updates.cs): it stops him starting with Windows, and asks whether his data
        // goes too.
        if (args.Contains("--uninstalling"))
        {
            Updates.Uninstalling();
            return;
        }

        /* One of him. A second launch is somebody opening Onsay again, maybe after closing him, not asking for twins: it
           brings the first one back and ends. A newer version started by an update, with --restarted, waits for the one
           before it to end instead. A test, with a folder of its own in ONSAY_DATA, is one of him for that folder, so it
           runs beside yours. */
        var test = Environment.GetEnvironmentVariable("ONSAY_DATA") is { Length: > 0 } ? "." + Settings.Folder.Replace('\\', '/') : "";
        using var only = new Mutex(true, $@"Local\Onsay.Desktop{test}", out var first);
        using var again = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\Onsay.Desktop{test}.Again");
        if (!first && args.Contains("--restarted"))
        {
            try { first = only.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { first = true; }   // it ended without letting go, which hands it over all the same
        }
        if (!first)
        {
            again.Set();
            return;
        }

        /* An error nothing caught is written down. One on the UI thread is let go and he carries on, since vanishing is
           worse than one thing going wrong. One anywhere else ends the app, as .NET always does, written down first. */
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Crashed("on the UI thread, and he carried on", e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crashed("on another thread, which ended the app", e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) => Crashed("in a task nobody waited for, and he carried on", e.Exception);

        // So what the windows start as they are made comes back to the UI thread, the loop below not running yet.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var him = new BuddyWindow();
        ThreadPool.RegisterWaitForSingleObject(again, (_, _) => app.Dispatcher.BeginInvoke(() => him.Away(false)), null, -1, false);
        app.Run(him);
    }

    static void Crashed(string where, object error)
    {
        try { File.AppendAllText(Path.Combine(Settings.Folder, "crash.log"), $"{DateTime.Now:s} Onsay {Updates.Version} crashed {where}:\n{error}\n\n"); }
        catch { }
    }
}
