using System.Text.Json;
namespace Kopra;

static class Diagnostics
{
    public static int Run(string output)
    {
        string root = Path.Combine(Path.GetTempPath(), "Kopra-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new HistoryStore(root);
            var search = new SearchService(new(), store);
            search.Refresh().GetAwaiter().GetResult();
            var apps = search.Search("", default).GetAwaiter().GetResult();
            var explorer = search.Search("Explorer", default).GetAwaiter().GetResult();
            var bluetooth = search.Search("Bluetooth", default).GetAwaiter().GetResult();
            int icons = 0;
            foreach (var app in apps.Take(8))
            {
                if (Icons.ForAsync(app).GetAwaiter().GetResult() != null)
                    icons++;
            }
            var microphone = search.Search("Mikrofon", default).GetAwaiter().GetResult();
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                RecentSuggestions = apps.Count,
                ExplorerFound = explorer.Any(x => x.Title == "Explorer"),
                BluetoothSettingFound = bluetooth.Any(x => x.Kind == "Einstellung"),
                search.SettingsCount,
                MicrophoneSettings = microphone.Count(x => x.Kind == "Einstellung"),
                search.IndexStatus,
                RealIconsLoaded = icons
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception e) { File.WriteAllText(output, e.ToString()); return 1; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
