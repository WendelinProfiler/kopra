using System.Text.Json;
namespace Kopra;

static class StorageRegression
{
    public static int Run(string parent)
    {
        int tests = 0;
        void Check(bool passed, string name)
        {
            if (!passed)
                throw new Exception(name);
            tests++;
        }
        Preferences Policy(int mb = 12) => new()
        {
            StorageLimitMb = mb,
            MinFreeDiskMb = 5,
            MaxEntryMb = 8,
            MaxHistoryEntries = 2000
        };
        string Root(string name) => Path.Combine(parent, "budget-" + name);
        void Age(string root, ClipEntry entry, int days)
        {
            using var data = new Sqlite(Path.Combine(root, "history.sqlite"));
            data.Run("UPDATE history SET payload=? WHERE id=?", JsonSerializer.Serialize(entry with
            {
                Created = DateTime.UtcNow.AddDays(-days)
            }), entry.Id);
        }
        bool Refused(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (IOException) { return true; }
        }
        using (var store = new HistoryStore(Root("eviction"), Policy(), () => 100L * 1073741824))
        {
            var first = store.AddImage(new byte[3 * 1048576], 4 * 1048576);
            var second = store.AddImage(Enumerable.Repeat((byte)1, 3 * 1048576).ToArray(), 4 * 1048576);
            var third = store.AddImage(Enumerable.Repeat((byte)2, 3 * 1048576).ToArray(), 4 * 1048576);
            Check(!store.All().Any(x => x.Id == first.Id) && store.All().Any(x => x.Id == third.Id), "Hard budget evicts oldest before storing new data");
            Check(store.Storage.UsedBytes < store.Storage.BudgetBytes, "All on-disk history including SQLite stays below budget");
            Check(store.Storage.ReservedBytes == 0, "Finished writes release reservations");
        }
        using (var store = new HistoryStore(Root("pins"), Policy(8), () => 100L * 1073741824))
        {
            var pinned = store.AddImage(new byte[3 * 1048576], 4 * 1048576);
            store.Pin(pinned);
            Check(Refused(() => store.AddImage(new byte[3 * 1048576], 4 * 1048576)), "Pinned data blocks new capture rather than exceeding budget");
            Check(store.All().Single().Pinned && File.Exists(pinned.Files[0]), "Quota rejection preserves pinned backing file");
            Check(Directory.GetDirectories(Path.Combine(Root("pins"), "objects")).Length == 1, "Quota rejection creates no partial directory");
        }
        using (var store = new HistoryStore(Root("reserve"), Policy(), () => 6L * 1048576))
            Check(Refused(() => store.AddText("A")), "Free space floor applies to text as well as files");
        string changing = Path.Combine(parent, "changing-free-space.bin");
        File.WriteAllBytes(changing, new byte[8 * 1048576]);
        int observations = 0;
        using (var store = new HistoryStore(Root("midcopy"), Policy(32), () => ++observations <= 2 ? 100L * 1073741824 : 0))
        {
            Check(Refused(() => store.AddFiles([changing], 10 * 1048576).GetAwaiter().GetResult()), "Disk-space loss during copy stops the write");
            Check(store.All().Count == 0 && Directory.GetDirectories(Path.Combine(Root("midcopy"), "objects")).Length == 0, "Aborted write publishes nothing and removes its partial data");
        }
        var agePolicy = Policy(32);
        using (var store = new HistoryStore(Root("age"), agePolicy, () => 100L * 1073741824))
        {
            var text = store.AddText("Keep text for thirty days");
            var picture = store.AddImage([1], 1024);
            var pin = store.AddImage([2], 1024);
            store.Pin(pin);
            Age(Root("age"), text, 20);
            Age(Root("age"), picture, 8);
            Age(Root("age"), pin with
            {
                Pinned = true
            }, 100);
            store.Prune(agePolicy);
            Check(store.All().Any(x => x.Id == text.Id) && !store.All().Any(x => x.Id == picture.Id), "Text and binary data use separate retention periods");
            Check(store.All().Any(x => x.Id == pin.Id && x.Pinned), "Age cleanup never removes pinned entries");
        }
        var countPolicy = Policy();
        countPolicy.MaxHistoryEntries = 2;
        using (var store = new HistoryStore(Root("count"), countPolicy, () => 100L * 1073741824))
        {
            var first = store.AddText("one");
            store.AddText("two");
            store.AddText("three");
            Check(store.All().Count == 2 && !store.All().Any(x => x.Id == first.Id), "Entry-count limit is enforced before insertion");
        }
        string orphan = Path.Combine(Root("orphan"), "objects", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(orphan);
        File.WriteAllText(Path.Combine(orphan, "partial.bin"), "unfinished");
        string unrelated = Path.Combine(Root("orphan"), "objects", "not-owned");
        Directory.CreateDirectory(unrelated);
        using (var store = new HistoryStore(Root("orphan"), Policy(), () => 100L * 1073741824))
        {
            Check(!Directory.Exists(orphan), "Startup reclaims unpublished crash leftovers");
            Check(Directory.Exists(unrelated), "Recovery does not delete unknown directories");
        }
        using (var store = new HistoryStore(Root("duplicates"), Policy(32), () => 100L * 1073741824))
        {
            var first = store.AddFiles([changing], 10 * 1048576).GetAwaiter().GetResult();
            var repeat = store.AddFiles([changing], 10 * 1048576).GetAwaiter().GetResult();
            Check(first.Id == repeat.Id && File.Exists(repeat.Files[0]) && store.All().Count == 1, "Duplicate file capture returns the surviving backing copy");
        }
        var shrink = Policy(12);
        using (var store = new HistoryStore(Root("shrink"), shrink, () => 100L * 1073741824))
        {
            var item = store.AddImage(new byte[3 * 1048576], 4 * 1048576);
            store.Pin(item);
            shrink.StorageLimitMb = 1;
            store.Prune(shrink);
            Check(store.All().Single().Pinned && File.Exists(item.Files[0]), "Lowering budget never destroys existing pinned data");
            Check(Refused(() => store.AddText("new")), "A budget below pinned usage blocks new captures");
        }
        using (var store = new HistoryStore(Root("metadata"), Policy(8), () => 100L * 1073741824))
        {
            var item = store.AddImage(new byte[3 * 1048576], 4 * 1048576);
            store.Pin(item);
            store.RecordRecent(new(new string('X', 1048576), "metadata.exe", "App", "test"));
            Check(store.All().Single().Pinned && store.Recent().Count == 0, "Optional recent-app metadata cannot bypass quota or evict pins");
        }
        string tiny = Path.Combine(parent, "many-tiny-files");
        Directory.CreateDirectory(tiny);
        for (int i = 0; i < 1024; i++)
            File.WriteAllBytes(Path.Combine(tiny, i + ".txt"), []);
        Check(StorageBudget.DirectoryBytes(tiny) >= 1024 * 4096, "Empty files still count allocation overhead");
        using (var store = new HistoryStore(Root("tiny"), Policy(8), () => 100L * 1073741824))
            Check(Refused(() => store.AddFiles([tiny], 1048576).GetAwaiter().GetResult()), "Tiny-file selections must fit the physical allocation budget before copying");
        return tests;
    }
}
