// Updates.cs - keeping him up to date, once he is installed (release.ps1 makes the installer, with Velopack): a minute
// after he starts, and every six hours after, he looks for a newer version, downloads it quietly, and it is put in when
// he next quits, so he never restarts on you. A build that was never installed, like start.ps1's, does nothing here.
//
// 1. Start()    the checks, in the background
using Velopack;

namespace Onsay.Desktop;

static class Updates
{
    /* Where the releases are published, the folder release.ps1 uploads Releases\ to. Not decided yet: the repos are
       private, so it can't be their GitHub releases. Change it here and in release.ps1 together. */
    public const string Feed = "https://onsay.dev/download/windows";

    // 1. A check that fails, offline or with nothing published yet, is tried again next time.
    public static async void Start()
    {
        var updates = new UpdateManager(Feed);
        if (!updates.IsInstalled) return;
        await Task.Delay(TimeSpan.FromMinutes(1));
        while (true)
        {
            try
            {
                if (await updates.CheckForUpdatesAsync() is { } newer)
                {
                    await updates.DownloadUpdatesAsync(newer);
                    updates.WaitExitThenApplyUpdates(newer.TargetFullRelease, silent: true, restart: false);
                    return;
                }
            }
            catch (Exception) { }
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }
}
