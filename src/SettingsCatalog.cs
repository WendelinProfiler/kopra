using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
namespace Kopra;

static class SettingsCatalog
{
    [DllImport("shlwapi", CharSet = CharSet.Unicode)] static extern int SHLoadIndirectString(string source, StringBuilder result, uint capacity, nint reserved);
    static string Resolve(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return "";
        if (!source.StartsWith('@'))
            return source;
        var buffer = new StringBuilder(4096);
        return SHLoadIndirectString(source, buffer, 4096, 0) == 0 ? buffer.ToString() : "";
    }
    public static List<SearchItem> Load()
    {
        var found = new List<SearchItem>();
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ImmersiveControlPanel", "Settings");
        try
        {
            var documents = Directory.EnumerateFiles(folder, "AllSystemSettings_*.xml").Select(XDocument.Load).ToList();
            var doc = documents.OrderByDescending(x => x.Descendants("PolicyIds").Count()).First();
            var entries = doc.Descendants("SearchableContent").ToList();
            var pages = new Dictionary<string, string> { ["SettingsPageNetworkWorkplace"] = "ms-settings:workplace", ["SettingsPageFlights"] = "ms-settings:windowsinsider", ["SettingsPagePCSystemBluetooth"] = "ms-settings:bluetooth", ["SettingsPagePCSystemDevices"] = "ms-settings:connecteddevices", ["SettingsPageAdvancedSystemSettings"] = "ms-settings:developers", ["SettingsPageRestoreOneBackup"] = "ms-settings:recovery" };
            string? Policy(XElement entry) => entry.Descendants("PolicyIds").SelectMany(x => x.Value.Split(';')).FirstOrDefault(x => x.Length > 0 && x.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
            foreach (var entry in entries)
            {
                string? policy = Policy(entry);
                if (policy != null)
                foreach (var page in entry.Descendants("PageID"))
                    pages.TryAdd(page.Value, "ms-settings:" + policy);
            }
            foreach (var entry in entries)
            {
                string name = Resolve(entry.Element("SettingInformation")?.Element("Description")?.Value);
                if (name.Length == 0)
                    continue;
                string keywords = Resolve(entry.Element("SettingInformation")?.Element("HighKeywords")?.Value);
                string? policy = Policy(entry), uri = policy == null ? entry.Descendants("PageID").Select(x => pages.GetValueOrDefault(x.Value)).FirstOrDefault(x => x != null) : "ms-settings:" + policy;
                if (uri == null)
                    continue;
                found.Add(new(name, uri, "Einstellung", L.Text("Windows-Einstellungen"), Keywords: keywords));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException) { }
        object? shell = null, control = null, items = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            dynamic panel = control = ((dynamic)shell!).NameSpace("shell:ControlPanelFolder");
            dynamic collection = items = panel.Items();
            for (int i = 0; i < collection.Count; i++)
            {
                object? value = null;
                try
                {
                    dynamic item = value = collection.Item(i);
                    string title = item.Name, path = item.Path;
                    if (!string.IsNullOrWhiteSpace(path))
                        found.Add(new(title, "shell:" + path, "Einstellung", "Systemsteuerung", Keywords: "Systemsteuerung " + title));
                }
                finally { if (value != null) Marshal.FinalReleaseComObject(value); }
            }
        }
        catch (COMException) { }
        finally { if (items != null) Marshal.FinalReleaseComObject(items); if (control != null) Marshal.FinalReleaseComObject(control); if (shell != null) Marshal.FinalReleaseComObject(shell); }
        return found.DistinctBy(x => x.Title + "|" + x.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
