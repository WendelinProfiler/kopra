namespace Kopra;

sealed partial class LauncherWindow
{
    void PopulateDeviceSettings(StackPanel panel)
    {
        panel.Children.Add(new TextBlock { Text = "Gemeinsame Zwischenablage", FontSize = 17, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = "Teile neue Kopien mit deinem Mac. Die Übertragung bleibt im lokalen Netzwerk. Bis zu 32 MB pro Kopie.", TextWrapping = TextWrapping.Wrap, FontSize = 12, LineHeight = 19, Foreground = Ink(175), Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = DeviceSync?.Status ?? "In dieser Vorschau nicht aktiv", FontSize = 12, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(MakeButton("Mac mit QR-Code verbinden …", ShowPairing));
        var toggle = new CheckBox { Content = "Neue Kopien direkt zum Einfügen bereitstellen", IsChecked = prefs.SharedPaste, FontSize = 12, Margin = new Thickness(0, 20, 0, 10) };
        toggle.Click += (_, _) => { prefs.SharedPaste = toggle.IsChecked == true; Save(); };
        panel.Children.Add(toggle);
        panel.Children.Add(new TextBlock { Text = "Offline nachgeholte Einträge erscheinen nur im Verlauf. Neuere lokale Kopien werden nicht überschrieben. Ausgeschlossene Apps werden nicht geteilt.", TextWrapping = TextWrapping.Wrap, FontSize = 11, LineHeight = 17, Foreground = Ink(150), Margin = new Thickness(0, 0, 0, 20) });
        var remove = MakeButton("Geräteverbindung aufheben …", () => Confirm("Verbindung aufheben?", "Die Geräte teilen keine neuen Kopien mehr. Bereits gespeicherte Einträge bleiben erhalten. Erneutes Verbinden erfordert einen neuen Kopplungscode.", async () => { DeviceSync?.Unpair(); ShowSettings(); await Task.CompletedTask; }));
        remove.Foreground = new SolidColorBrush(Color.FromRgb(255, 105, 110));
        panel.Children.Add(remove);
    }
}
