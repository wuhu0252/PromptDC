using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PromptDC;

internal static class Native
{
    public const int HotkeyId = 0x5044;
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort Key, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }

    public static bool ModifiersPressed => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => GetAsyncKeyState(k) < 0);
    public static void Chord(params int[] keys)
    {
        var events = keys.Select(k => Key(k, false)).Concat(keys.Reverse().Select(k => Key(k, true))).ToArray();
        if (SendInput((uint)events.Length, events, Marshal.SizeOf<Input>()) != events.Length)
            throw new InvalidOperationException("无法向目标窗口发送按键。请确认目标软件没有以管理员权限运行。");
    }
    private static Input Key(int key, bool up) => new() { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = (ushort)key, Flags = up ? 2u : 0u } } };

    public static (uint Modifiers, uint Key) ParseHotkey(string value)
    {
        uint modifiers = 0;
        uint key = 0;
        foreach (var piece in value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (piece.ToUpperInvariant())
            {
                case "CTRL": modifiers |= 2; break;
                case "ALT": modifiers |= 1; break;
                case "SHIFT": modifiers |= 4; break;
                case "WIN": modifiers |= 8; break;
                default:
                    if (key != 0 || piece.Length != 1 || !char.IsAsciiLetterOrDigit(piece[0]))
                        throw new InvalidOperationException("快捷键格式示例：Ctrl+Alt+E，主键必须是字母或数字。");
                    key = char.ToUpperInvariant(piece[0]); break;
            }
        }
        if (key == 0 || (modifiers & 3) == 0)
            throw new InvalidOperationException("快捷键至少包含 Ctrl 或 Alt，以及一个字母或数字。");
        return (modifiers | 0x4000, key);
    }
}

// Only count activity. Never collect/store pressed keys or text.
internal sealed class InputGuard : IDisposable
{
    private delegate nint Hook(int code, nint message, nint data);
    private delegate void WinEvent(nint hook, uint evt, nint window, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int type, Hook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWinEventHook(uint min, uint max, nint module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? module);
    private readonly Hook keyboard, mouse;
    private readonly WinEvent foreground;
    private readonly nint keyboardHandle, mouseHandle, foregroundHandle;
    public long Epoch { get; private set; }
    public InputGuard()
    {
        keyboard = (code, message, data) =>
        {
            if (code >= 0 && ((int)message is 0x100 or 0x104) && (Marshal.ReadInt32(data, 8) & 0x10) == 0) Epoch++;
            return CallNextHookEx(0, code, message, data);
        };
        mouse = (code, message, data) =>
        {
            if (code >= 0 && ((int)message is 0x201 or 0x204 or 0x207 or 0x20A or 0x20B or 0x20E)
                && (Marshal.ReadInt32(data, 12) & 1) == 0) Epoch++;
            return CallNextHookEx(0, code, message, data);
        };
        foreground = (_, _, _, _, _, _, _) => Epoch++;
        keyboardHandle = SetWindowsHookExW(13, keyboard, GetModuleHandleW(null), 0);
        mouseHandle = SetWindowsHookExW(14, mouse, GetModuleHandleW(null), 0);
        foregroundHandle = SetWinEventHook(3, 3, 0, foreground, 0, 0, 0);
        if (keyboardHandle == 0 || mouseHandle == 0 || foregroundHandle == 0)
        {
            Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监听输入状态，已停止自动替换。");
        }
    }
    public bool Matches(nint window, long epoch) => window != 0 && window == Native.GetForegroundWindow() && epoch == Epoch;
    public void Dispose()
    {
        if (keyboardHandle != 0) UnhookWindowsHookEx(keyboardHandle);
        if (mouseHandle != 0) UnhookWindowsHookEx(mouseHandle);
        if (foregroundHandle != 0) UnhookWinEvent(foregroundHandle);
    }
}
