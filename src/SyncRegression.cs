using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Kopra;

static class SyncRegression
{
    public static int VerifyPeer(string value)
    {
        var plain = SyncWire.Open(Convert.FromBase64String(value), Enumerable.Range(0, 32).Select(x => (byte)x).ToArray());
        return Encoding.UTF8.GetString(plain) == "Kopra · verschlüsselt" ? 0 : 1;
    }
    public static int Run(string root)
    {
        int count = 0;
        void Check(bool condition, string name)
        {
            count++;
            if (!condition)
                throw new Exception("Sync: " + name);
        }
        var key = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
        var text = Encoding.UTF8.GetBytes("Kopra · verschlüsselt");
        var sealedBox = SyncWire.Seal(text, key);
        Check(SyncWire.Open(sealedBox, key).SequenceEqual(text), "AEAD round trip");
        sealedBox[14] ^= 1;
        bool rejected = false;
        try
        {
            SyncWire.Open(sealedBox, key);
        }
        catch (CryptographicException) { rejected = true; }
        Check(rejected, "tampering rejected");
        var sample = new SharedClip("synthetic-test", "files", "Testordner", "", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), [new("Dokumente", true, ""), new("Dokumente/Notiz.txt", false, Convert.ToBase64String(text)), new("Dokumente/Leer", true, "")]);
        SyncWire.Check(sample);
        rejected = false;
        try { SyncWire.Check(sample with { Files = [new("Bad.txt", false, "!invalid!")] }); }
        catch (IOException) { rejected = true; }
        Check(rejected, "invalid base64 rejected before storage");
        foreach (var bad in new[] { "../outside", "/absolute", "C:/drive", "a\\b", "CON.txt", "a/../b", "a/", "a.", "COM1" })
        {
            rejected = false;
            try
            {
                SyncWire.Check(sample with
                {
                    Files = [new(bad, false, "")]
                });
            }
            catch (IOException) { rejected = true; }
            Check(rejected, "unsafe path " + bad);
        }
        using (var store = new HistoryStore(Path.Combine(root, "shared"), new Preferences { MinFreeDiskMb = 0 }))
        {
            var entry = store.ImportShared(sample, "remote-test", "Mac", 1048576);
            Check(File.ReadAllBytes(Path.Combine(entry.Files[0], "Notiz.txt")).SequenceEqual(text), "actual received file contents");
            Check(Directory.Exists(Path.Combine(entry.Files[0], "Leer")), "empty folders preserved");
            Check(entry.OriginDevice == "remote-test", "remote source retained");
            var packed = SyncWire.Pack(entry);
            Check(packed.Files.Count == 3, "folder export");
            var message = store.ImportShared(new("text-test", "text", "Nachricht", "Mac → Windows", 0, []), "remote", "Mac", 1048576);
            Check(message.Text == "Mac → Windows", "text import");
            store.SetOrigin(message, "", "");
            Check(store.All().Single(x => x.Id == message.Id).OriginDevice == "", "explicit local recopy becomes local");
        }
        var fixture = new
        {
            key = Convert.ToBase64String(key),
            plain = Convert.ToBase64String(text),
            sealedBox = Convert.ToBase64String(SyncWire.Seal(text, key))
        };
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sync-fixture.json"), JsonSerializer.Serialize(fixture));
        return count;
    }
}
