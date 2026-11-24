using System.Windows.Input;
namespace Kopra;

sealed class Shortcuts : IDisposable
{
    readonly nint hwnd; readonly Preferences preferences; readonly Action<bool> open; readonly WinKeyHook windows;
    int searchId, historyId, nextId = 20;
    public bool Recording
    {
        get; private set;
    }
    public Action<uint, uint, bool>? Captured
    {
        get; set;
    }
    public string StartupError { get; private set; } = "";
    public bool WindowsHookInstalled => windows.IsInstalled;
    public bool HistoryRegistered => historyId != 0;
    public Shortcuts(nint hwnd, Preferences preferences, Action<bool> open)
    {
        this.hwnd = hwnd;
        this.preferences = preferences;
        this.open = open;
        windows = new WinKeyHook(() => Application.Current.Dispatcher.BeginInvoke(() => { if (Recording) Captured?.Invoke(0, 0, true); else open(false); }), () => preferences.WindowsKey);
        if (!Register(false, preferences.SearchKey, preferences.SearchModifiers, preferences.WindowsKey, out var error))
            StartupError = error;
        if (!Register(true, preferences.HistoryKey, preferences.HistoryModifiers, false, out error))
            StartupError = error;
    }
    public string Label(bool history) => !history && preferences.WindowsKey ? "Windows-Taste" : Label(history ? preferences.HistoryKey : preferences.SearchKey, history ? preferences.HistoryModifiers : preferences.SearchModifiers);
    public static string Label(uint key, uint modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & 2) != 0)
            parts.Add("Strg");
        if ((modifiers & 1) != 0)
            parts.Add("Alt");
        if ((modifiers & 4) != 0)
            parts.Add("Umschalt");
        if ((modifiers & 8) != 0)
            parts.Add("Win");
        parts.Add(key == 32 ? "Leertaste" : KeyInterop.KeyFromVirtualKey((int)key).ToString());
        return string.Join(" + ", parts);
    }
    public static bool Valid(uint key, uint modifiers) => key != 0 && key <= 255 && key is not (16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165 or 229 or 231) && ((modifiers & 11) != 0 || key is >= 112 and <= 135) && !(modifiers == 8 && key is 76 or 85) && !(modifiers == 3 && key == 46);
    public bool Change(bool history, uint key, uint modifiers, bool windowsKey, out string error)
    {
        error = "";
        if (windowsKey && history)
        {
            error = "Die Windows-Taste ist für die Suche vorgesehen.";
            return false;
        }
        if (!windowsKey && !Valid(key, modifiers))
        {
            error = "Bitte eine Tastenkombination oder eine Funktionstaste wählen.";
            return false;
        }
        if (!windowsKey && (history ? !preferences.WindowsKey && key == preferences.SearchKey && modifiers == preferences.SearchModifiers : key == preferences.HistoryKey && modifiers == preferences.HistoryModifiers))
        {
            error = "Diese Kombination nutzt bereits die andere Aktion.";
            return false;
        }
        if (!Register(history, key, modifiers, windowsKey, out error))
            return false;
        if (history)
        {
            preferences.HistoryKey = key;
            preferences.HistoryModifiers = modifiers;
        }
        else
        {
            preferences.SearchKey = key;
            preferences.SearchModifiers = modifiers;
            preferences.WindowsKey = windowsKey;
        }
        return true;
    }
    bool Register(bool history, uint key, uint modifiers, bool windowsKey, out string error)
    {
        error = "";
        int old = history ? historyId : searchId;
        if (windowsKey)
        {
            if (!windows.IsInstalled) { error = "Die Windows-Taste konnte nicht registriert werden. Das bisherige Kürzel bleibt erhalten."; return false; }
            if (old != 0)
                Native.UnregisterHotKey(hwnd, old);
            searchId = 0;
            return windows.IsInstalled;
        }
        if (old != 0 && key == (history ? preferences.HistoryKey : preferences.SearchKey) && modifiers == (history ? preferences.HistoryModifiers : preferences.SearchModifiers))
            return true;
        int id = ++nextId;
        if (!Native.RegisterHotKey(hwnd, id, modifiers | 0x4000, key))
        {
            error = "Diese Kombination ist belegt. Die bisherige Einstellung bleibt erhalten.";
            return false;
        }
        if (old != 0)
            Native.UnregisterHotKey(hwnd, old);
        if (history)
            historyId = id;
        else
            searchId = id;
        return true;
    }
    public bool Handle(int id)
    {
        if (id == 0 || id != searchId && id != historyId)
            return false;
        bool history = id == historyId;
        if (Recording)
            Captured?.Invoke(history ? preferences.HistoryKey : preferences.SearchKey, history ? preferences.HistoryModifiers : preferences.SearchModifiers, false);
        else
            open(history);
        return true;
    }
    public void Begin(Action<uint, uint, bool> captured)
    {
        Captured = captured;
        Recording = true;
    }
    public void End()
    {
        Recording = false;
        Captured = null;
    }
    public void Capture(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint mods = 0;
        var current = Keyboard.Modifiers;
        if (current.HasFlag(ModifierKeys.Control))
            mods |= 2;
        if (current.HasFlag(ModifierKeys.Alt))
            mods |= 1;
        if (current.HasFlag(ModifierKeys.Shift))
            mods |= 4;
        if (current.HasFlag(ModifierKeys.Windows))
            mods |= 8;
        if (vk is 16 or 17 or 18 or 91 or 92 or 160 or 161 or 162 or 163 or 164 or 165)
            return;
        Captured?.Invoke(vk, mods, false);
    }
    public void Dispose()
    {
        End();
        if (searchId != 0)
            Native.UnregisterHotKey(hwnd, searchId);
        if (historyId != 0)
            Native.UnregisterHotKey(hwnd, historyId);
        windows.Dispose();
    }
}
