# Onsay Desktop

Onsay for Windows, in C# and WPF: a free, open source AI agent and employee. Customize, change, tweak and edit it
however you like: the character, the UI, the setup, the lingos it ships. It is Apache 2.0 licensed and built to be built
on.

It is two things:

- **The lingo client**, `onsay-core.exe`, from [lingo-client](https://github.com/brandozer1/lingo-client): a small
  program that answers web requests on port 6969, on this computer alone until its policy shares something. Each running
  lingo in its lingos folder, a `.lingo` file, is a program installed at its route, and `GET /` lists them all.
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

**He asks first.** When a lingo needs something that matters (your screen, a file outside his own folder, running a
program) and hasn't been allowed it, a small card shows beside him, like "Onsay wants to see your screen" or "agent wants
to read C:\Users\you\Documents\notes.txt", with Allow (from now on), Only this time and Don't allow. One question at a
time; one left unanswered is refused after a minute and its card goes. While the lingo client runs, the app asks it for
the questions every second (`/.lingo/asks`), which is how it knows someone is there, and answers through
`/.lingo/answer`. With the app closed, the lingo client refuses instead.

## Settings

| Page | What it has |
|------|-------------|
| General | starting with Windows, the version, Check for updates now, and Uninstall Onsay |
| Look | his colour, face and surface |
| Lingos | every lingo on this computer: on or off, what it does, its settings, or why it can't run |
| Permissions | the most his programs may ever do on this computer; the lingo client refuses anything that is off |
| Account | signing in and out |
| Requests | every request between this computer and Onsay, exactly as it went, your sign in token masked |

**Lingos.** The list comes from the lingo client itself (`GET /`): each lingo with a switch that starts or stops it, the
first sentence of what it does from its `openapi.json`, whether it comes with Onsay, and its settings. One that can't
load shows why instead. To add a lingo, put its `.lingo` file in the lingos folder (`%AppData%\Onsay\core\lingos`); it
starts off.

**What it ships.** Every `.lingo` file in `..\lingo-client\core\bin\lingos`: `windows`, `native`, `agent` (set your
Claude, ChatGPT or OpenRouter key in its settings) and the rest of the lingo library. Each time it starts the lingo
client, the app copies them into the lingos folder and removes what an older version left there for one, like a
`windows` folder, so none is there twice. The first time a shipped lingo appears, the app starts it, and gives `windows`
seeing the screen, using the mouse and keyboard and reading files, so he needn't ask for those every time. It remembers
which it started (`Started` in `desktop.json`), so one you turn off stays off.

**Settings stay on this computer.** A lingo's settings form is built from what its `openapi.json` declares, and the app
reads and saves them through the lingo client (`settings`, `set`), wherever the lingo keeps them, never on Onsay's
servers.

**Permissions** are the ceiling in the lingo client's `policy.json`: anything off, no lingo here can do.

**How it talks to the lingo client.** Every change goes to its control routes, `http://127.0.0.1:6969/.lingo/<verb>`
(`start`, `stop`, `asks`, `answer`, `settings`, `set`, `policy`, `grant`, `ceiling`), or to its command line,
`onsay-core core.json <verb> ...`, when it isn't answering (`Core.Control`).

**Signed in or not.** The lingo client runs either way, signed out bare. Signed in, `core.json` has the same token, so it
keeps its own connection to Onsay and Onsay can reach its lingos. `core.json` holds only that token, this computer's name
and its home folder. Signing in or out starts it again.

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
| `src/Core/` | `Core.cs` runs the lingo client, ships its lingos, lists them and makes every control call; `Link.cs` signs in through the website and holds the chat's connection to Onsay |
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat and the look every window shares (`ChatWindow`), replies drawn as Markdown (`Markdown`), setting him up (`SetupWindow`), settings (`SettingsWindow`), a lingo asking first (`AskWindow`), his pointer (`HandWindow`), the ring (`RingWindow`) and his panel (`PanelWindow`) |
| `src/System/` | Windows itself: Win32 calls (`Native`), docking maths (`Dock`), the stop key (`StopKey`), what the lingo client asks to be drawn (`Overlay`), the app's settings and starting with Windows (`Settings`), updates (`Updates`) and the request log (`Requests`) |
| `src/Character/` | his face as data (`Face`) and the view that animates it (`OnsayView`) |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Part of Onsay, with the lingo client
(https://github.com/brandozer1/lingo-client) and Onsay IO (https://github.com/brandozer1/onsay-io).
