using System.Runtime.InteropServices;
namespace Kopra;

static class Native
{
    public const int WM_HOTKEY = 0x312, WM_CLIPBOARDUPDATE = 0x31D;
    [DllImport("user32")] public static extern nint GetForegroundWindow();
    [DllImport("user32")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    public static bool HasNativeFrame(nint hwnd) => (GetWindowLongPtr(hwnd, -16).ToInt64() & 0x00C40000) != 0;
    public static bool IsLayered(nint hwnd) => (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x00080000) != 0;
    [DllImport("user32")] static extern bool AttachThreadInput(uint first, uint second, bool attach);
    [DllImport("user32")] static extern bool BringWindowToTop(nint hwnd);
    [DllImport("user32")] static extern nint SetActiveWindow(nint hwnd);
    [DllImport("user32")] static extern nint SetFocus(nint hwnd);
    [DllImport("user32")] static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] static extern bool IsHungAppWindow(nint hwnd);
    [DllImport("kernel32")] static extern uint GetCurrentThreadId();
    public static bool Activate(nint hwnd)
    {
        ShowWindow(hwnd, 5);
        if (GetForegroundWindow() == hwnd)
        {
            SetActiveWindow(hwnd);
            SetFocus(hwnd);
            return true;
        }
        uint current = GetCurrentThreadId(), foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = false;
        try
        {
            if (foreground != 0 && foreground != current && !IsHungAppWindow(GetForegroundWindow()))
                attached = AttachThreadInput(current, foreground, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            SetActiveWindow(hwnd);
            SetFocus(hwnd);
        }
        finally { if (attached) AttachThreadInput(current, foreground, false); }
        return GetForegroundWindow() == hwnd;
    }
    [DllImport("user32")] public static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32")] public static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32")] public static extern short GetAsyncKeyState(int key);
    [DllImport("dwmapi")] public static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
    [DllImport("user32")] public static extern nint SetWindowsHookEx(int kind, Hook callback, nint module, uint thread);
    [DllImport("user32")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? module);
    public delegate nint Hook(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardData
    {
        public uint Key, Scan, Flags, Time; public nuint Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Input
    {
        public uint type; public InputUnion data;
    }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput keyboard; [FieldOffset(0)] public MouseInput mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public int x, y; public uint data, flags, time; public nuint extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct KeyboardInput
    {
        public ushort key, scan; public uint flags, time; public nuint extra;
    }
    [DllImport("user32", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    public const nuint Marker = 0x4B4F5052;
    public static bool Key(uint key, bool up = false) => SendInput(1, [new Input { type = 1, data = new InputUnion { keyboard = new KeyboardInput { key = (ushort)key, flags = up ? 2u : 0, extra = Marker } } }], Marshal.SizeOf<Input>()) == 1;
    public static bool ModifiersReleased() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.All(x => (GetAsyncKeyState(x) & 0x8000) == 0);
}
// Suppress only an unmodified Windows key. Replay its down event before
// passing a chord through; its real key-up then releases the replayed key.
sealed class WinKeyState
{
    public uint Active
    {
        get; private set;
    }
    public bool Chord
    {
        get; private set;
    }
    public (bool swallow, bool replay, bool open) Handle(uint key, bool down, bool modifiers = false)
    {
        bool win = key is 0x5B or 0x5C;
        if (Active == 0)
        {
            if (win && down && !modifiers)
            {
                Active = key;
                Chord = false;
                return (true, false, false);
            }
            return (false, false, false);
        }
        if (key == Active)
        {
            if (down)
                return (!Chord, false, false);
            bool solo = !Chord;
            Active = 0;
            Chord = false;
            return (solo, false, solo);
        }
        if (down && !Chord)
        {
            Chord = true;
            return (false, true, false);
        }
        return (false, false, false);
    }
}
sealed class WinKeyHook : IDisposable
{
    public bool IsInstalled => hook != 0;
    readonly Native.Hook callback; readonly Thread thread; System.Windows.Threading.Dispatcher? dispatcher; nint hook; readonly WinKeyState state = new(); readonly Action open; readonly Func<bool> enabled;
    public WinKeyHook(Action open, Func<bool> enabled)
    {
        this.open = open;
        this.enabled = enabled;
        callback = OnKey;
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() => { dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher; hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0); ready.Set(); System.Windows.Threading.Dispatcher.Run(); }) { IsBackground = true, Name = "Kopra Windows key" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
    }
    nint OnKey(int code, nint message, nint data)
    {
        if (code < 0)
            return Native.CallNextHookEx(hook, code, message, data);
        var key = Marshal.PtrToStructure<Native.KeyboardData>(data);
        if (key.Extra == Native.Marker || ((key.Flags & 0x10) != 0) || (!enabled() && state.Active == 0))
            return Native.CallNextHookEx(hook, code, message, data);
        bool down = message == 0x100 || message == 0x104;
        uint active = state.Active;
        bool modifiers = new[] { 0x10, 0x11, 0x12 }.Any(x => (Native.GetAsyncKeyState(x) & 0x8000) != 0);
        var action = state.Handle(key.Key, down, modifiers);
        if (action.replay)
            Native.Key(active);
        if (action.open)
            open();
        return action.swallow ? 1 : Native.CallNextHookEx(hook, code, message, data);
    }
    public void Dispose()
    {
        if (hook != 0)
            Native.UnhookWindowsHookEx(hook);
        if (state.Active != 0 && state.Chord)
            Native.Key(state.Active, true);
        dispatcher?.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Send);
    }
}
