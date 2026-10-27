# Onsay Desktop

Onsay for Windows, in C# and WPF: a free, open source AI agent and employee. Customize, change, tweak and edit it
however you like: the character, the UI, the setup, the lingos it ships. It is Apache 2.0 licensed and built to be built
on.

It is two things:

- **The lingo client**, `onsay-core.exe`, from [lingo-client](https://github.com/brandozer1/lingo-client): a small
  program that answers web requests on port 6969, from this computer only by default. Each lingo in its lingos folder is
  a program installed at its route, `/<name>` or the `route` its `lingo.json` names, and `GET /` lists them. A lingo
  whose `lingo.json` says `"open": true` can also be reached from outside this computer; the rest answer this computer
  alone.
- **The app around it**: his character on the taskbar, the chat, signing in, setting him up, and settings.

## Setting him up

The first time he opens, a window walks through it one step at a time: hello, his look, where to find him (desktop and
Start menu shortcuts, starting with Windows), what is sent to Onsay (Agree and continue), and signing in, which can be
skipped. Signing in opens the website, where you name this computer and approve, and the app gets its own token. Then
his chat opens.

## Using him

He sits on the taskbar and watches your cursor. Click him to chat. Alt+Shift+O stops him at any time, from any app.
While a game, film or presentation has the whole screen he steps aside; the stop key still works, and he comes back when
it ends. Right click him for Open chat, Settings, Open lingos folder, Put him back and Quit.

Each message you type goes to Onsay over the app's own connection, and the reply comes straight back. Nothing of the
chat is kept there: it lives only in the chat window, and the new chat button starts over. Each message goes with the
chat before it, its newest 40 turns that come to at most 20,000 characters, and with what was around it: this
computer's time, zone and language, and the app and window you were in.

His replies are drawn as Markdown, with Copy under every reply and on every code block. A lingo can open his panel, a
small window showing a page of HTML, with the `windows` lingo's `panel.show(html, title, width, height)`.

## Settings

| Page | What it has |
|------|-------------|
| General | starting with Windows, the version, Check for updates now, and Uninstall Onsay |
| Look | his colour, face and surface |
| Lingos | every lingo on this computer, with its settings |
| Permissions | what his programs may do on this computer; the lingo client refuses anything that is off |
| Account | signing in and out |
| Requests | every request between this computer and Onsay, exactly as it went, your sign in token masked |

**Lingos.** Each lingo shows its name, what it does, whether it comes with Onsay, its address on this computer, like
`http://localhost:6969/agent`, and its settings. The list comes from the lingo client itself (`GET /`), so a route its
`lingo.json` names is the one shown. To add a lingo, put it in the lingos folder (`%AppData%\Onsay\core\lingos`): a
folder with its `lingo.json` and its program, like the lingos in the lingo client's `lingo-library`, or one `.lingo`
file. The app ships `windows` and `agent`, the AI agent (set your Claude, ChatGPT or OpenRouter key in its settings),
and puts them there each time it starts the lingo client.

**Settings stay on this computer.** A lingo's settings form is built from what its `lingo.json` declares, and the app
reads and saves them through the lingo client (`--settings`, `--set`), wherever the lingo keeps them, never on Onsay's
servers.

**Signed in, open lingos are reachable from anywhere.** The lingo client runs whether or not you are signed in. Signed
out it runs bare, for this computer alone. Signed in, the app gives it the same token and `inbound: "onsay"`, so
Onsay's proxy passes web requests down to it, and an open lingo also shows its proxy address, like
`https://api.onsay.dev/proxy/laptop.brandon/weather`. Signing in or out starts it again with the new settings.

## Files

Everything he keeps is in `%AppData%\Onsay`: `desktop.json` for the app, `core.json` and `core.log` for the lingo
client, `core\` for its lingos folder and storage, `panel\` for his panel's browser storage, and `requests.log`.

## Building

```
..\lingo-client\core\build.ps1   builds the lingo client and its lingos, in the lingo-client project beside this one
.\start.ps1                       builds and starts the app. Needs the .NET 8 SDK
.\release.ps1 0.2.0               a release in Releases\. Needs vpk: dotnet tool install -g vpk
```

The app talks to `https://api.onsay.dev`. A debug build can point at another server with `ONSAY_API`, like
`$env:ONSAY_API = "http://localhost:3000"`.

## Releasing

Push a tag like `v0.2.0`, or run the release workflow from the Actions tab with a version. GitHub builds the lingo
client and the app, packs them with Velopack (`release.ps1`), and puts them on this repo's release page:
`Onsay-win-Setup.exe`, the portable zip, and the packages installed copies update from. It needs no secrets.

The installer puts him in `%LocalAppData%\Onsay` for that person alone, with no admin rights, adds him to the Start menu
and desktop, and opens him. Installed copies look at this repo's GitHub Releases a minute after starting and every six
hours, download a newer version quietly, and it goes in when he next quits (`src/System/Updates.cs`).

## Layout

| File | Job |
|------|-----|
| `Onsay.csproj` | the build, with the lingo client and its lingos copied in from `..\lingo-client` |
| `app.manifest` | tells Windows the app is per monitor DPI aware |
| `onsay.ico`, `splash.png` | his face as the app and installer icon, and what the installer shows while it copies |
| `start.ps1` | builds and starts the app, closing a running one first |
| `release.ps1` | a release: the installer and update packages, in `Releases\` |
| `.github/workflows/release.yml` | the same on GitHub, for a pushed version tag |
| `src/Program.cs` | the entry point: one of him, sitting on the taskbar |
| `src/Core/` | `Core.cs` runs the lingo client, lists its lingos and reads and writes their settings; `Link.cs` signs in through the website and holds the chat's connection to Onsay |
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat and the look every window shares (`ChatWindow`), replies drawn as Markdown (`Markdown`), setting him up (`SetupWindow`), settings (`SettingsWindow`), his pointer (`HandWindow`), the ring (`RingWindow`) and his panel (`PanelWindow`) |
| `src/System/` | Windows itself: Win32 calls (`Native`), docking maths (`Dock`), the stop key (`StopKey`), what the lingo client asks to be drawn (`Overlay`), the app's settings and starting with Windows (`Settings`), updates (`Updates`) and the request log (`Requests`) |
| `src/Character/` | his face as data (`Face`) and the view that animates it (`OnsayView`) |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Part of Onsay, with the lingo client
(https://github.com/brandozer1/lingo-client) and Onsay IO (https://github.com/brandozer1/onsay-io).
