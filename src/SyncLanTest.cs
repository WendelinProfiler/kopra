using System.Text.Json;
namespace Kopra;

static class SyncLanTest
{
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);
        var prefs = new Preferences { MinFreeDiskMb = 0, SharedPaste = false, StorageLimitMb = 128 };
        using var store = new HistoryStore(root, prefs);
        using var clipboard = new ClipboardService(store, prefs);
        DeviceSync.ExportTestPairing(root, Path.Combine(root, "pairing.txt"));
        using var sync = new DeviceSync(root, store, clipboard, prefs);
        store.AddText("IslandSync Test Windows → Mac");
        store.AddImage(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4GQAAAAASUVORK5CYII="), 1048576);
        string source = Path.Combine(root, "source", "Testordner");
        Directory.CreateDirectory(Path.Combine(source, "Leer"));
        File.WriteAllText(Path.Combine(source, "Notiz.txt"), "Windows test file");
        store.AddFiles([source], 1048576).GetAwaiter().GetResult();
        var limit = DateTime.UtcNow.AddSeconds(110);
        while (DateTime.UtcNow < limit)
        {
            var incoming = store.All().Where(x => x.OriginName == "Mac").ToList();
            SyncConfiguration state = JsonSerializer.Deserialize<SyncConfiguration>(File.ReadAllText(Path.Combine(root, "devices.json")))!;
            if (incoming.Count == 3 && state.Sent.Count >= 3)
            {
                bool actual = incoming.Any(x => x.Kind == "text" && x.Text == "IslandSync Test Mac → Windows") && incoming.Any(x => x.Kind == "image") && incoming.Any(x => x.Kind == "files" && File.ReadAllText(Path.Combine(x.Files[0], "Notiz.txt")) == "Mac test file" && Directory.Exists(Path.Combine(x.Files[0], "Leer")));
                Thread.Sleep(1500);
                File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
                {
                    Ok = actual,
                    Incoming = incoming.Count,
                    OutgoingAcknowledged = state.Sent.Count,
                    ActualFilesAndEmptyFolders = actual,
                    UsesSshForTransport = false
                }));
                return actual ? 0 : 1;
            }
            Thread.Sleep(300);
        }
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            Ok = false,
            Error = "LAN test timeout",
            sync.Status
        }));
        return 1;
    }
}
