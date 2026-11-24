namespace Kopra;

public sealed record StorageSnapshot(long UsedBytes, long BudgetBytes, long FreeDiskBytes, int Entries, long PinnedBytes, long ReservedBytes);
sealed class StorageBudget
{
    [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] static extern bool GetDiskFreeSpace(string root, out uint sectors, out uint bytes, out uint free, out uint total);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> units = new(StringComparer.OrdinalIgnoreCase);
    public static long AllocationUnit(string path) => units.GetOrAdd(Path.GetPathRoot(Path.GetFullPath(path))!, root => GetDiskFreeSpace(root, out uint sectors, out uint bytes, out _, out _) ? Math.Max(4096, (long)sectors * bytes) : 65536);
    public const long DatabaseHeadroom = 4L * 1048576;
    readonly string root; readonly Func<List<ClipEntry>> entries; readonly Action<ClipEntry> delete; readonly Func<long> freeDisk;
    readonly Dictionary<string, long> active = new(); readonly object gate = new();
    public Preferences Policy
    {
        get;
    }
    public StorageBudget(string root, Preferences policy, Func<List<ClipEntry>> entries, Action<ClipEntry> delete, Func<long>? freeDisk = null)
    {
        this.root = root;
        Policy = policy;
        this.entries = entries;
        this.delete = delete;
        this.freeDisk = freeDisk ?? (() => new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace);
    }
    // Conservatively count allocation and directory overhead as well as data;
    // many tiny/empty files must not evade the byte budget.
    public static long DirectoryBytes(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        long block = AllocationUnit(path);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        long total = block;
        foreach (string file in Directory.EnumerateFiles(path, "*", options))
        {
            long length = new FileInfo(file).Length;
            total = checked(total + Math.Max(block, ((length + block - 1) / block) * block));
        }
        total = checked(total + Directory.EnumerateDirectories(path, "*", options).LongCount() * block);
        return total;
    }
    long Reserved() => active.Sum(x => Math.Max(0, x.Value - DirectoryBytes(Path.Combine(root, "objects", x.Key))));
    public StorageSnapshot Snapshot()
    {
        lock (gate)
        {
            var all = entries();
            return new(DirectoryBytes(root), Math.Max(1, Policy.StorageLimitMb) * 1048576, freeDisk(), all.Count, all.Where(x => x.Pinned).Sum(x => x.Bytes), Reserved());
        }
    }
    public IDisposable Reserve(string id, long bytes, bool addsEntry = true, bool allowEviction = true)
    {
        lock (gate)
        {
            long incoming = checked(bytes + 65536);
            var snap = Snapshot();
            long reserve = Math.Max(0, Policy.MinFreeDiskMb) * 1048576;
            if (snap.FreeDiskBytes < incoming + reserve + DatabaseHeadroom)
                throw new IOException("Sicherung pausiert: Der freie Festplattenplatz würde unter die eingestellte Reserve fallen.");
            if (incoming + DatabaseHeadroom > snap.BudgetBytes)
                throw new IOException("Nicht gespeichert: Dieser Eintrag passt nicht in das gesamte Speicherbudget.");
            var candidates = entries().Where(x => !x.Pinned).OrderBy(x => x.Created).ToList();
            long required = snap.UsedBytes + snap.ReservedBytes + incoming + DatabaseHeadroom - snap.BudgetBytes;
            if (!allowEviction && required > 0)
                throw new IOException("Speicherbudget ausgeschöpft.");
            long removable = candidates.Sum(x => DirectoryBytes(Path.Combine(root, "objects", x.Id)) + System.Text.Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(x)) * 2L);
            if (required > 0 && required > removable)
                throw new IOException("Sicherung pausiert: Angeheftete Inhalte oder andere Verlaufsdaten belegen das Speicherbudget. Bitte Speicher freigeben oder das Budget erhöhen.");
            int unpinned = candidates.Count;
            foreach (var entry in candidates)
            {
                if (required <= 0 && (!addsEntry || unpinned < Math.Max(1, Policy.MaxHistoryEntries)))
                    break;
                delete(entry);
                var current = Snapshot();
                required = current.UsedBytes + current.ReservedBytes + incoming + DatabaseHeadroom - current.BudgetBytes;
                unpinned--;
            }
            snap = Snapshot();
            if (snap.UsedBytes + snap.ReservedBytes + incoming + DatabaseHeadroom > snap.BudgetBytes)
                throw new IOException("Sicherung pausiert: Speicherbudget bleibt ausgeschöpft.");
            active.Add(id, incoming);
            return new Ticket(this, id);
        }
    }
    public void CheckWriting()
    {
        lock (gate)
        {
            var snap = Snapshot();
            if (snap.FreeDiskBytes < Math.Max(0, Policy.MinFreeDiskMb) * 1048576 + 1048576)
                throw new IOException("Kopiervorgang gestoppt: Die freie Speicherreserve wurde erreicht.");
            if (snap.UsedBytes + snap.ReservedBytes + DatabaseHeadroom > snap.BudgetBytes)
                throw new IOException("Kopiervorgang gestoppt: Das Speicherbudget wurde erreicht.");
        }
    }
    public bool IsActive(string id)
    {
        lock (gate)
            return active.ContainsKey(id);
    }
    public bool HasActive
    {
        get
        {
            lock (gate)
                return active.Count > 0;
        }
    }
    sealed class Ticket(StorageBudget owner, string id) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
                owner.active.Remove(id);
        }
    }
}
