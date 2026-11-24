using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Kopra;

sealed class SearchService(Preferences prefs, HistoryStore store)
{
    readonly object gate = new(); List<SearchItem> apps = []; string? lastFolders; List<SearchItem> extras = [];
    public string IndexStatus { get; private set; } = "";
    public int SettingsCount
    {
        get
        {
            lock (gate)
                return apps.Count(x => x.Kind == "Einstellung");
        }
    }
    public async Task Refresh() => await Task.Run(() =>
    {
        var found = new List<SearchItem>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu) })
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                    found.Add(new(Path.GetFileNameWithoutExtension(path), path, "App", "Programm"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        object? shell = null, appFolder = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            dynamic folder = appFolder = ((dynamic)shell!).NameSpace("shell:AppsFolder");
            dynamic collection = items = folder.Items();
            for (int i = 0; i < collection.Count; i++)
            {
                object? entry = null;
                try
                {
                    dynamic item = entry = collection.Item(i);
                    string name = item.Name;
                    string id = item.Path;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(id))
                        found.Add(new(name, "shell:AppsFolder\\" + id, "App", "Programm"));
                }
                finally { if (entry != null) Marshal.FinalReleaseComObject(entry); }
            }
        }
        catch (COMException) { }
        finally { if (items != null) Marshal.FinalReleaseComObject(items); if (appFolder != null) Marshal.FinalReleaseComObject(appFolder); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        foreach (var item in new[] { (L.Text("Einstellungen"), "ms-settings:"), ("Bluetooth", "ms-settings:bluetooth"), (L.Text("WLAN"), "ms-settings:network-wifi"), ("Sound", "ms-settings:sound"), (L.Text("Anzeige"), "ms-settings:display"), (L.Text("Apps deinstallieren"), "ms-settings:appsfeatures"), ("Windows Update", "ms-settings:windowsupdate"), (L.Text("Zwischenablage"), "ms-settings:clipboard"), (L.Text("Datenschutz"), "ms-settings:privacy"), (L.Text("Speicher"), "ms-settings:storagesense"), (L.Text("Tastatur"), "ms-settings:keyboard"), (L.Text("Maus"), "ms-settings:mousetouchpad"), (L.Text("Benachrichtigungen"), "ms-settings:notifications"), (L.Text("Standard-Apps"), "ms-settings:defaultapps"), (L.Text("Autostart"), "ms-settings:startupapps"), (L.Text("Energie"), "ms-settings:powersleep"), (L.Text("Personalisierung"), "ms-settings:personalization"), (L.Text("Taskleiste"), "ms-settings:taskbar") })
            found.Add(new(item.Item1, item.Item2, "Einstellung", L.Text("Windows-Einstellungen")));
        found.Add(new("Explorer", "explorer.exe", "App", L.Text("Dateien und Ordner")));
        found.AddRange(SettingsCatalog.Load());
        string folders = string.Join('|', prefs.SearchFolders);
        if (folders != lastFolders)
        {
            var files = new List<SearchItem>();
            foreach (string root in prefs.SearchFolders)
            {
                if (!Directory.Exists(root))
                    continue;
                var queue = new Queue<(string, int)>();
                queue.Enqueue((root, 0));
                while (queue.Count > 0 && files.Count < 40000)
                {
                    var (dir, depth) = queue.Dequeue();
                    try
                    {
                        foreach (string path in Directory.EnumerateFileSystemEntries(dir))
                        {
                            var attr = File.GetAttributes(path);
                            if ((attr & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0)
                                continue;
                            bool folder = (attr & FileAttributes.Directory) != 0;
                            files.Add(new(Path.GetFileName(path), path, folder ? "Ordner" : Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? "App" : "Datei", path));
                            if (folder && depth < 8)
                                queue.Enqueue((path, depth + 1));
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            lock (gate)
            {
                extras = files;
                lastFolders = folders;
            }
        }
        lock (gate)
            apps = found.DistinctBy(x => x.Kind == "Einstellung" ? x.Title + "|" + x.Path : x.Title, StringComparer.OrdinalIgnoreCase).ToList();
    });
    public async Task<List<SearchItem>> Search(string query, CancellationToken token)
    {
        query = query.Trim();
        List<SearchItem> local;
        lock (gate)
            local = [.. apps, .. extras];
        if (query.Length == 0)
            return await Task.Run(() => store.Recent().Concat(RecentActivity.WindowsFiles()).Where(x => x.Item.Kind == "App" || File.Exists(x.Item.Path) || Directory.Exists(x.Item.Path)).OrderByDescending(x => x.At).DistinctBy(x => x.Item.Path, StringComparer.OrdinalIgnoreCase).Take(25).Select(x => x.Item).ToList(), token);
        var ranked = local.Select(x => (item: x, score: Math.Max(Rank(x.Title, query), Math.Max(0, Rank(x.Keywords, query) - 25)) + Math.Min(20, store.Usage(x.Path)) + (x.Kind == "App" ? 5 : 0))).Where(x => Rank(x.item.Title, query) > 0 || Rank(x.item.Keywords, query) > 0).OrderByDescending(x => x.score).ThenBy(x => x.item.Title).Take(45).Select(x => x.item).ToList();
        if (query.Length >= 2)
        {
            var indexed = await Task.Run(() => Indexed(query, token), token);
            ranked.AddRange(indexed);
        }
        return ranked.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).Take(60).ToList();
    }
    public static int Rank(string title, string query)
    {
        if (query.Length == 0)
            return 1;
        if (title.Equals(query, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (title.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 80;
        if (title.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 55;
        var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(x => title.Contains(x, StringComparison.OrdinalIgnoreCase)) ? 35 : 0;
    }
    List<SearchItem> Indexed(string query, CancellationToken token)
    {
        object? connection = null, recordset = null;
        var result = new List<SearchItem>();
        try
        {
            var type = Type.GetTypeFromProgID("ADODB.Connection");
            if (type == null)
                return result;
            dynamic conn = connection = Activator.CreateInstance(type)!;
            conn.ConnectionTimeout = 2;
            conn.CommandTimeout = 2;
            conn.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';");
            string escaped = query.Replace("'", "''").Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            dynamic rows = recordset = conn.Execute("SELECT TOP 30 System.ItemNameDisplay, System.ItemPathDisplay FROM SYSTEMINDEX WHERE System.FileName LIKE '%" + escaped + "%' ORDER BY System.DateModified DESC");
            while (!rows.EOF && !token.IsCancellationRequested)
            {
                string title = Convert.ToString(rows.Fields.Item(0).Value) ?? "", path = Convert.ToString(rows.Fields.Item(1).Value) ?? "";
                if ((File.Exists(path) || Directory.Exists(path)) && !string.IsNullOrEmpty(title))
                    result.Add(new(title, path, Directory.Exists(path) ? "Ordner" : "Datei", path));
                rows.MoveNext();
            }
            IndexStatus = "";
        }
        catch (COMException) { IndexStatus = L.Text("Windows-Suchindex nicht verfügbar · Apps und eigene Suchordner funktionieren weiter"); }
        catch (Exception e) when (e is not OutOfMemoryException) { IndexStatus = L.Text("Dateisuche derzeit nicht verfügbar"); }
        finally { if (recordset != null) { try { ((dynamic)recordset).Close(); } catch { } Marshal.FinalReleaseComObject(recordset); } if (connection != null) { try { ((dynamic)connection).Close(); } catch { } Marshal.FinalReleaseComObject(connection); } }
        return result;
    }
    public void Open(SearchItem item)
    {
        var start = new ProcessStartInfo(item.Path.StartsWith("shell:") ? "explorer.exe" : item.Path) { UseShellExecute = true };
        if (item.Path.StartsWith("shell:"))
            start.ArgumentList.Add(item.Path);
        Process.Start(start);
        store.Use(item.Path);
        store.RecordRecent(item);
    }
}
