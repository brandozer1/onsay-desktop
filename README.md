# Onsay Desktop

Onsay for Windows, in C# and WPF: a free, open source AI agent and employee. Customize, change, tweak and edit it
however you like: the character, the UI, the setup, the lingos it ships. It is Apache 2.0 licensed and built to be built
on.

It is two things:

- **The lingo client**, `onsay-core.exe`, from [lingo-client](https://github.com/brandozer1/lingo-client): a small
  program that answers web requests on port 6969, on this computer alone until its policy shares something. Each running
  lingo in its lingos folder is a program at its route. Its **onsay lingo** connects it to Onsay: signing in, the
  connection, and `/onsay`, where prompts come in.
- **The app around it**: his character on the taskbar, the chat, setting him up, and his window: Apps, Devices, People,
  Activity, Settings and Advanced. On screen a lingo is an app.

## Setting him up

The first time he opens, a window walks through it one step at a time: hello, his look, where to find him, what is sent
to Onsay (Agree and continue), and signing in, which can be skipped. Then his chat opens.

**Signing in** is the onsay lingo's: `onsay.sign_in` gives the address of Onsay's page and a code. The page opens in the
browser and the code shows in the app; once the person presses Connect there, the onsay lingo keeps its token and
connects. "You're brandon. This computer is laptop." is `onsay.status`, and Sign out is `onsay.sign_out`. The app keeps
no sign in of its own. Signing in works even with the onsay lingo turned off, and never holds anything up; when it
fails, the reason shows in words (`failed` in `onsay.status`).

**With the onsay lingo turned off** (`running` in `onsay.status`), the chat and Settings say so plainly, with Turn
Onsay on.

## Using him

He sits on the taskbar and watches your cursor. Click him to chat. Alt+Shift+O stops him at any time, from any app.
While something has the whole screen he steps aside. Right click him for Open chat, Apps, Settings, Put him back and
Quit.

**Each chat is a session of the onsay lingo.** A message goes to `/onsay` on the lingo client with its prompt key, read
from the onsay lingo's settings, in a session the app names when the chat starts, the way the onsay lingo names one. The
onsay lingo keeps the session on this computer and sends the whole of it to Onsay with every message, so he remembers
the chat. New chat starts a new one. One message at a time; Stop, or the stop key, starts the lingo client again,
which ends whatever it was doing.

**What he is doing shows as he does it.** While a message runs the app asks `/onsay/sessions/<session>/live` every
moment, the way it asks for the "Allow" questions: each step is a line in plain words under your message, like
"Understanding what you asked", "Found 8 ways to do it", "Picked: Move the mouse pointer" and "Running it on your
laptop", what a step asked indented under it, and the newest on the bar. Once he answers they fold under one line,
"▸ 4 steps · 3.1s"; a click opens or folds them all, and he remembers which (`ShowSteps` in `desktop.json`).

**Let me choose**, a switch under the box, has him ask before every pick he makes (`LetMeChoose` in `desktop.json`,
sent as `mode: "decision"`): each time he would choose a way, for your message or for anything it asks on the way, a
card asks "Which should I do on your laptop?" with each way in a few words and None of these, which stops him. A value
he needs that nobody gave is asked for on the next card. Your answers go to `/onsay/sessions/<session>/answers`, and the
card keeps only each question and its answer.

**Questions back** show in the chat together: a question as text gets a box to type in, `{question, choices}` a button
for each choice. Send, once every one is answered, sends them as the next message in the same session, a line each, like
`Where to? Rome`.

His replies are drawn as Markdown, with Copy. What the `windows` lingo draws, his pointer, rings and his panel, comes as
lines of JSON on the lingo client's output (`Overlay.cs`). Whatever lingo uses the mouse and keyboard, his pointer
glides to each click and ripples, and turns into a text cursor that ticks where he types (`HandWindow.cs`).

**He asks first.** When an app needs something that matters (your screen, a file outside his own folder, running a
program) and hasn't been allowed it, a card shows beside him with Allow, Only this time and Don't allow. Asked for by
another of your devices it says so: "Your desktop wants Onsay to run a program here." The app asks the
lingo client for the questions every second (`/.lingo/asks`) and answers through `/.lingo/answer`. With the app closed,
the lingo client refuses instead.

## His window

| Page | What it has |
|------|-------------|
| Apps | every lingo on this computer: on or off, what it does, or why it can't run. Details: what it can do, each thing that matters allowed without asking or asked first (`grant`, `ungrant`); who can use it, with Stop sharing (`unshare`); and its own settings. Share: an Onsay name, and all of it or one part (`share`) |
| Devices | the computers on the account, online or when last seen (`onsay.devices`) |
| People | everyone an app is shared with, and what of it, with Stop sharing |
| Activity | the lingo client's `decisions.log` in words, newest first: "Your desk used Windows", "Native wasn't allowed to run a program" |
| Settings | the account and signing in and out, starting with Windows, his look, updates and Uninstall Onsay |
| Advanced | the ceiling, every access rule and block in force, the account's policy, this computer's key and the chat (prompt) key, the folders, the lingo client's settings file and log, and everything sent to Onsay (`sent.log`) |

**What it ships.** Every `.lingo` file in `..\lingo-client\core\bin\lingos`: `onsay`, `windows`, `native`, `agent` and
the rest of the lingo library, copied into the lingos folder each time the lingo client starts. The first time a shipped
lingo appears the app starts it, and gives `windows` seeing the screen, using the mouse and keyboard and reading files.
It remembers which it started (`Started` in `desktop.json`), so one you turn off stays off.

**How it talks to the lingo client.** Through its control routes, `/.lingo/<verb>` (`start`, `stop`, `asks`, `answer`,
`settings`, `set`, `policy`, `share`, `unshare`, `grant`, `ungrant`, `ceiling`, and `call` for the onsay lingo's
phrases), or its command line,
`onsay-core core.json <verb> ...`, when nothing answers the port (`Core.Control`).

## Files

Everything he keeps is in `%AppData%\Onsay`: `desktop.json` for the app, `core.json` (the lingo client's home, and a port
when one is written there) and `core.log` for the lingo client, `core\` for its home (lingos, settings, storage and
`sent.log`), and `panel\` for his panel's browser storage. A test sets `ONSAY_DATA` to a folder of its own before he
starts, so it never touches yours.

## Building

```
..\lingo-client\core\build.ps1   builds the lingo client and its lingos, in the lingo-client project beside this one
.\start.ps1                       builds and starts the app. Needs the .NET 8 SDK
.\release.ps1 0.2.0               a release in Releases\. Needs vpk: dotnet tool install -g vpk
```

## Releasing

Push a tag like `v0.2.0`, or run the release workflow from the Actions tab with a version. GitHub builds the lingo
client and the app, packs them with Velopack (`release.ps1`), and puts them on this repo's release page:
`Onsay-win-Setup.exe`, the portable zip, and the packages installed copies update from. It needs no secrets.

The installer puts him in `%LocalAppData%\Onsay` for that person alone, with no admin rights, and opens him. Installed
copies look for a newer release a minute after starting and every six hours, download it quietly, and it goes in when he
next quits (`src/System/Updates.cs`).

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
| `src/Core/Core.cs` | runs the lingo client, ships its lingos, lists them, and makes every control call and onsay lingo call |
| `src/Windows/` | every window: him on the taskbar (`BuddyWindow`), the chat, signing in and the look every window shares (`ChatWindow`), replies drawn as Markdown (`Markdown`), setting him up (`SetupWindow`), his window, Apps to Advanced (`SettingsWindow`), a lingo asking first (`AskWindow`), his pointer (`HandWindow`), the ring (`RingWindow`) and his panel (`PanelWindow`) |
| `src/System/` | Windows itself: Win32 calls (`Native`), docking maths (`Dock`), the stop key (`StopKey`), what the windows lingo asks to be drawn (`Overlay`), the app's settings and starting with Windows (`Settings`), and updates (`Updates`) |
| `src/Character/` | his face as data (`Face`) and the view that animates it (`OnsayView`) |

## License

Apache License 2.0. See [LICENSE](LICENSE) and [NOTICE](NOTICE). Part of Onsay, with the lingo client
(https://github.com/brandozer1/lingo-client) and Onsay IO (https://github.com/brandozer1/onsay-io).
