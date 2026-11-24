using System.Text.Json;
namespace Kopra;

public sealed class Preferences
{
    public string Language { get; set; } = "en";
    public int RetentionDays { get; set; } = 30;
    public long StorageLimitMb { get; set; } = 2048;
    public long MaxEntryMb { get; set; } = 256;
    public int BinaryRetentionDays { get; set; } = 7;
    public int MaxHistoryEntries { get; set; } = 2000;
    public long MinFreeDiskMb { get; set; } = 5120;
    public int StoragePolicyVersion
    {
        get; set;
    }
    public bool Paused
    {
        get; set;
    }
    public bool WindowsKey { get; set; } = true;
    public uint SearchKey { get; set; } = 32;
    public uint SearchModifiers { get; set; } = 2;
    public uint HistoryKey { get; set; } = 86;
    public uint HistoryModifiers { get; set; } = 6;
    public bool AutoStart { get; set; } = false;
    public bool SharedPaste { get; set; } = true;
    public List<string> SearchFolders { get; set; } = [];
    public List<string> ExcludedApps { get; set; } = ["1Password", "Bitwarden", "KeePass", "KeePassXC"];
    public static Preferences Load(string root)
    {
        try
        {
            var prefs = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(Path.Combine(root, "preferences.json"))) ?? new();
            if (prefs.StoragePolicyVersion < 1)
            {
                if (prefs.MaxEntryMb == 512)
                    prefs.MaxEntryMb = 256;
                prefs.ExcludedApps = prefs.ExcludedApps.Concat(new[] { "1Password", "Bitwarden", "KeePass", "KeePassXC" }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                prefs.StoragePolicyVersion = 1;
            }
            return prefs;
        }
        catch { return new(); }
    }
    public void Save(string root)
    {
        var path = Path.Combine(root, "preferences.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
public sealed record ClipEntry(string Id, string Kind, string Title, string Text, string[] Files, string Fingerprint, long Bytes, DateTime Created, bool Pinned = false)
{
    public string OriginDevice { get; init; } = "";
    public string OriginName { get; init; } = "";
    public string Subtitle
    {
        get
        {
            string kind = Kind == "text" ? L.Text("Text") : Kind == "image" ? L.Text("Bild") : Files.Length == 1 ? L.Text(Directory.Exists(Files[0]) ? "Ordner" : new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".heic" }.Contains(Path.GetExtension(Files[0]), StringComparer.OrdinalIgnoreCase) ? "Bilddatei" : "Datei") : L.Format("{0} Objekte", Files.Length);
            return (Pinned ? L.Text("Angeheftet") + " · " : "") + kind + " · " + FormatBytes(Bytes) + " · " + Created.ToLocalTime().ToString("g", L.Culture);
        }
    }
    public string Glyph => Kind == "text" ? "≡" : Kind == "image" ? "▧" : "▱";
    public static string FormatBytes(long value) => value >= 1073741824 ? (value / 1073741824d).ToString("0.#", L.Culture) + " GB" : value >= 1048576 ? (value / 1048576d).ToString("0.#", L.Culture) + " MB" : value >= 1024 ? (value / 1024d).ToString("0.#", L.Culture) + " KB" : value.ToString(L.Culture) + " B";
}
public sealed record SearchItem(string Title, string Path, string Kind, string Detail, int Uses = 0, string Keywords = "")
{
    public string Glyph => Kind == "App" ? "▦" : Kind == "Einstellung" ? "⚙" : Kind == "Ordner" ? "▱" : "▤";
}
public sealed record ViewItem(string Title, string Subtitle, string Glyph, object Value, ImageSource? Thumbnail = null, ImageSource? Preview = null);
public enum LauncherAction
{
    History
}
public sealed record RecentItem(SearchItem Item, DateTime At);
