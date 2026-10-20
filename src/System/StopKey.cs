// StopKey.cs - Alt+Shift+O, the safeguard: a low level keyboard hook on its own thread, so a busy window never delays it.
// Only real keys count, so he can never press it himself, and the O is swallowed so the app in front does not get it.
//
// 1. Start(pressed)    hooks on. pressed runs on a pool thread, never the hook's, so the hook always answers at once
using System.Runtime.InteropServices;
using System.Windows.Threading;
using static Onsay.Desktop.Native;

namespace Onsay.Desktop;

static class StopKey
{
    static readonly HookProc OnKey = KeyHook;   // held here so the collector never frees it under Windows
    static Action? pressed;
    static bool alt, shift, ctrl, win, held;    // held on the real keyboard, from the hook's own events

    // 1.
    public static void Start(Action onPressed)
    {
        if (pressed is not null) return;
        pressed = onPressed;
        var t = new Thread(() =>
        {
            var hook = Hook(13, OnKey);   // 13 is the keyboard
            Dispatcher.Run();
            UnhookWindowsHookEx(hook);
        }) { IsBackground = true, Name = "Onsay stop key" };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
    }

    static IntPtr KeyHook(int code, IntPtr msg, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, msg, data);
        var k = Marshal.PtrToStructure<KEYHOOK>(data);
        bool down = (int)msg is 0x100 or 0x104, up = (int)msg is 0x101 or 0x105;   // key down or up, with or without alt
        if ((k.flags & 0x10) != 0 || !(down || up)) return CallNextHookEx(IntPtr.Zero, code, msg, data);   // 0x10 injected
        switch (k.vk)
        {
            case 0x12 or 0xA4 or 0xA5: alt = down; break;
            case 0x10 or 0xA0 or 0xA1: shift = down; break;
            case 0x11 or 0xA2 or 0xA3: ctrl = down; break;
            case 0x5B or 0x5C: win = down; break;
            // Holding it down repeats the key, but stops once. Its release is swallowed too, whatever was let go first.
            case 'O' when up:
                if (!held) break;
                held = false;
                return 1;
            // Windows agreeing too covers a release this hook missed, like one made on the lock screen.
            case 'O' when alt && shift && !ctrl && !win && GetAsyncKeyState(0x12) < 0 && GetAsyncKeyState(0x10) < 0:
                if (!held) ThreadPool.QueueUserWorkItem(_ => pressed?.Invoke());
                held = true;
                return 1;
        }
        return CallNextHookEx(IntPtr.Zero, code, msg, data);
    }
}
