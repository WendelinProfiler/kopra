using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Threading;
namespace Kopra;

sealed partial class LauncherWindow
{
    DispatcherTimer? focusGuard;
    WeakReference<UIElement>? lastFocus;

    void InitializeFocusOwnership()
    {
        PreviewGotKeyboardFocus += (_, e) => { if (e.NewFocus is UIElement element) lastFocus = new(element); };
        focusGuard = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(150) };
        focusGuard.Tick += (_, _) => EnsureFocusOwnership();
        IsVisibleChanged += (_, _) => { if (IsVisible && !previewMode) focusGuard.Start(); else focusGuard.Stop(); };
        Deactivated += (_, _) => { if (IsVisible && !previewMode) Dispatcher.BeginInvoke(EnsureFocusOwnership, DispatcherPriority.Input); };
        Closed += (_, _) => focusGuard.Stop();
    }

    void EnsureFocusOwnership()
    {
        if (!IsVisible || previewMode || activating) return;
        var hwnd = new WindowInteropHelper(this).Handle;
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0) return;
        Native.GetWindowThreadProcessId(foreground, out uint process);
        // Native file pickers, owned windows and menus must keep their own responder.
        if (process == (uint)Environment.ProcessId && foreground != hwnd) return;
        var popup = Native.GetLastActivePopup(hwnd);
        if (popup != hwnd && Native.IsWindowVisible(popup))
        { Native.Activate(popup, false); return; }
        if (foreground != hwnd && !Native.Activate(hwnd)) return;
        if (IsKeyboardFocusWithin) return;
        RestoreContentFocus();
    }

    void RestoreContentFocus()
    {
        if (lastFocus != null && lastFocus.TryGetTarget(out var remembered) && remembered.IsVisible && remembered.IsEnabled && remembered.Focusable)
        { remembered.Focus(); Keyboard.Focus(remembered); return; }
        if (confirmation != null) confirmation.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        else if (editing) settings.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        else { input.Focus(); Keyboard.Focus(input); }
    }
}

static class FocusTestPeer
{
    public static int Run()
    {
        var app = new Application();
        var window = new Window { Title = "Kopra Testgegenstelle", Width = 260, Height = 100, Content = new TextBox { Text = "Künstlicher Fokustest" }, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        timeout.Tick += (_, _) => app.Shutdown(); timeout.Start();
        app.Run(window); return 0;
    }
}