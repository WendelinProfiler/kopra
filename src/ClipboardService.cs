using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
namespace Kopra;

sealed class ClipboardService : IDisposable
{
    readonly HistoryStore store; readonly Preferences prefs; string restoredId = "";
    public ClipboardService(HistoryStore store, Preferences prefs)
    {
        this.store = store;
        this.prefs = prefs;
        store.Deleting += OnDeleting;
    }
    void OnDeleting(ClipEntry item)
    {
        if (restoredId != item.Id)
            return;
        Application.Current?.Dispatcher.BeginInvoke(() => { if (restoredId == item.Id && ignoredSequence == Native.GetClipboardSequenceNumber()) { try { Clipboard.Clear(); ignoredSequence = 0; restoredId = ""; } catch (COMException) { } } });
    }
    readonly SemaphoreSlim copies = new(1, 1); readonly CancellationTokenSource stop = new(); uint ignoredSequence;
    public bool IsBusy => copies.CurrentCount == 0;
    public event Action? Changed; public event Action<string>? Status;
    public async Task ImportShared(SharedClip clip, string name, string device, CancellationToken token)
    {
        await copies.WaitAsync(token);
        try
        {
            uint before = Native.GetClipboardSequenceNumber();
            var latest = store.All().Where(x => x.OriginDevice.Length == 0).Select(x => new DateTimeOffset(x.Created).ToUnixTimeMilliseconds()).DefaultIfEmpty(0).Max();
            var item = await Task.Run(() => store.ImportShared(clip, device, name, Math.Min(SyncWire.MaxContent, prefs.MaxEntryMb * 1048576)), token);
            Changed?.Invoke();
            if (prefs.SharedPaste && Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - clip.Created) < 60000 && clip.Created >= latest)
                await Application.Current.Dispatcher.InvokeAsync(async () => { if (before == Native.GetClipboardSequenceNumber()) await Restore(item, 0, false); }).Task.Unwrap();
        }
        finally { copies.Release(); }
    }
    [DllImport("user32")] static extern nint GetClipboardOwner();
    public async void Capture()
    {
        if (prefs.Paused)
            return;
        uint sequence = Native.GetClipboardSequenceNumber();
        if (sequence == ignoredSequence)
            return;
        try
        {
            Native.GetWindowThreadProcessId(GetClipboardOwner(), out uint pid);
            try
            {
                if (prefs.ExcludedApps.Contains(Process.GetProcessById((int)pid).ProcessName, StringComparer.OrdinalIgnoreCase))
                    return;
            }
            catch (ArgumentException) { }
            System.Windows.IDataObject? data = null;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    data = Clipboard.GetDataObject();
                    break;
                }
                catch (COMException) { await Task.Delay(40); if (sequence != Native.GetClipboardSequenceNumber()) return; }
            }
            if (data == null || sequence != Native.GetClipboardSequenceNumber() || data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || !HistoryAllowed(data))
                return;
            string[]? files = null;
            byte[]? image = null;
            string? text = null;
            if (data.GetDataPresent(DataFormats.FileDrop))
            {
                files = ReadFiles(data);
            }
            else if (Clipboard.ContainsImage())
            {
                var bitmap = Clipboard.GetImage();
                if (bitmap != null)
                {
                    if ((long)bitmap.PixelWidth * bitmap.PixelHeight > 64000000)
                    {
                        Status?.Invoke("Bild zu groß für den Verlauf");
                        return;
                    }
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    image = stream.ToArray();
                }
            }
            else if (data.GetDataPresent(DataFormats.UnicodeText))
                text = data.GetData(DataFormats.UnicodeText) as string;
            if (files == null && image == null && string.IsNullOrEmpty(text))
                return;
            long limit = Math.Clamp(prefs.MaxEntryMb, 1, 1048576) * 1048576;
            await copies.WaitAsync(stop.Token);
            try
            {
                if (prefs.Paused)
                    return;
                await Task.Run(() => store.Prune(prefs));
                ClipEntry? captured = null;
                if (files != null)
                {
                    Status?.Invoke("Dateien werden gesichert …");
                    captured = await Task.Run(() => store.AddFiles(files, limit, (current, total) => { if (total > 0) Status?.Invoke($"Dateien sichern · {Math.Min(100, current * 100 / total)} %"); }, stop.Token));
                    foreach (string path in files)
                        store.RecordRecent(new(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), path, Directory.Exists(path) ? "Ordner" : "Datei", "Zuletzt kopiert"));
                }
                else if (image != null)
                    captured = await Task.Run(() => store.AddImage(image, limit));
                else if (text != null)
                {
                    if (System.Text.Encoding.UTF8.GetByteCount(text) > Math.Min(limit, 16 * 1048576))
                    {
                        Status?.Invoke("Text zu groß für den Verlauf");
                        return;
                    }
                    captured = await Task.Run(() => store.AddText(text));
                }
                if (captured != null)
                    store.SetOrigin(captured, "", "");
                store.Prune(prefs);
                Status?.Invoke("");
                Changed?.Invoke();
            }
            finally { copies.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Status?.Invoke("Nicht gespeichert: " + e.Message); }
    }
    public async Task<bool> Maintain(bool clear = false, bool includePinned = false)
    {
        if (!await copies.WaitAsync(0))
        {
            Status?.Invoke("Bereinigung wartet: Eine Sicherung läuft noch.");
            return false;
        }
        try
        {
            await Task.Run(() => { if (clear) store.Clear(includePinned); store.Prune(prefs); });
            Changed?.Invoke();
            Status?.Invoke("");
            return true;
        }
        catch (Exception e) { Status?.Invoke("Bereinigung: " + e.Message); return false; }
        finally { copies.Release(); }
    }
    internal static string[]? ReadFiles(System.Windows.IDataObject data) => data.GetData(DataFormats.FileDrop) switch { string[] paths => paths, System.Collections.Specialized.StringCollection list => list.Cast<string>().ToArray(), _ => null };
    static bool HistoryAllowed(System.Windows.IDataObject data)
    {
        if (!data.GetDataPresent("CanIncludeInClipboardHistory"))
            return true;
        var value = data.GetData("CanIncludeInClipboardHistory");
        return value switch
        {
            int n => n != 0,
            uint n => n != 0,
            MemoryStream stream => ReadDword(stream) != 0,
            byte[] bytes when bytes.Length >= 4 => BitConverter.ToUInt32(bytes) != 0,
            _ => false
        };
    }
    static uint ReadDword(MemoryStream stream)
    {
        long position = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> bytes = stackalloc byte[4];
            return stream.Read(bytes) == 4 ? BitConverter.ToUInt32(bytes) : 0;
        }
        finally { stream.Position = position; }
    }
    public async Task<bool> Restore(ClipEntry item, nint target, bool paste)
    {
        var data = new DataObject();
        if (item.Kind == "text")
            data.SetText(item.Text);
        else if (item.Kind == "image")
        {
            if (!File.Exists(item.Files[0]))
                throw new IOException("Gespeichertes Bild fehlt.");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(item.Files[0]);
            bitmap.EndInit();
            bitmap.Freeze();
            data.SetImage(bitmap);
        }
        else
        {
            if (item.Files.Any(x => !File.Exists(x) && !Directory.Exists(x)))
                throw new IOException("Gespeicherte Datei fehlt.");
            var paths = new StringCollection();
            paths.AddRange(item.Files);
            data.SetFileDropList(paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1u)));
        }
        // Keep restored entries local and avoid recapturing our own restore.
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0u)));
        for (int retry = 0; ; retry++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                ignoredSequence = Native.GetClipboardSequenceNumber();
                restoredId = item.Id;
                break;
            }
            catch (COMException) when (retry < 6) { await Task.Delay(50); }
        }
        if (!paste || target == 0)
            return false;
        if (!Native.SetForegroundWindow(target))
            return false;
        for (int i = 0; i < 20 && !Native.ModifiersReleased(); i++)
            await Task.Delay(25);
        if (!Native.ModifiersReleased() || Native.GetForegroundWindow() != target)
            return false;
        bool control = Native.Key(0x11);
        try
        {
            if (!control)
                return false;
            bool down = Native.Key(0x56);
            bool up = Native.Key(0x56, true);
            return down && up;
        }
        finally { Native.Key(0x11, true); }
    }
    public void Dispose()
    {
        store.Deleting -= OnDeleting;
        stop.Cancel();
    }
}
