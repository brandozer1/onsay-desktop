# Onsay for Windows

The Onsay app for Windows, in C# and WPF. It is the Onsay core plus some programming on the native platform it is made
for, Windows, that connects to `windows`, the device lingo dedicated to talking to this program, so the computer it is
on can be controlled and operated through the INS (the Identity Name System, Onsay's names: one top level name per
person, their handle, with a route for each of their computers, like `laptop.brandon`).

Onsay Desktop is a free, open source AI agent and employee. Customize, change, tweak and edit it however you like: the
character, the UI, the walkthrough, the lingos it ships, the routes it opens. It is Apache 2.0 licensed and built to be
built on.

The core is the lingo client (https://github.com/brandozer1/lingo-client), and the `windows` lingo ships with it. This
app ships the lingo client's build, `onsay-core.exe`, and the lingos it builds. In this repo it is bundled from a
release of that project (during local development, a sibling checkout at `..\lingo-client\core\bin` also works).

## What he does

He sits on the taskbar and watches your cursor. Click him to chat. Alt+Shift+O stops him at any time, from any app.
The first time, he opens the chat himself and walks you through who he is, what he is for, how to stop him and what
this computer sends to Onsay, agreed with one press, then signs you in.

Every request between this computer and Onsay is kept in a log you can read, under Settings, Requests: the app's own
messages and the core's, each exactly as it went, your sign in token the one thing masked. The prompts you send, what
this computer can do, and what each lingo asks for and answers as it runs (`src/System/Requests.cs`,
`%AppData%\Onsay\requests.log`). Under Settings, Routes are the lingos installed here, each a route Onsay can reach at
its address under your handle, like `laptop.brandon/lingos/weather.lingo`.

His replies are drawn as Markdown: headings, lists, quotes, tables, links and code, each code block with a Copy button,
and Copy under every reply for the whole of it. When a page says more than words, a lingo can open his panel, a small
rounded window of his own showing a page of HTML, with the `windows` lingo's `panel.show(html, title, width, height)`.

The app is the friendly part: his character, the chat, signing in, and permissions. Each message you type goes to
Onsay, which answers it and sends the reply straight back. Nothing of the
chat is kept there: it lives only in the chat window, and the new chat button starts over. Each message goes with the
chat before it, its newest 40 turns that come to at most 20,000 characters, oldest dropped first, and with what was
around it: this computer's time, zone and language, and the app and window you were in. Both are kept nowhere but that
message's 7 day trace.

His hands are the core, which runs the lingos in its lingos folder, `%AppData%\Onsay\core\lingos`, when Onsay
relays a call to one, like `laptop.brandon/lingos/test.lingo/find`. The app ships `windows` and `agent`, the AI agent (set
your Claude, ChatGPT or OpenRouter key in Settings, Routes, agent), and puts them in that folder each time it starts the
core. Right click him and choose Open lingos folder to add your own, like the lingos in the lingo client's
`lingo-library`: a folder with its `lingo.json` and its program, or one `.lingo` file. Every lingo there runs on this
computer, and the core tells Onsay what each one does, never its code. What lingos remember is kept here, in the core's
store, and so are their settings: never on Onsay's servers. Settings, Routes shows each lingo's settings as its
`lingo.json` declares them, and saves them through the core, wherever the lingo keeps them.

## Building

```
..\lingo-client\core\build.ps1   builds the core and its lingos first, in the lingo-client project beside this one
.\start.ps1                 builds and starts the app. Needs the .NET 7 SDK
.\release.ps1 0.2.0         a release: the installer and its updates, in Releases\. Needs vpk, see Releasing
```

## Releasing

`release.ps1` builds the core and the app, then packs them with Velopack into `Releases\`: `Onsay-win-Setup.exe`, the
installer people download, and the packages installed copies update from. The installer puts him in
`%LocalAppData%\Onsay` for that person alone, with no admin rights, adds him to the Start menu and desktop, and opens
him. Installed, he looks for a newer version a minute after starting and every six hours, downloads it quietly, and it
goes in when he next quits (`src/System/Updates.cs`). Settings, General shows the version, a Check for updates now
button, and Uninstall Onsay, which removes him, his shortcuts and his startup, the same uninstaller as Windows'
Installed apps. The updater itself almost never changes: Velopack installs a tiny separate program that applies updates,
so a new version is a normal update, never a reinstall.

To release: `dotnet tool install -g vpk` once (it needs .NET 8), then `.\release.ps1 0.2.0`, and upload everything in
`Releases\` to the feed, `Feed` in `src/System/Updates.cs`, keeping the files already there. Pushing a tag like `v0.2.0`
does the same on GitHub (`.github/workflows/release.yml`, with the secret `ONSAY_REPO_TOKEN` to read the onsay repo),
keeping `Releases\` as the run's download. Where the feed lives is not decided yet.

The app talks to `https://api.onsay.dev`. To point it at another server while developing, set `ONSAY_API` before
starting it, like `$env:ONSAY_API = "http://localhost:3000"`.

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
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat and its walkthrough (`ChatWindow`), his replies drawn as Markdown (`Markdown`), settings (General, Look, Routes, Permissions, Account, Requests), his pointer (`HandWindow`), the ring (`RingWindow`), his panel showing a page (`PanelWindow`), setting him up the first time he opens, his look included (`SetupWindow`), and the tray icon |
| `src/System/` | Windows itself: Win32 calls, docking maths, the stop key, the core's overlay messages, the app's settings, updates (`Updates`), and the request log (`Requests`) |
| `src/Core/` | `Core.cs` runs the core, `Link.cs` signs in through the website and holds the live connection to the server |
| `src/Character/` | the character: his face as data, and the view that animates it |

## Using it

Signing in: right click him and choose Sign in to Onsay. The website opens, you name this computer, its Windows name
suggested, and approve, and the app gets its own token. The home screen shows the route it goes by, "This computer is
laptop.brandon". The core signs in with the same token. Permissions under the chat say what his programs may do on this
computer, and the core refuses anything that needs one not ticked.

Settings, in his right click menu and the chat's menu, has it all on pages: starting with Windows, his look, each
lingo's settings under Routes, permissions and your account. What a lingo asks for, like a key, is kept on this
computer (see the lingo client's `core/README.md`, Lingo settings).

Everything he keeps is in `%AppData%\Onsay`: `desktop.json` for the app, `core.json` and `core.log` for the core, and
`core\` for the core's own storage, `panel\` for his panel's browser storage, and `requests.log`, every request between
this computer and Onsay.

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Part of Onsay, with the lingo client
(https://github.com/brandozer1/lingo-client) and Onsay IO (https://github.com/brandozer1/onsay-io).
