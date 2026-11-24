using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kopra;

sealed class HistoryStore : IDisposable
{
    readonly string root, objects; readonly Sqlite db; readonly StorageBudget? budget; readonly object stateGate = new();
    public HistoryStore(string root, Preferences? policy = null, Func<long>? freeDisk = null)
    {
        this.root = root;
        objects = Path.Combine(root, "objects");
        Directory.CreateDirectory(objects);
        db = new(Path.Combine(root, "history.sqlite"));
        db.Run("CREATE TABLE IF NOT EXISTS history(id TEXT PRIMARY KEY, fingerprint TEXT UNIQUE, payload TEXT NOT NULL)");
        db.Run("CREATE TABLE IF NOT EXISTS usage(path TEXT PRIMARY KEY, count INTEGER NOT NULL)");
        db.Run("CREATE TABLE IF NOT EXISTS recent(path TEXT PRIMARY KEY,touched TEXT NOT NULL,payload TEXT NOT NULL)");
        if (policy != null)
        {
            budget = new(root, policy, All, x => { if (DeleteUnpinned(x)) Compact(); }, freeDisk);
            RecoverOrphans();
        }
    }
    public StorageSnapshot Storage => budget?.Snapshot() ?? new(StorageBudget.DirectoryBytes(root), 0, new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace, All().Count, All().Where(x => x.Pinned).Sum(x => x.Bytes), 0);
    public void RecordRecent(SearchItem item)
    {
        if (item.Kind == "Einstellung")
            return;
        var recent = new RecentItem(item, DateTime.UtcNow);
        string payload = JsonSerializer.Serialize(recent);
        try
        {
            using var ticket = budget?.Reserve(Guid.NewGuid().ToString("N"), Encoding.UTF8.GetByteCount(payload) * 8L, false, false);
            db.Run("INSERT INTO recent(path,touched,payload) VALUES(?,?,?) ON CONFLICT(path) DO UPDATE SET touched=excluded.touched,payload=excluded.payload", item.Path, recent.At.Ticks.ToString("D19"), payload);
            db.Run("DELETE FROM recent WHERE path NOT IN (SELECT path FROM recent ORDER BY touched DESC LIMIT 100)");
        }
        catch (IOException) {/* Optional ranking must not evict clipboard contents or exceed its budget. */}
    }
    public List<RecentItem> Recent() => db.Run("SELECT payload FROM recent ORDER BY touched DESC LIMIT 50").Select(x => JsonSerializer.Deserialize<RecentItem>(x)!).ToList();
    public List<ClipEntry> All() => db.Run("SELECT payload FROM history").Select(x => JsonSerializer.Deserialize<ClipEntry>(x)!).Where(x => x != null).OrderByDescending(x => x.Pinned).ThenByDescending(x => x.Created).ToList();
    public void Use(string path)
    {
        try
        {
            using var ticket = budget?.Reserve(Guid.NewGuid().ToString("N"), Encoding.UTF8.GetByteCount(path) * 8L + 32768, false, false);
            db.Run("INSERT INTO usage(path,count) VALUES(?,1) ON CONFLICT(path) DO UPDATE SET count=count+1", path);
            db.Run("DELETE FROM usage WHERE path NOT IN (SELECT path FROM usage ORDER BY count DESC LIMIT 500)");
        }
        catch (IOException) { }
    }
    public int Usage(string path)
    {
        var rows = db.Run("SELECT count FROM usage WHERE path=?", path);
        return rows.Count > 0 && int.TryParse(rows[0], out int n) ? n : 0;
    }
    public ClipEntry Put(ClipEntry item)
    {
        var old = All().FirstOrDefault(x => x.Fingerprint == item.Fingerprint);
        if (old != null)
        {
            if (item.Kind != "text")
                DeleteDirectory(item.Id);
            item = old with
            {
                Created = DateTime.UtcNow
            };
        }
        db.Run("INSERT INTO history(id,fingerprint,payload) VALUES(?,?,?) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload", item.Id, item.Fingerprint, JsonSerializer.Serialize(item));
        return item;
    }
    public ClipEntry AddText(string text)
    {
        var item = new ClipEntry(Guid.NewGuid().ToString("N"), "text", text.Replace('\r', ' ').Replace('\n', ' ').Trim(), text, [], Hash(Encoding.UTF8.GetBytes(text)), Encoding.UTF8.GetByteCount(text), DateTime.UtcNow);
        if (item.Title.Length > 120)
            item = item with
            {
                Title = item.Title[..120]
            };
        using var ticket = budget?.Reserve(item.Id, checked(item.Bytes * 8));
        return Put(item);
    }
    public ClipEntry AddImage(byte[] png, long limit)
    {
        if (png.LongLength > limit)
            throw new IOException(L.Text("Das Bild überschreitet das Eintragslimit."));
        string id = Guid.NewGuid().ToString("N"), folder = Path.Combine(objects, id);
        using var ticket = budget?.Reserve(id, png.LongLength);
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "Bild.png");
            File.WriteAllBytes(path, png);
            var item = new ClipEntry(id, "image", L.Text("Kopiertes Bild"), "", [path], Hash(png), png.Length, DateTime.UtcNow);
            return Put(item);
        }
        catch { DeleteDirectory(id); throw; }
    }
    public async Task<ClipEntry> AddFiles(string[] sources, long limit, Action<long, long>? progress = null, CancellationToken cancel = default)
    {
        if (sources.Length == 0)
            throw new IOException(L.Text("Keine Dateien ausgewählt."));
        string id = Guid.NewGuid().ToString("N"), folder = Path.Combine(objects, id);
        try
        {
            var plan = new List<(string source, string relative, bool directory)>();
            long total = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string source in sources)
            {
                cancel.ThrowIfCancellationRequested();
                string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
                if (string.IsNullOrEmpty(name))
                    name = "Laufwerk";
                string baseName = name;
                int suffix = 2;
                while (!names.Add(name))
                    name = $"{baseName} ({suffix++})";
                void Scan(string path, string relative, int depth = 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (depth > 64)
                        throw new IOException(L.Text("Der Ordner ist zu tief verschachtelt."));
                    var info = new FileInfo(path);
                    var attributes = File.GetAttributes(path);
                    FileSafety.Check(path, attributes);
                    bool directory = (attributes & FileAttributes.Directory) != 0;
                    plan.Add((path, relative, directory));
                    if (plan.Count > 100000)
                        throw new IOException(L.Text("Zu viele Dateien in einem Eintrag."));
                    if (directory)
                    {
                        foreach (var child in Directory.EnumerateFileSystemEntries(path))
                            Scan(child, Path.Combine(relative, Path.GetFileName(child)), depth + 1);
                    }
                    else
                    {
                        total = checked(total + info.Length);
                        if (total > limit)
                            throw new IOException(L.Text("Die Auswahl überschreitet das Eintragslimit."));
                    }
                }
                Scan(source, name);
            }
            using var ticket = budget?.Reserve(id, checked(total + sources.Sum(x => (long)x.Length) * 16 + plan.Count * StorageBudget.AllocationUnit(root)));
            if (budget == null && new DriveInfo(Path.GetPathRoot(folder)!).AvailableFreeSpace < total + 64L * 1048576)
                throw new IOException(L.Text("Nicht genügend freier Speicher für die Dateisicherung."));
            Directory.CreateDirectory(folder);
            long copied = 0, lastCheck = 0;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var top = new List<string>();
            foreach (var name in names)
                top.Add(Path.Combine(folder, name));
            foreach (var item in plan)
            {
                cancel.ThrowIfCancellationRequested();
                string target = Path.Combine(folder, item.relative);
                hash.AppendData(Encoding.UTF8.GetBytes((item.directory ? "D" : "F") + item.relative + "\0"));
                if (item.directory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = new FileStream(item.source, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
                byte[] buffer = new byte[131072];
                int read;
                while ((read = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    copied = checked(copied + read);
                    if (copied > limit || copied > total)
                        throw new IOException(L.Text("Die Auswahl wurde während der Sicherung größer. Bitte erneut kopieren."));
                    if (copied - lastCheck >= 4L * 1048576)
                    {
                        budget?.CheckWriting();
                        lastCheck = copied;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                    hash.AppendData(buffer.AsSpan(0, read));
                    progress?.Invoke(copied, total);
                }
            }
            string title = sources.Length == 1 ? Path.GetFileName(Path.TrimEndingDirectorySeparator(sources[0])) : L.Format("{0} kopierte Objekte", sources.Length);
            var entry = new ClipEntry(id, "files", title, "", top.ToArray(), Convert.ToHexString(hash.GetHashAndReset()), copied, DateTime.UtcNow);
            return Put(entry);
        }
        catch { DeleteDirectory(id); throw; }
    }
    public void Pin(ClipEntry item)
    {
        lock (stateGate)
            db.Run("UPDATE history SET payload=? WHERE id=?", JsonSerializer.Serialize(item with
            {
                Pinned = !item.Pinned
            }), item.Id);
    }
    public event Action<ClipEntry>? Deleting;
    public ClipEntry SetOrigin(ClipEntry item, string device, string name)
    {
        var updated = item with
        {
            OriginDevice = device,
            OriginName = name
        };
        db.Run("UPDATE history SET payload=? WHERE id=?", JsonSerializer.Serialize(updated), item.Id);
        return updated;
    }
    public ClipEntry ImportShared(SharedClip clip, string device, string name, long limit)
    {
        SyncWire.Check(clip);
        long bytes = System.Text.Encoding.UTF8.GetByteCount(clip.Text);
        foreach (var file in clip.Files)
            bytes = checked(bytes + file.Data.Length * 3L / 4);
        if (bytes > limit)
            throw new IOException(L.Text("Empfang überschreitet das Eintragslimit."));
        if (clip.Kind == "text")
            return SetOrigin(AddText(clip.Text), device, name);
        string id = Guid.NewGuid().ToString("N"), folder = Path.Combine(objects, id);
        using var ticket = budget?.Reserve(id, checked(bytes + clip.Files.Count * StorageBudget.AllocationUnit(root)));
        Directory.CreateDirectory(folder);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var file in clip.Files.OrderBy(x => x.Path.Count(c => c == '/')))
            {
                string target = Path.GetFullPath(Path.Combine(folder, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new IOException(L.Text("Ungültiger Dateipfad"));
                if (file.Directory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                byte[] data = Convert.FromBase64String(file.Data);
                budget?.CheckWriting();
                File.WriteAllBytes(target, data);
                hash.AppendData(Encoding.UTF8.GetBytes(file.Path));
                hash.AppendData(data);
            }
            var tops = clip.Files.Select(x => x.Path.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Select(x => Path.Combine(folder, x)).ToArray();
            var item = Put(new ClipEntry(id, clip.Kind, clip.Title, "", tops, Convert.ToHexString(hash.GetHashAndReset()), bytes, DateTime.UtcNow));
            return SetOrigin(item, device, name);
        }
        catch { DeleteDirectory(id); throw; }
    }
    public void Delete(ClipEntry item)
    {
        lock (stateGate)
        {
            Deleting?.Invoke(item);
            DeleteDirectory(item.Id);
            db.Run("DELETE FROM history WHERE id=?", item.Id);
        }
    }
    bool DeleteUnpinned(ClipEntry item)
    {
        lock (stateGate)
        {
            var current = All().FirstOrDefault(x => x.Id == item.Id);
            if (current == null || current.Pinned)
                return false;
            Delete(current);
            return true;
        }
    }
    public void Clear(bool includePinned = false)
    {
        foreach (var item in All().Where(x => includePinned || !x.Pinned))
            Delete(item);
    }
    public void Prune(Preferences prefs)
    {
        if (budget?.HasActive == true)
            return;
        var items = All();
        long size = items.Sum(x => x.Bytes);
        int count = items.Count(x => !x.Pinned);
        foreach (var item in items.Where(x => !x.Pinned).OrderBy(x => x.Created))
        {
            int days = item.Kind == "text" ? prefs.RetentionDays : prefs.BinaryRetentionDays;
            if (item.Created < DateTime.UtcNow.AddDays(-Math.Max(1, days)) || size > prefs.StorageLimitMb * 1048576 || count > prefs.MaxHistoryEntries)
            {
                if (DeleteUnpinned(item))
                {
                    size -= item.Bytes;
                    count--;
                }
            }
        }
        RecoverOrphans();
        Compact();
        if (budget != null)
        {
            foreach (var item in All().Where(x => !x.Pinned).OrderBy(x => x.Created))
            {
                if (Storage.UsedBytes + StorageBudget.DatabaseHeadroom <= Storage.BudgetBytes)
                    break;
                DeleteUnpinned(item);
            }
            Compact();
        }
    }
    void RecoverOrphans()
    {
        var known = All().Select(x => x.Id).ToHashSet();
        foreach (string folder in Directory.EnumerateDirectories(objects))
        {
            string id = Path.GetFileName(folder);
            if (Guid.TryParseExact(id, "N", out _) && !known.Contains(id) && budget?.IsActive(id) != true)
            {
                try
                {
                    DeleteDirectory(id);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    public void Compact()
    {
        if (budget?.HasActive == true)
            return;
        db.Run("PRAGMA wal_checkpoint(TRUNCATE)");
        db.Run("PRAGMA incremental_vacuum(256)");
    }
    void DeleteDirectory(string id)
    {
        if (id.Length != 32 || !Guid.TryParseExact(id, "N", out _))
            throw new IOException(L.Text("Ungültiger Speicherpfad"));
        string path = Path.GetFullPath(Path.Combine(objects, id));
        if (!path.StartsWith(Path.GetFullPath(objects) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException(L.Text("Ungültiger Speicherpfad"));
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }
    static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    public void Dispose() => db.Dispose();
}
