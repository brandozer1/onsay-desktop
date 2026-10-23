# Onsay for Windows

The Onsay app for Windows, in C# and WPF. It is the Onsay core plus some programming on the native platform it is made
for, Windows, that connects to `windows`, the device lingo dedicated to talking to this program, so the computer it is
on can be controlled and operated through the INS (the Identity Name System, Onsay's names: one top level name per
person, their handle, with a route for each of their computers, like `laptop.brandon`).

The core and the `windows` lingo live in the onsay project, which this one sits beside: `..\onsay`. This app ships the
core built there, `..\onsay\core\bin\onsay-core.exe`, and the lingos it builds, `..\onsay\core\bin\lingos`.

## What he does

He sits on the taskbar and watches your cursor. Click him to chat. Alt+Shift+O stops him at any time, from any app.
The first time, he opens the chat himself and walks you through who he is, what he is for, how to stop him and what
this computer sends to Onsay, agreed with one press, then signs you in.

Everything this computer sends to Onsay is kept in a log you can read, under Settings, Privacy: the app's own messages
and the core's, each exactly as it went, your sign in token the one thing masked. What you type goes, and what this
computer can do; of the calls a lingo makes only their shape goes, never a value (`src/System/Privacy.cs`,
`%AppData%\Onsay\sent.log`).

His replies are drawn as Markdown: headings, lists, quotes, tables, links and code, each code block with a Copy button,
and Copy under every reply for the whole of it. When a page says more than words, a lingo can open his panel, a small
rounded window of his own showing a page of HTML, with the `windows` lingo's `panel.show(html, title, width, height)`.

The app is the friendly part: his character, the chat, signing in, and permissions. Each message you type goes to
Onsay's server, which runs it as `lingo(words, {prompt: words, chat})` and sends the reply straight back. Nothing of the
chat is kept there: it lives only in the chat window, and the new chat button starts over. Each message goes with the
chat before it, its newest 40 turns that come to at most 20,000 characters, oldest dropped first, and with what was
around it: this computer's time, zone and language, and the app and window you were in. Both are kept nowhere but that
message's 7 day trace (see `..\onsay\docs\lingo-function.md`, the context).

His hands are the core, which runs the lingos in its lingos folder, `%AppData%\Onsay\core\lingos`, when Onsay
relays a call to one, like `laptop.brandon/lingos/test.lingo/find`. The app ships only `windows`, and puts it in that
folder each time it starts the core. Right click him and choose Open lingos folder to add your own, like the `.lingo` files in
`..\onsay\lingo-library`, each one file: every lingo there runs on this computer, and the core tells Onsay what each one does, never
its code. What lingos remember is kept here, in the core's store. Their settings, like keys, are on your account
instead, set on the dashboard.

## Building

```
..\onsay\core\build.ps1     builds the core and its lingos first, in the onsay project beside this one
.\start.ps1                 builds and starts the app. Needs the .NET 7 SDK
.\release.ps1 0.2.0         a release: the installer and its updates, in Releases\. Needs vpk, see Releasing
```

## Releasing

`release.ps1` builds the core and the app, then packs them with Velopack into `Releases\`: `Onsay-win-Setup.exe`, the
installer people download, and the packages installed copies update from. The installer puts him in
`%LocalAppData%\Onsay` for that person alone, with no admin rights, adds him to the Start menu and desktop, and opens
him. He is uninstalled from Windows' Installed apps like any app. Installed, he looks for a newer version a minute after
starting and every six hours, downloads it quietly, and it goes in when he next quits (`src/System/Updates.cs`).

To release: `dotnet tool install -g vpk` once (it needs .NET 8), then `.\release.ps1 0.2.0`, and upload everything in
`Releases\` to the feed, `Feed` in `src/System/Updates.cs`, keeping the files already there. Pushing a tag like `v0.2.0`
does the same on GitHub (`.github/workflows/release.yml`, with the secret `ONSAY_REPO_TOKEN` to read the onsay repo),
keeping `Releases\` as the run's download. Where the feed lives is not decided yet.

The app talks to `https://api.onsay.dev`. To point it at another server while developing, set `ONSAY_API` before
starting it, like `$env:ONSAY_API = "http://localhost:3000"`.

## Layout

| File | Job |
|------|-----|
| `Onsay.csproj` | the build, with the core and its lingos copied in from `..\onsay\core\bin` |
| `app.manifest` | tells Windows the app is per monitor DPI aware |
| `start.ps1` | builds and starts the app, closing a running one first |
| `release.ps1` | a release: the installer and update packages, in `Releases\` |
| `.github/workflows/release.yml` | the same on GitHub, for a pushed version tag |
| `src/Program.cs` | the entry point: one of him, sitting on the taskbar |
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat and its walkthrough (`ChatWindow`), his replies drawn as Markdown (`Markdown`), settings, his pointer (`HandWindow`), the ring (`RingWindow`), his panel showing a page (`PanelWindow`), and the tray icon |
| `src/System/` | Windows itself: Win32 calls, docking maths, the stop key, the core's overlay messages, the app's settings, updates (`Updates`), and the log of everything sent to Onsay (`Privacy`) |
| `src/Core/` | `Core.cs` runs the core, `Link.cs` signs in through the website and holds the live connection to the server |
| `src/Character/` | the character: his face as data, and the view that animates it |

## Using it

Signing in: right click him and choose Sign in to Onsay. The website opens, you name this computer, its Windows name
suggested, and approve, and the app gets its own token. The home screen shows the route it goes by, "This computer is
laptop.brandon". The core signs in with the same token. Permissions under the chat say what his programs may do on this
computer, and the core refuses anything that needs one not ticked.

Settings, in his right click menu and the chat's menu, has it all on pages: starting with Windows, permissions and your
account. What a lingo asks for, like a key, is set on the dashboard and kept with your account (see
`..\onsay\core\README.md`).

Everything he keeps is in `%AppData%\Onsay`: `desktop.json` for the app, `core.json` and `core.log` for the core, and
`core\` for the core's own storage, `panel\` for his panel's browser storage, and `sent.log`, everything sent to Onsay.
