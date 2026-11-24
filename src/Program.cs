using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Windows.Interop;
using Microsoft.Win32;
namespace Kopra;

static class Program
{
    static System.Windows.Forms.NotifyIcon? tray;
    static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Kopra");
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--focus-test-peer")) return FocusTestPeer.Run();
        if (args.Contains("--self-test"))
            return Regression.Run();
        if (args.Length == 2 && args[0] == "--generate-icon")
        {
            AppIconGenerator.Generate(args[1]);
            return 0;
        }
        if (args.Length == 2 && args[0] == "--sync-lan-test")
            return SyncLanTest.Run(args[1]);
        if (args.Length == 2 && args[0] == "--crypto-peer-check")
            return SyncRegression.VerifyPeer(args[1]);
        if (args.Length == 2 && args[0] == "--diagnostics")
            return Diagnostics.Run(args[1]);
        bool isolated = args.Contains("--preview") || args.Contains("--interaction-test") || args.Contains("--focus-test");
        using var mutex = new Mutex(true, isolated ? "Local\\Kopra.Preview." + Guid.NewGuid().ToString("N") : "Local\\Kopra." + Environment.UserName, out bool first);
        if (!first)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                pipe.Connect(1500);
                using var writer = new StreamWriter(pipe);
                writer.WriteLine(args.Contains("--quit-safe") ? "quit" : args.Contains("--history") ? "history" : "search");
            }
            catch { }
            return 0;
        }
        if (args.Contains("--quit-safe"))
            return 0;
        string activeRoot = isolated ? Path.Combine(Path.GetTempPath(), "Kopra-ui-" + Guid.NewGuid().ToString("N")) : Root;
        Directory.CreateDirectory(activeRoot);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) => { try { File.AppendAllText(Path.Combine(activeRoot, "errors.log"), DateTime.UtcNow.ToString("O") + " " + e.Exception.GetType().Name + " " + e.Exception.StackTrace + Environment.NewLine); } catch { } e.Handled = true; Notify(L.Text("Kopra: Aktion konnte nicht abgeschlossen werden")); };
        Preferences prefs = isolated ? new Preferences { MinFreeDiskMb = 0, AutoStart = false } : Preferences.Load(Root);
        L.Use(prefs.Language);
        using var store = new HistoryStore(activeRoot, prefs);
        using var clipboard = new ClipboardService(store, prefs);
        var search = new SearchService(prefs, store);
        var window = new LauncherWindow(store, prefs, search, clipboard, activeRoot);
        app.MainWindow = window;
        using var sync = isolated ? null : new DeviceSync(Root, store, clipboard, prefs);
        window.DeviceSync = sync;
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(handle);
        Shortcuts? shortcuts = null;
        RecentActivity? recent = null;
        System.Windows.Threading.DispatcherTimer? cleanup = null;
        using var stop = new CancellationTokenSource();
        if (!isolated)
        {
            shortcuts = new Shortcuts(handle, prefs, mode => window.Toggle(mode));
            window.Shortcuts = shortcuts;
            source.AddHook((nint hwnd, int message, nint w, nint l, ref bool handled) => { if (message == Native.WM_CLIPBOARDUPDATE) clipboard.Capture(); if (message == Native.WM_HOTKEY) handled = shortcuts.Handle((int)w); return 0; });
            bool listener = Native.AddClipboardFormatListener(handle);
            File.WriteAllText(Path.Combine(Root, "startup-health.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                ClipboardListener = listener,
                ClipboardHotkey = shortcuts.HistoryRegistered,
                WindowsKeyHook = shortcuts.WindowsHookInstalled,
                ProcessId = Environment.ProcessId,
                Started = DateTime.UtcNow,
                StoragePolicyVersion = prefs.StoragePolicyVersion,
                CleanupIntervalMinutes = 15
            }));
            tray = new System.Windows.Forms.NotifyIcon { Text = "Kopra", Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application, Visible = true };
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(L.Text("Suche öffnen"), null, (_, _) => window.OpenMode(false));
            menu.Items.Add(L.Text("Zwischenablageverlauf"), null, (_, _) => window.OpenMode(true));
            menu.Items.Add(L.Text("Verlauf pausieren"), null, (_, _) => { prefs.Paused = !prefs.Paused; prefs.Save(Root); ((System.Windows.Forms.ToolStripMenuItem)menu.Items[2]).Checked = prefs.Paused; });
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add(L.Text("Beenden"), null, (_, _) => app.Shutdown());
            tray.ContextMenuStrip = menu;
            L.Changed += () => { menu.Items[0].Text = L.Text("Suche öffnen"); menu.Items[1].Text = L.Text("Zwischenablageverlauf"); menu.Items[2].Text = L.Text("Verlauf pausieren"); menu.Items[4].Text = L.Text("Beenden"); };
            tray.DoubleClick += (_, _) => window.OpenMode(false);
            _ = Task.Run(async () => { while (!stop.IsCancellationRequested) { try { using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly); await pipe.WaitForConnectionAsync(stop.Token); using var reader = new StreamReader(pipe); string? command = await reader.ReadLineAsync(stop.Token); if (command is "history" or "search") await app.Dispatcher.InvokeAsync(() => window.OpenMode(command == "history")); else if (command == "quit" && !clipboard.IsBusy) await app.Dispatcher.InvokeAsync(() => app.Shutdown()); } catch (OperationCanceledException) { break; } catch (IOException) { await Task.Delay(500); } } });
        }
        app.Startup += async (_, _) =>
        {
            await search.Refresh();
            if (args.Contains("--focus-test"))
            {
                string output = args.SkipWhile(x => x != "--focus-test").Skip(1).First();
                try { var result = await window.TestFocus(); File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
                catch (Exception error) { File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(new { Passed = false, Error = error.GetType().Name })); Environment.ExitCode = 1; }
                finally { app.Shutdown(); }
                return;
            }
            if (args.Contains("--interaction-test"))
            {
                string output = args.SkipWhile(x => x != "--interaction-test").Skip(1).First();
                store.Clear(true);
                store.AddText("Erster Testeintrag");
                store.AddText("Zweiter Testeintrag");
                var result = await window.TestInteraction();
                File.WriteAllText(output, System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                app.Shutdown();
                return;
            }
            if (args.Contains("--preview"))
            {
                string folder = args.SkipWhile(x => x != "--preview").Skip(1).FirstOrDefault() ?? Root;
                Directory.CreateDirectory(folder);
                store.Clear(true);
                store.AddText("A note to keep for later.");
                store.AddText("https://example.com/design");
                string samples = Path.Combine(Path.GetTempPath(), "Kopra-preview-source-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(samples);
                try
                {
                    string documents = Path.Combine(samples, "Project notes");
                    Directory.CreateDirectory(documents);
                    File.WriteAllText(Path.Combine(documents, "Notes.txt"), "Example project notes");
                    var saved = await store.AddFiles([documents], 1048576);
                    store.RecordRecent(new("Notes.txt", Path.Combine(saved.Files[0], "Notes.txt"), "Datei", L.Text("Zuletzt geöffnet")));
                    var visual = new DrawingVisual();
                    using (var draw = visual.RenderOpen())
                    {
                        draw.DrawRectangle(new LinearGradientBrush(Color.FromRgb(69, 173, 225), Color.FromRgb(88, 80, 185), 45), null, new Rect(0, 0, 640, 400));
                        draw.DrawEllipse(new SolidColorBrush(Color.FromRgb(236, 202, 155)), null, new Point(465, 105), 45, 45);
                        draw.DrawGeometry(new SolidColorBrush(Color.FromRgb(37, 64, 114)), null, Geometry.Parse("M0,400 L0,300 170,120 340,310 440,210 640,400 Z"));
                    }
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(visual);
                    var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    string image = Path.Combine(samples, "Landscape.jpeg");
                    using (var output = File.Create(image))
                        encoder.Save(output);
                    await store.AddFiles([image], 1048576);
                }
                finally { Directory.Delete(samples, true); }
                await window.RenderPreview(Path.Combine(folder, "kopra-search.png"), false);
                await window.RenderPreview(Path.Combine(folder, "kopra-history.png"), true);
                await window.RenderPreview(Path.Combine(folder, "kopra-history-text.png"), true, 3);
                await window.RenderPreview(Path.Combine(folder, "kopra-history-folder.png"), true, 1);
                await window.RenderSettingsPreview(Path.Combine(folder, "kopra-general.png"), 0);
                await window.RenderSettingsPreview(Path.Combine(folder, "kopra-storage.png"), 1);
                await window.RenderSettingsPreview(Path.Combine(folder, "kopra-privacy.png"), 2);
                app.Shutdown();
                return;
            }
            recent = new RecentActivity(store);
            await clipboard.Maintain();
            prefs.Save(Root);
            if (prefs.AutoStart)
                SetAutoStart(true);
            cleanup = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            cleanup.Tick += async (_, _) => await clipboard.Maintain();
            cleanup.Start();
            if (shortcuts?.StartupError.Length > 0)
                Notify(shortcuts.StartupError);

            if (!args.Contains("--background"))
                window.OpenMode(args.Contains("--history"));
        };
        try
        {
            app.Run();
        }
        finally { stop.Cancel(); cleanup?.Stop(); recent?.Dispose(); Native.RemoveClipboardFormatListener(handle); shortcuts?.Dispose(); if (tray != null) { tray.Visible = false; tray.Dispose(); } }
        return 0;
    }
    static string PipeName => "Kopra." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
    public static void Notify(string message)
    {
        if (tray != null)
        {
            tray.BalloonTipTitle = "Kopra";
            tray.BalloonTipText = message;
            tray.ShowBalloonTip(2500);
        }
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
        if (enabled)
            key.SetValue("Kopra", "\"" + Environment.ProcessPath + "\" --background");
        else
            key.DeleteValue("Kopra", false);
    }
}
