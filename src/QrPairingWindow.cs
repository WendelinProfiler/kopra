using System.Windows.Media.Imaging;
using QRCoder;
namespace Kopra;

sealed partial class LauncherWindow
{
    System.Windows.Threading.DispatcherTimer? pairingTimer;
    void ShowPairing()
    {
        if (DeviceSync == null)
            return;
        DismissConfirmation();
        string code;
        try
        {
            code = DeviceSync.BeginPairing();
        }
        catch (IOException error) { footer.Text = error.Message; return; }
        var panel = new StackPanel { Margin = new Thickness(22) };
        var heading = new TextBlock { Text = "Mac verbinden", FontSize = 21, FontWeight = FontWeights.SemiBold };
        panel.Children.Add(heading);
        var instructions = new TextBlock { Text = "Gib diesen Code auf deinem Mac in Kopra → Einstellungen → Geräte ein.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Ink(180), Margin = new Thickness(0, 10, 0, 14) };
        panel.Children.Add(instructions);
        var entryCode = new TextBlock { Text = DeviceSync.Pairing.EntryCode, FontSize = 38, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 12) };
        panel.Children.Add(entryCode);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(data);
        using var memory = new MemoryStream(png.GetGraphic(8));
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        var qr = new Image { Source = image, Width = 236, Height = 236, HorizontalAlignment = HorizontalAlignment.Center };
        RenderOptions.SetBitmapScalingMode(qr, BitmapScalingMode.NearestNeighbor);
        var surface = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Child = qr, HorizontalAlignment = HorizontalAlignment.Center };
        var extra = new Expander { Header = "Stattdessen QR-Code verwenden", Content = surface, IsExpanded = false, Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(extra);
        var state = new TextBlock { Text = "Wartet auf deinen Mac", FontSize = 12, Foreground = Ink(175), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 12, 0, 8), TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(state);
        var digits = new TextBlock { FontSize = 30, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 12) };
        panel.Children.Add(digits);
        var approve = MakeButton("Codes stimmen überein", () => DeviceSync.Pairing.Approve());
        approve.Background = new SolidColorBrush(Color.FromRgb(10, 132, 255));
        approve.Visibility = Visibility.Collapsed;
        panel.Children.Add(approve);
        var save = MakeButton("QR-Code als Bild speichern …", () => { var picker = new Microsoft.Win32.SaveFileDialog { Filter = "PNG-Bild|*.png", FileName = "Kopra-Kopplung.png" }; if (picker.ShowDialog(this) == true) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(picker.FileName); encoder.Save(output); } });
        save.FontSize = 11;
        save.Background = Brushes.Transparent;
        save.Visibility = Visibility.Collapsed;
        extra.Expanded += (_, _) => save.Visibility = Visibility.Visible;
        extra.Collapsed += (_, _) => save.Visibility = Visibility.Collapsed;
        panel.Children.Add(save);
        var retry = MakeButton("Neuen Code anzeigen", ShowPairing);
        retry.Visibility = Visibility.Collapsed; panel.Children.Add(retry);
        var close = MakeButton("Abbrechen", () => { DismissConfirmation(); ShowSettings(); });
        panel.Children.Add(close);
        confirmation = new Border { Background = new SolidColorBrush(Color.FromArgb(165, 0, 0, 0)), CornerRadius = new CornerRadius(15), Child = new Border { Width = 410, MaxHeight = Math.Max(350, Height - 30), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(15), Background = Ink(44), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
        Grid.SetRowSpan(confirmation, 5);
        Panel.SetZIndex(confirmation, 10);
        content.Children.Add(confirmation);
        pairingTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        pairingTimer.Tick += (_, _) =>
        {
            var snapshot = DeviceSync.Pairing.Snapshot;
            long seconds = Math.Max(0, (snapshot.Expires - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000);
            switch (snapshot.Status)
            {
                case "scanning":
                    state.Text = $"Gültig für {seconds / 60}:{seconds % 60:D2} · Beide Geräte im selben Netzwerk";
                    break;
                case "confirmation":
                    entryCode.Visibility = extra.Visibility = save.Visibility = Visibility.Collapsed;
                    heading.Text = "Mac gefunden";
                    instructions.Text = snapshot.Numeric ? "Erlaube die Verbindung, damit beide Geräte neue Kopien teilen können." : "Vergleiche diesen Bestätigungscode mit dem Mac.";
                    digits.Text = snapshot.Code;
                    digits.Visibility = snapshot.Numeric ? Visibility.Collapsed : Visibility.Visible;
                    approve.Visibility = Visibility.Visible;
                    approve.Content = snapshot.HostApproved ? "Auf diesem PC bestätigt" : snapshot.Numeric ? "Mit Mac verbinden" : "Codes stimmen überein";
                    approve.IsEnabled = !snapshot.HostApproved;
                    state.Text = snapshot.HostApproved ? "Bitte jetzt am Mac bestätigen." : snapshot.PeerApproved ? "Am Mac bestätigt. Bestätige jetzt auf diesem PC." : "Bestätige die Verbindung auf diesem PC und am Mac.";
                    break;
                case "complete":
                    entryCode.Visibility = extra.Visibility = digits.Visibility = save.Visibility = approve.Visibility = Visibility.Collapsed;
                    heading.Text = "Mit Mac verbunden";
                    instructions.Text = "Neue Texte, Bilder, Dateien und Ordner erscheinen jetzt auf beiden Geräten.";
                    state.Text = "Die Verbindung bleibt gespeichert. Beim nächsten Start musst du nicht erneut koppeln.";
                    close.Content = "Fertig";
                    break;
                case "expired":
                case "rejected":
                    entryCode.Visibility = extra.Visibility = digits.Visibility = approve.Visibility = save.Visibility = Visibility.Collapsed;
                    heading.Text = snapshot.Status == "expired" ? "Code abgelaufen" : "Verbindung abgebrochen";
                    instructions.Text = "Dein Verlauf bleibt erhalten. Du kannst einen neuen Versuch starten.";
                    state.Text = "Erzeuge hier einen neuen Code und gib ihn am Mac ein.";
                    retry.Visibility = Visibility.Visible;
                    close.Content = "Schließen";
                    DeviceSync.CancelPairing();
                    pairingTimer?.Stop();
                    break;
            }
        };
        pairingTimer.Start();
    }
    void StopPairingUi()
    {
        pairingTimer?.Stop();
        pairingTimer = null;
        DeviceSync?.CancelPairing();
    }
}
