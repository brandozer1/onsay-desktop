# Onsay Desktop

Onsay for Windows, in C# and WPF: a free AI agent and employee. Change it however you like: the character, the UI, the
setup, the lingo it ships. It is Apache 2.0 licensed and built to be built on.

It is two things:

- **The floor**, `floor.exe`, the lingo client made for Windows, from
  [lingo-client](https://github.com/brandozer1/lingo-client): a small program that takes calls on port 6969, checks
  who signed each and whether its policy lets them, runs the lingo script, and logs it. Nothing else.
- **The app**, one of the floor's lingos: his character on the taskbar, the chat and its to-dos, setting him up, and
  Apps and settings: Apps, Devices, People, Activity, Settings and Advanced. On screen a lingo is an app.

## Setting him up

The first time he opens, a window walks through it one step at a time: hello, his look, where to find him, what is sent
to Onsay (Agree and continue), his AI, and signing in, which can be skipped. Then his chat opens.

**Signing in** goes through Onsay's website (`src/Core/Account.cs`): the page opens in the browser with a code, and once
you press Connect there the app gets this computer's token. The floor then sends its own key to Onsay itself, signed by
that key, so Onsay knows this computer by it: its log, its calls to Onsay's AI and its `lingo(...)` count for your
account with no token at all. Sign out tells Onsay too, so the key counts for nothing until you sign in again.

## Using him

He sits on the taskbar and watches your cursor. Click him to chat. Alt+Shift+O stops him at any time, from any app.
While something has the whole screen he steps aside. Right click him for Open chat, Apps, Settings, Put him back and
Close Onsay. Closed, he goes to the notification area by the clock and Onsay keeps running, so your devices and apps
can still use this computer. Click his face there, or open Onsay again, to bring him back. Quit Onsay, from there or in
Settings, stops it all.

**Where the chat sits** is yours to choose, the way a browser's developer tools dock: next to him, moving with him, or
docked along the left, right or bottom of his screen. Docked, it keeps that strip of the screen the way the taskbar does,
so your windows make room instead of sitting under it, and it stays open while he works. The button beside the menu in
its top bar shows the four as small pictures of a screen. Dragging the top bar does the same: it carries him and the
card together, and dropped on a side of the screen, which lights up as you get near, the card docks there and he walks
back to where he was. Drag a docked one's top bar away and it comes off, with him, and its inner edge drags to make it
wider or taller. It is kept as `Docked` and `DockSize` in `desktop.json`, and Windows gets the strip back when the
card closes or the app ends.

**The chat only draws; the agent does the work.** Each to-do is kept by the app (`src/Core/ToDos.cs`) and run by the
`agent` lingo on the floor, `agent.ask`, with everything said in it before, so he remembers the to-do and what he did
in it. A message goes to the app's own `/onsay` with the prompt key, which any program given that key can use too.
Stop, or the stop key, starts the floor again, which ends whatever it was doing.

**What he is doing shows as he does it.** While a message runs the app asks `/onsay/sessions/<session>/live` every
moment, the way it asks for the questions waiting: each step is a line in plain words under your message, like
"Understanding what you asked", "Found 8 ways to do it", "Picked: Move the mouse pointer" and "Running it on your
laptop", what a step asked indented under it, and the newest on the bar. Once he answers they fold under one line,
"4 steps, 3.1s"; a click opens or folds them all, and he remembers which (`ShowSteps` in `desktop.json`).

**A value a call needs that nobody gave** is asked for on a card. Your answers go to `/onsay/sessions/<session>/answers`, and the card keeps only each question and its answer.

**Questions back** show in the chat together: a question as text gets a box to type in, `{question, why, choices, other,
skip}` a button for each choice, with Something else and Skip unless `other` or `skip` is false. Send, once every one is
answered, sends them as the next message in the same session, a line each, like `Where to? Rome`.

**Any lingo can talk in the chat** through the app's `chat` lingo, which the floor sends on to the app, with the
to-do it is for. `chat.ask` asks you while it works and waits: its questions open the chat and show as a card, and your
answers go back to the lingo, so the agent carries on with them in the same turn. `chat.say` adds his line to the chat
and `chat.step` a step under the to-do running; neither opens the chat, so the work keeps the keyboard.

His replies are drawn as Markdown, with Copy. What the base calls draw, his pointer, rings, and the chat
lines, come as lines of JSON on the floor's output (`Overlay.cs`). Whatever lingo uses the mouse and keyboard, his
pointer glides to each click and ripples, and turns into a text cursor that ticks where he types (`HandWindow.cs`).
That shows only with Hide where he clicks off, in Settings: then he flies along with it and the chat steps aside.
Work in the background has him click and type in apps without bringing them to the front or moving your mouse, and
brings an app forward for one action when it doesn't allow that, saying why. Both are on at first, so he works unseen
until he must show something or you ask to see, and both are kept by the floor (`input.modes`).

**Screens** are canvases a lingo draws on as it works, like the browser's page, with `screen.open`, `screen.draw`,
`screen.show` and `screen.close`: Canvas 2D instructions, drawn here by `Drawing.cs`, never pictures sent whole. Each to-do that opened some shows them in a small strip under its row, and above the box when it is
open; a click shows them full screen, one at a time, and Screens, above the list, shows every one in a grid. `screen` is one
of the app's own lingos, beside `chat` and `settings`: the floor sends each call here, `Screens.cs` keeps the screens with
their to-do and `ScreensWindow.cs` shows them.

**What an app may do is decided in one place.** This computer's own rules, a file in Onsay's folder that Advanced
opens, let every app here do anything until someone narrows them. What an app says it uses is shown on its page, never
a permission. Nothing asks per request. A question a lingo asks you with `chat.ask` shows in the chat, and your answer
goes back through `/.lingo/answer`.

## Apps and settings

| Page | What it has |
|------|-------------|
| Apps | a list of the apps on this computer, none of what is built into the floor. Each opens to tabs for what it says it uses; who can use it, with Stop sharing on what was shared (`share`, `unshare`) and Share with someone; and its settings, each with its value now (`set`). A map shows every address on this computer and what can be asked for there |
| Devices | the computers on the account, online or when last seen (`onsay.devices`) |
| People | everyone an app is shared with, and what of it, with Stop sharing |
| Activity | the floor's `requests.log` in words, requests from anyone but this computer and calls refused an app, a row each under its day, newest first, the time on the left: "Your desk used Windows", "Native wasn't allowed to run a program" |
| Settings | sections of one setting a row: the account and signing in and out, starting with Windows, Work in the background and Show where he clicks, his look, updates and Uninstall Onsay |
| Advanced | the same rows, in plain labels: this computer's own rules, every rule in force and all of them in full, this computer's key and the chat (prompt) key to copy, the folders, and the logs, each read in place in a monospace viewer with Copy and Open the file: every request (`requests.log`) as written, which is what is sent to Onsay, sealed, and the floor's `floor.log` |

**What it ships.** Each `.ls` file in `..\lingo-client\lingo-library`, like `agent` and `ai`, offered to the floor with
its code each time the floor starts, so the floor keeps it and runs it. It remembers which it offered once (`Started` in
`desktop.json`), so one you turn off stays off.

**How it talks to the floor.** As a lingo: each call a request to the floor, signed by the app's own key and carrying a
badge from you, the floor's owner, both kept in `keys\`, sealed for your Windows user (`src/Core/Core.cs`). The floor
calls the app back the same way, signed by the floor, for its own lingos, `chat` and `settings`, which it answers on a
port of its own on this computer (`src/Core/Host.cs`). Each to-do is the `agent` lingo's `agent.ask`, run on the floor
(`src/Core/ToDos.cs`).

## Files

Everything he keeps is in `%AppData%\Onsay`: `desktop.json` for the app, `todos.json` for your to-dos, `account.json` for who you are on Onsay, `keys\` for the
app's keys, `floor.json` (the floor's home, its owner, and a port when one is written there) and `floor.log` for the
floor, `floor\` for its home (`policy.json`, `offered.json`, each lingo's settings and `requests.log`), and `crash.log`,
every error nothing caught, with its whole stack. A test sets `ONSAY_DATA` to a folder of its own before he starts, so
it never touches yours, and runs beside yours, with its own port written in that folder's `floor.json`. Uninstalling, from his settings or Windows' Installed apps, first stops everything
running from Onsay, the floor and the browser he uses included, so its folder goes whole, then asks whether this
folder and the browser's (`%LocalAppData%\lingo-browser`) go too, and keeps them unless you say so, since most who
uninstall mean to install again.

## Building

```
..\lingo-client\core\build.ps1   builds the floor, in the lingo-client project beside this one
.\start.ps1                       builds and starts the app. Needs the .NET SDK
.\release.ps1 0.2.0               a release in Releases\. Needs Inno Setup 6, from jrsoftware.org
```

## Releasing

Push a tag like `v0.2.0`, then on the server run `./release.sh 0.2.0`, with `release.sh` and an `.env` holding the
storage's keys beside it. It builds the lingo client and the app in a throwaway container, makes the installer with
Inno Setup under Wine (`release.ps1`, `setup.iss`), and puts them in Onsay's storage, the `onsay` bucket at
storage.brandongamez.com: `Onsay-Setup.exe`, one installer for 32 and 64-bit Windows 7 and later, always at
`windows/Onsay-Setup.exe`, and `release.json` with the `update-` files installed copies update from. Onsay's server
signs `release.json` as it hands it out.

The installer has nothing to click: it shows his splash, `splash.png`, with a bar under it while it puts him in
`%LocalAppData%\Onsay` for that person alone, with no admin rights, and opens him: the
launcher `Onsay.exe` (`launcher.c`), `version.txt`, and the version itself in `app-<version>`. On Windows 7 it first
checks for .NET Framework 4.8 and the update KB2999226, and says where to get either. Installed copies ask Onsay's server
for a newer release half a minute after starting and every hour (`api.onsay.dev/updates`, which hands out the newest
release in the storage, onsay's `server/routes/updates.js`), through the update lingo built into the floor beside the app, which
puts it together beside the version running, downloading only the files that changed, each checked against what Onsay
signed, and put it in by themselves the next time he isn't busy, his card put away and no to-do working, or as he next starts
(`src/System/Updates.cs`). Nobody presses anything.

## Layout

| File | Job |
|------|-----|
| `Onsay.csproj` | the build, with the floor and the lingos the app offers it copied in from `..\lingo-client` |
| `app.manifest` | tells Windows the app is per monitor DPI aware |
| `onsay.ico` | his face as the app, launcher and installer icon |
| `splash.png` | what the installer shows while it puts him in |
| `fonts/` | the website's fonts, Gabarito and IBM Plex, which every window uses, with their license (`OFL.txt`) |
| `start.ps1` | builds and starts the app, closing a running one first |
| `release.ps1` | a release: the installer and the files updates come from, in `Releases\`, on Windows or Linux |
| `release.sh` | a release made on a server in a throwaway container and put in Onsay's storage |
| `setup.iss` | the installer, for Inno Setup: one for 32 and 64-bit Windows 7 and later |
| `launcher.c`, `launcher.rc` | `Onsay.exe` in the install folder, starting the version `version.txt` names, with his icon |
| `src/Program.cs` | the entry point: one of him, sitting on the taskbar |
| `src/Core/Core.cs` | runs the floor, offers it the lingos, and signs every call the app makes to it |
| `src/Core/Host.cs` | the app's own lingos, `chat` and `settings`, which the floor calls, and the to-dos at `/onsay` |
| `src/Core/Account.cs` | your Onsay account: signing in through the website, the floor's key sent to Onsay, your devices and sharing |
| `src/Core/ToDos.cs` | your to-dos, each run by the `agent` lingo on the floor, with its steps and questions |
| `src/Core/Screens.cs` | the screen lingo: the screens lingos draw on, kept for the to-do list and the Screens window |
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat, signing in and the look every window shares (`ChatWindow`), replies drawn as Markdown (`Markdown`), setting him up (`SetupWindow`), Apps and settings, Apps to Advanced (`SettingsWindow`), his pointer (`HandWindow`), the ring (`RingWindow`), the screens, a strip under each to-do and full screen (`ScreensWindow`), and a screen drawn from its instructions (`Drawing`) |
| `src/System/` | Windows itself: Win32 calls (`Native`), docking maths (`Dock`), the stop key (`StopKey`), what the base calls ask to be drawn or said in the chat (`Overlay`), the app's settings and starting with Windows (`Settings`), updates and removing him (`Updates`), Ed25519 signatures (`Ed25519`), and what .NET Framework 4.8 lacks (`Compat`) |
| `src/Character/` | his face as data (`Face`) and the view that animates it (`OnsayView`) |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Part of Onsay, with the lingo client
(https://github.com/brandozer1/lingo-client) and Onsay IO (https://github.com/brandozer1/onsay-io).
