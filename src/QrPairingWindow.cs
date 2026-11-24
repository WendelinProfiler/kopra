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
        panel.Children.Add(new TextBlock { Text = "Mac verbinden", FontSize = 21, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Öffne Kopra auf dem Mac → Einstellungen → Geräte. Gib dort diesen Code ein und bestätige anschließend auf beiden Geräten.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Ink(180), Margin = new Thickness(0, 10, 0, 14) });
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
        panel.Children.Add(save);
        panel.Children.Add(MakeButton("Schließen", () => DismissConfirmation()));
        confirmation = new Border { Background = new SolidColorBrush(Color.FromArgb(165, 0, 0, 0)), CornerRadius = new CornerRadius(15), Child = new Border { Width = 410, MaxHeight = Math.Max(350, Height - 30), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(15), Background = Ink(44), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
        Grid.SetRowSpan(confirmation, 5);
        Panel.SetZIndex(confirmation, 10);
        content.Children.Add(confirmation);
        pairingTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        pairingTimer.Tick += (_, _) => { var snapshot = DeviceSync.Pairing.Snapshot; long seconds = Math.Max(0, (snapshot.Expires - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000); switch (snapshot.Status) { case "scanning": state.Text = $"Wartet auf deinen Mac · {seconds / 60}:{seconds % 60:D2} Minuten"; break; case "confirmation": entryCode.Visibility = Visibility.Collapsed; extra.Visibility = Visibility.Collapsed; surface.Visibility = Visibility.Collapsed; save.Visibility = Visibility.Collapsed; digits.Text = snapshot.Code; digits.Visibility = Visibility.Visible; approve.Visibility = Visibility.Visible; approve.IsEnabled = !snapshot.HostApproved; state.Text = snapshot.HostApproved ? "Am Mac ebenfalls bestätigen." : "Zeigen beide Geräte denselben Zahlencode?"; break; case "complete": entryCode.Visibility = Visibility.Collapsed; extra.Visibility = Visibility.Collapsed; surface.Visibility = Visibility.Collapsed; digits.Visibility = Visibility.Collapsed; save.Visibility = Visibility.Collapsed; approve.Visibility = Visibility.Collapsed; state.Text = "Mac verbunden. Neue Kopien werden jetzt geteilt."; break; case "expired": case "rejected": entryCode.Visibility = Visibility.Collapsed; extra.Visibility = Visibility.Collapsed; surface.Visibility = Visibility.Collapsed; digits.Visibility = Visibility.Collapsed; approve.Visibility = Visibility.Collapsed; save.Visibility = Visibility.Collapsed; state.Text = snapshot.Status == "expired" ? "Der Code ist abgelaufen. Schließe das Fenster und starte eine neue Kopplung." : "Die Kopplung wurde abgebrochen."; DeviceSync.CancelPairing(); break; } };
        pairingTimer.Start();
    }
    void StopPairingUi()
    {
        pairingTimer?.Stop();
        pairingTimer = null;
        DeviceSync?.CancelPairing();
    }
}
