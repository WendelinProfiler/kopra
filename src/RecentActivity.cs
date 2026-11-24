using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Kopra;

sealed class RecentActivity : IDisposable
{
    readonly System.Threading.Timer timer; uint last;
    public RecentActivity(HistoryStore store)
    {
        timer = new(_ => { try { Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out uint pid); if (pid == 0 || pid == last || pid == Environment.ProcessId) return; last = pid; using var process = Process.GetProcessById((int)pid); if (new[] { "explorer", "StartMenuExperienceHost", "SearchHost", "ShellExperienceHost", "TextInputHost", "ApplicationFrameHost", "LockApp" }.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase)) return; string? path = process.MainModule?.FileName; if (path == null || !File.Exists(path)) return; string title = FileVersionInfo.GetVersionInfo(path).FileDescription ?? process.ProcessName; store.RecordRecent(new(title, path, "App", L.Text("Zuletzt verwendet"))); } catch (Exception e) when (e is not OutOfMemoryException) { } }, null, 1000, 2000);
    }
    public void Dispose() => timer.Dispose();
    public static List<RecentItem> WindowsFiles()
    {
        var results = new List<RecentItem>();
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            foreach (var file in Directory.EnumerateFiles(folder, "*.lnk").Select(x => new FileInfo(x)).OrderByDescending(x => x.LastWriteTimeUtc).Take(45))
            {
                object? link = null;
                try
                {
                    dynamic shortcut = link = ((dynamic)shell!).CreateShortcut(file.FullName);
                    string target = shortcut.TargetPath;
                    if (File.Exists(target) || Directory.Exists(target))
                        results.Add(new(new(Path.GetFileName(target), target, Directory.Exists(target) ? "Ordner" : "Datei", target), file.LastWriteTimeUtc));
                }
                catch (COMException) { }
                finally { if (link != null) Marshal.FinalReleaseComObject(link); }
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException) { }
        finally { if (shell != null) Marshal.FinalReleaseComObject(shell); }
        return results;
    }
}
