using System.Globalization;
using System.Text.Json;
namespace Kopra;

static class L
{
    static readonly Dictionary<string, string> english = Load("en");
    static readonly Dictionary<string, string> german = Load("de");
    static string language = "en";
    public static string Language => language;
    public static event Action? Changed;
    static Dictionary<string, string> Load(string locale) => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "assets", "localization." + locale + ".json")))!;
    public static void Use(string locale)
    {
        language = locale == "de" ? "de" : "en";
        var culture = CultureInfo.GetCultureInfo(language == "de" ? "de-DE" : "en-US");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Changed?.Invoke();
    }
    public static string Text(string source)
    {
        if (language == "en") return english.GetValueOrDefault(source, source);
        if (german.TryGetValue(source, out var value)) return value;
        var original = english.FirstOrDefault(x => x.Value == source).Key;
        return original == null ? source : german.GetValueOrDefault(original, original);
    }
    public static string Format(string source, params object?[] values) => string.Format(CultureInfo.CurrentCulture, Text(source), values);
}
