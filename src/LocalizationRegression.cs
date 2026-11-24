using System.Text.Json;
using System.Text.RegularExpressions;
namespace Kopra;

static class LocalizationRegression
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool value) { count++; if (!value) throw new Exception("Language regression"); }
        var previous = L.Language;
        try
        {
            Check(new Preferences().Language == "en");
            L.Use("en");
            Check(L.Text("Verlauf") == "History");
            Check(L.Text("Keine Ergebnisse") == "No results");
            Check(L.Format("Gültig für {0}:{1:D2} · Beide Geräte im selben Netzwerk", 2, 7).Contains("2:07"));
            Check(Shortcuts.Label(77, 6) == "Ctrl + Shift + M");
            var storeRoot = Path.Combine(root, "language"); Directory.CreateDirectory(storeRoot);
            using (var store = new HistoryStore(storeRoot))
            {
                var entry = store.AddText("Keine Ergebnisse");
                Check(entry.Text == "Keine Ergebnisse" && entry.Title == "Keine Ergebnisse");
                Check(entry.Subtitle.StartsWith("Text"));
            }
            var prefs = new Preferences { Language = "de" }; prefs.Save(storeRoot);
            Check(Preferences.Load(storeRoot).Language == "de");
            L.Use("de"); Check(L.Text("Verlauf") == "Verlauf");
            Check(L.Text("Connected to Windows") == "Mit Windows verbunden");
            Check(Shortcuts.Label(77, 6) == "Strg + Umschalt + M");
            var dictionary = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets/localization.en.json")))!;
            Check(dictionary.Count >= 300 && dictionary.All(x => !string.IsNullOrWhiteSpace(x.Value)));
            var placeholders = new Regex(@"\{([0-9]+)(?:[^}]*)\}");
            foreach (var pair in dictionary)
            {
                var source = placeholders.Matches(pair.Key).Select(m => m.Groups[1].Value).Order().ToArray();
                var target = placeholders.Matches(pair.Value).Select(m => m.Groups[1].Value).Order().ToArray();
                if (!source.SequenceEqual(target)) throw new Exception("Translated placeholders differ: " + pair.Key);
            }
            Check(true);
            L.Use("unsupported"); Check(L.Language == "en");
        }
        finally { L.Use(previous); }
        return count;
    }
}
