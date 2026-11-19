# Onsay Desktop

Onsay for Windows, in C# and WPF: a free AI agent and employee. It is Apache 2.0 licensed and built to be built on.

It is two things:

- **The app:** his character on the taskbar, the chat and its to-dos, setting him up, and a few settings.
- **The port** (`port/`): Onsay's port for Node ([onsay-ports](../onsay-ports/node)), which makes this computer a
  device of your Onsay account. It brings what runs here up to a web interface Onsay can call, the shell first, checks
  every call at its door and logs it raw: what went in and what came out. That log goes to Onsay, sealed, and is how the
  lingo search learns what works.

Everything from before, the lingo client, screens and his pointer, is on the `archive` branch.

## Setting him up

The first time he opens, a window walks through it one step at a time: hello, his look, where to find him, what is sent
to Onsay (Agree and continue), his AI, and adding this computer to your account, which can be skipped. Then his chat
opens.

**Adding this computer** (`src/Core/Core.cs`): the app makes this computer's device key itself, its private half sealed
for your Windows user and never sent anywhere, and opens onsay.dev's Devices page with the public half filled in. Press
Add this computer there and you're done: the port's next post to Onsay is answered with who this computer is. Sign out
forgets the key, so it is no device until it is added again.

## Using him

He sits on the taskbar and watches your cursor. Click him to chat. Alt+Shift+O stops him at any time, from any app.
While something has the whole screen he steps aside. Right click him for Open chat, Settings, Put him back and Close
Onsay. Closed, he goes to the notification area by the clock and Onsay keeps running, so your devices can still use this
computer. Quit Onsay, from there or in Settings, stops it all.

Each to-do is asked of Onsay through the port (`POST /onsay/ask`, `src/Core/ToDos.cs`): Onsay finds what can do it, runs
it, and the answer comes back to the chat.

## Files

Everything he keeps is in `%AppData%\Onsay`: `desktop.json` for the app, `todos.json` for your to-dos, `keys\device.key`
for this computer's key, `port\` for the port (`local.key`, `policy.json`, `me.json` and `requests.log`), `port.log`
for what the port says, and `crash.log`, every error nothing caught. A test sets `ONSAY_DATA` to a folder of its own
before he starts, so it never touches yours.

## Building

```
cd port && npm install     the port, from ../onsay-ports beside this one
.\start.ps1                builds and starts the app. Needs the .NET SDK and Node
.\release.ps1 0.27.0       a release in Releases\: the installer and the files updates come from. Also needs zig
                           and Inno Setup 6, and downloads Node for 64 and 32-bit Windows
sh release.sh upload 0.27.0   puts a release made here in Onsay's storage (keys in .env, never in git)
```

The installer puts him in `%LocalAppData%\Onsay` with no admin rights, on Windows 10 and later, the oldest Node runs on.
Installed, he checks Onsay every hour for a newer release, checks Onsay signed it, downloads only the files that changed,
checks each one, and goes in by himself the next time he isn't busy (`src/System/Updates.cs`). Update now and Uninstall
are in Settings.

## Layout

| File | Job |
|------|-----|
| `Onsay.csproj` | the build, with `port\` copied beside the app |
| `app.manifest` | tells Windows the app is per monitor DPI aware |
| `onsay.ico` | his face as the app's icon |
| `fonts/` | the website's fonts, Gabarito and IBM Plex, which every window uses, with their license (`OFL.txt`) |
| `start.ps1` | builds and starts the app, closing a running one first |
| `release.ps1` | a release: the installer and the files updates come from, in `Releases\`, on Windows or Linux |
| `release.sh` | a release made on a server in a throwaway container and put in Onsay's storage, or one made here uploaded |
| `setup.iss` | the installer, for Inno Setup: one for 32 and 64-bit Windows 10 and later |
| `launcher.c`, `launcher.rc` | `Onsay.exe` in the install folder, starting the version `version.txt` names, with his icon |
| `splash.png` | what the installer shows while it puts him in |
| `port/` | Onsay's port for Node, installed with npm |
| `src/Program.cs` | the entry point: one of him, sitting on the taskbar |
| `src/Core/Core.cs` | runs the port, this computer's key, and adding it to your account |
| `src/Core/ToDos.cs` | your to-dos, each asked of Onsay |
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat and the look every window shares (`ChatWindow`), replies drawn as Markdown (`Markdown`), setting him up (`SetupWindow`) and settings (`SettingsWindow`) |
| `src/System/` | Windows itself: Win32 calls (`Native`), docking maths (`Dock`), the stop key (`StopKey`), the app's settings and starting with Windows (`Settings`), updates and removing him (`Updates`), Ed25519 keys (`Ed25519`), and what .NET Framework 4.8 lacks (`Compat`) |
| `src/Character/` | his face as data (`Face`) and the view that animates it (`OnsayView`) |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
