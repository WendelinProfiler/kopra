using System.Diagnostics;
using System.Windows.Input;

namespace Kopra;

sealed partial class LauncherWindow
{
    readonly Border emptyState = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    Border? confirmation;
    int settingsTab; bool advancedSettings;
    static SolidColorBrush Ink(byte level) => new(Color.FromRgb(level, level, level));
    static System.Windows.Shapes.Path Vector(string data, double size) => new() { Data = Geometry.Parse(data), Stroke = Ink(175), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = size, Height = size, Stretch = Stretch.Uniform };
    void FitToScreen()
    {
        if (previewMode)
            return;
        var area = System.Windows.Forms.Screen.FromHandle(target).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        Width = Math.Min(740, area.Width / dpi.DpiScaleX - 32);
        Height = Math.Min(Height, area.Height / dpi.DpiScaleY - 48);
        if (IsVisible)
        {
            Left = Math.Clamp(Left, area.Left / dpi.DpiScaleX + 16, (area.Right / dpi.DpiScaleX) - Width - 16);
            Top = Math.Clamp(Top, area.Top / dpi.DpiScaleY + 16, area.Bottom / dpi.DpiScaleY - Height - 16);
        }
    }
    void ShowEmptyState(bool show, string query)
    {
        if (!body.Children.Contains(emptyState))
        {
            Grid.SetColumnSpan(emptyState, 2);
            body.Children.Add(emptyState);
        }
        emptyState.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
            return;
        preview.Children.Clear();
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 330, Margin = new Thickness(20) };
        panel.Children.Add(Vector("M19,10 A9,9 0 1 1 1,10 A9,9 0 1 1 19,10 M17,17 L25,25", 32));
        panel.Children.Add(new TextBlock { Text = query.Length > 0 ? "Keine Ergebnisse" : history ? "Deine Zwischenablage" : "Keine Vorschläge", FontSize = 18, FontWeight = FontWeights.Medium, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 16, 0, 7) });
        panel.Children.Add(new TextBlock { Text = query.Length > 0 ? "Versuche einen anderen Suchbegriff." : "Kopiere Text, Bilder, Dateien oder Ordner.\nDu findest sie anschließend hier wieder.", Foreground = Ink(163), FontSize = 12, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 18 });
        emptyState.Child = panel;
    }
    void ShowSettings()
    {
        editing = true;
        queryStop?.Cancel();
        navigation.Visibility = Visibility.Visible;
        contextTitle.Text = "Einstellungen";
        body.Visibility = Visibility.Collapsed;
        section.Visibility = Visibility.Collapsed;
        foreach (UIElement child in content.Children)
        if (Grid.GetRow(child) == 1 && child != settings)
            child.Visibility = Visibility.Collapsed;
        settings.Visibility = Visibility.Visible;
        Height = 590;
        FitToScreen();
        hint.Text = "Tab  Auswählen     esc  Ausblenden";
        footer.Text = "Änderungen werden automatisch gespeichert";
        var panel = new StackPanel { Margin = new Thickness(10, 4, 10, 8) };
        settings.Content = panel;
        panel.Children.Add(new TextBlock { Text = "Kopra", FontSize = 25, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 5) });
        panel.Children.Add(new TextBlock { Text = "Deine Suche. Deine Zwischenablage. Lokal auf deinem Rechner.", FontSize = 12, Foreground = Ink(165), Margin = new Thickness(0, 0, 0, 18) });
        var tabs = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
        var tabsBorder = new Border { Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)), CornerRadius = new CornerRadius(8), Padding = new Thickness(3), Child = tabs, Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(tabsBorder);
        var labels = new[] { "Allgemein", "Verlauf", "Geräte", "Datenschutz" };
        for (int i = 0; i < labels.Length; i++)
        {
            int tab = i;
            var button = MakeButton(labels[i], () => { settingsTab = tab; ShowSettings(); });
            button.Margin = new Thickness(0);
            button.Padding = new Thickness(10, 7, 10, 7);
            button.FontSize = 12;
            button.Background = i == settingsTab ? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) : Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetHelpText(button, i == settingsTab ? "Ausgewählt" : "");
            tabs.Children.Add(button);
        }
        StackPanel Group(string title, string? note = null)
        {
            panel.Children.Add(new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = Ink(175), Margin = new Thickness(3, 0, 0, 7) });
            var rows = new StackPanel();
            panel.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(13, 255, 255, 255)), CornerRadius = new CornerRadius(10), Padding = new Thickness(13, 0, 13, 0), Child = rows, Margin = new Thickness(0, 0, 0, note == null ? 18 : 7) });
            if (note != null)
                panel.Children.Add(new TextBlock { Text = note, Foreground = Ink(148), FontSize = 11, LineHeight = 16, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(3, 0, 3, 18) });
            return rows;
        }
        void Row(StackPanel group, UIElement element)
        {
            if (group.Children.Count > 0)
                group.Children.Add(new Border { Height = 0.5, Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)) });
            group.Children.Add(element);
        }
        void Toggle(StackPanel group, string label, bool value, Action<bool> change)
        {
            var check = new CheckBox { Content = label, IsChecked = value, FontSize = 12, Margin = new Thickness(0, 11, 0, 11) };
            check.Click += (_, _) => { change(check.IsChecked == true); Save(); };
            Row(group, check);
        }
        void Choice(StackPanel group, string label, long value, (long Value, string Label)[] choices, Action<long> change, string explanation)
        {
            var row = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            row.ColumnDefinitions.Add(new()
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto
            });
            var text = new StackPanel { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = label, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(new TextBlock { Text = explanation, FontSize = 11, Foreground = Ink(150), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            row.Children.Add(text);
            var available = choices.ToList();
            if (!available.Any(x => x.Value == value))
                available.Add((value, label.Contains("behalten") ? value + " Tage (aktuell)" : label.Contains("Einträge") ? value + " Einträge (aktuell)" : ClipEntry.FormatBytes(value * 1048576) + " (aktuell)"));
            var button = MakeButton(available.First(x => x.Value == value).Label + "  ⌄", () => ChooseSetting(label, value, available, chosen => { change(chosen); Save(); _ = clipboard.Maintain(); ShowSettings(); }), label + " ändern");
            button.FontSize = 12;
            button.MinWidth = 135;
            button.Padding = new Thickness(11, 7, 11, 7);
            button.Margin = new Thickness(0);
            Grid.SetColumn(button, 1);
            row.Children.Add(button);
            Row(group, row);
        }
        void ActionRow(StackPanel group, string label, Action action, bool destructive = false)
        {
            var button = MakeButton(label, action);
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            button.Margin = new Thickness(0);
            button.Padding = new Thickness(0, 11, 0, 11);
            button.FontSize = 12;
            button.Background = Brushes.Transparent;
            button.Foreground = destructive ? new SolidColorBrush(Color.FromRgb(255, 105, 110)) : new SolidColorBrush(Color.FromRgb(100, 177, 255));
            Row(group, button);
        }
        if (settingsTab == 0)
        {
            var general = Group("Bedienung");
            ActionRow(general, "Suche öffnen · " + (Shortcuts?.Label(false) ?? "Windows-Taste"), () => RecordShortcut(false));
            ActionRow(general, "Verlauf öffnen · " + (Shortcuts?.Label(true) ?? "Strg + Umschalt + V"), () => RecordShortcut(true));
            Toggle(general, "Nach Anmeldung starten", prefs.AutoStart, x => { prefs.AutoStart = x; Program.SetAutoStart(x); });
            Toggle(general, "Zwischenablageverlauf pausieren", prefs.Paused, x => prefs.Paused = x);
            var folders = Group("Suchordner", "Apps und Windows-Einstellungen sind immer durchsuchbar. Diese Ordner ergänzen die Dateisuche.");
            foreach (var path in prefs.SearchFolders.ToArray())
            {
                var row = new DockPanel { Margin = new Thickness(0, 8, 0, 8) };
                var remove = MakeButton("Entfernen", () => { prefs.SearchFolders.Remove(path); Save(); _ = search.Refresh(); ShowSettings(); });
                remove.FontSize = 11;
                remove.Padding = new Thickness(8, 4, 8, 4);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = path, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) });
                Row(folders, row);
            }
            ActionRow(folders, "Suchordner hinzufügen …", () => { activating = true; try { using var picker = new System.Windows.Forms.FolderBrowserDialog(); if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK) { prefs.SearchFolders.Add(picker.SelectedPath); prefs.SearchFolders = prefs.SearchFolders.Distinct(StringComparer.OrdinalIgnoreCase).ToList(); Save(); _ = search.Refresh(); ShowSettings(); } } finally { activating = false; Activate(); } });
            var shortcuts = Group("Tastatur");
            Row(shortcuts, new TextBlock { Text = "Windows     Suche öffnen\nStrg + Umschalt + V     Verlauf öffnen\nStrg + Tab     Suche und Verlauf wechseln\n↑ / ↓     Auswählen     ·     Enter     Öffnen / Einfügen\nEscape     Ausblenden", FontSize = 12, LineHeight = 24, Foreground = Ink(190), Margin = new Thickness(0, 9, 0, 9) });
        }
        else if (settingsTab == 1)
        {
            var storage = Group("Speicherbelegung");
            var storageText = new TextBlock { Text = "Speicher wird ermittelt …", FontSize = 12, LineHeight = 20, Foreground = Ink(195), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 9) };
            Row(storage, storageText);
            var meter = new ProgressBar { Height = 4, Minimum = 0, Maximum = 1, Foreground = new SolidColorBrush(Color.FromRgb(10, 132, 255)), Background = Ink(65), BorderThickness = new Thickness(0), Margin = new Thickness(0, 0, 0, 12) };
            storage.Children.Add(meter);
            async void Load()
            {
                try
                {
                    var state = await Task.Run(() => store.Storage);
                    storageText.Text = $"{ClipEntry.FormatBytes(state.UsedBytes)} von {ClipEntry.FormatBytes(state.BudgetBytes)} verwendet · {state.Entries} Einträge\nAngeheftet: {ClipEntry.FormatBytes(state.PinnedBytes)} · Laufwerk frei: {ClipEntry.FormatBytes(state.FreeDiskBytes)}";
                    meter.Value = state.BudgetBytes > 0 ? Math.Clamp((double)(state.UsedBytes + state.ReservedBytes) / state.BudgetBytes, 0, 1) : 0;
                }
                catch { storageText.Text = "Speicheranzeige momentan nicht verfügbar"; }
            }
            Load();
            var days = new (long, string)[] { (1, "1 Tag"), (7, "1 Woche"), (14, "2 Wochen"), (30, "1 Monat"), (90, "3 Monate"), (365, "1 Jahr") };
            var retention = Group("Wie lange soll dein Verlauf bleiben?", "Angeheftete Einträge bleiben erhalten – unabhängig von diesen Einstellungen.");
            Choice(retention, "Texte behalten", prefs.RetentionDays, days, x => prefs.RetentionDays = (int)x, "Ältere Texte werden automatisch entfernt.");
            Choice(retention, "Bilder und Dateien behalten", prefs.BinaryRetentionDays, days, x => prefs.BinaryRetentionDays = (int)x, "Gilt auch für kopierte Ordner.");
            var limits = Group("Wie viel Platz darf der Verlauf nutzen?", "Ist das Limit erreicht, werden zuerst ältere, nicht angeheftete Einträge entfernt. Deine Originaldateien bleiben erhalten.");
            Choice(limits, "Platz für den gesamten Verlauf", prefs.StorageLimitMb, [(512, "512 MB"), (1024, "1 GB"), (2048, "2 GB · empfohlen"), (5120, "5 GB"), (10240, "10 GB")], x => prefs.StorageLimitMb = x, "Enthält auch angeheftete Inhalte.");
            var advanced = MakeButton(advancedSettings ? "Weitere Schutzgrenzen  ⌃" : "Weitere Schutzgrenzen  ⌄", () => { advancedSettings = !advancedSettings; ShowSettings(); });
            advanced.Background = Brushes.Transparent;
            advanced.HorizontalAlignment = HorizontalAlignment.Left;
            advanced.FontSize = 12;
            advanced.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(advanced);
            if (advancedSettings)
            {
                var safety = Group("Schützt deinen Speicher", "Diese Grenzen sind bereits sinnvoll voreingestellt. Werden sie erreicht, pausiert die Sicherung neuer Inhalte.");
                Choice(safety, "Größe einer einzelnen Kopie", prefs.MaxEntryMb, [(32, "32 MB"), (128, "128 MB"), (256, "256 MB · empfohlen"), (512, "512 MB"), (1024, "1 GB")], x => prefs.MaxEntryMb = x, "Größere Kopien bleiben in der Zwischenablage, werden aber nicht im Verlauf gesichert.");
                Choice(safety, "Auf dem Laufwerk freihalten", prefs.MinFreeDiskMb, [(1024, "1 GB"), (2048, "2 GB"), (5120, "5 GB · empfohlen"), (10240, "10 GB"), (20480, "20 GB")], x => prefs.MinFreeDiskMb = x, "Stoppt neue Sicherungen, bevor dein Laufwerk voll wird.");
                Choice(safety, "Höchstens gespeicherte Einträge", prefs.MaxHistoryEntries, [(100, "100 Einträge"), (500, "500 Einträge"), (1000, "1.000 Einträge"), (2000, "2.000 · empfohlen"), (5000, "5.000 Einträge")], x => prefs.MaxHistoryEntries = (int)x, "Angeheftete Einträge werden nicht mitgezählt.");
            }
            var actions = Group("Verwalten");
            ActionRow(actions, "Jetzt bereinigen", async () => { await clipboard.Maintain(); if (editing) ShowSettings(); });
            ActionRow(actions, "Speicherordner öffnen", () => Process.Start(new ProcessStartInfo("explorer.exe", root) { UseShellExecute = true }));
        }
        else if (settingsTab == 3)
        {
            var privacy = Group("Von diesen Apps nichts speichern", "Vertraulich markierte Inhalte werden grundsätzlich nicht gespeichert. Der Verlauf bleibt lokal und ist nicht zusätzlich verschlüsselt.");
            foreach (var excluded in prefs.ExcludedApps.ToArray())
            {
                var row = new DockPanel { Margin = new Thickness(0, 9, 0, 9) };
                var remove = MakeButton("Entfernen", () => { prefs.ExcludedApps.Remove(excluded); Save(); ShowSettings(); });
                remove.FontSize = 11;
                remove.Padding = new Thickness(8, 4, 8, 4);
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
                row.Children.Add(new TextBlock { Text = excluded, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
                Row(privacy, row);
            }
            ActionRow(privacy, "App ausschließen …", () => { activating = true; try { var picker = new Microsoft.Win32.OpenFileDialog { Title = "App auswählen, aus der nichts gespeichert werden soll", Filter = "Apps (*.exe)|*.exe", CheckFileExists = true }; if (picker.ShowDialog(this) == true) { var name = System.IO.Path.GetFileNameWithoutExtension(picker.FileName); if (!prefs.ExcludedApps.Contains(name, StringComparer.OrdinalIgnoreCase)) prefs.ExcludedApps.Add(name); Save(); ShowSettings(); } } finally { activating = false; Activate(); } });
            var clear = Group("Verlauf löschen", "Gelöscht werden ausschließlich die Sicherungen der App. Deine Originaldateien bleiben erhalten.");
            ActionRow(clear, "Nicht angehefteten Verlauf löschen …", () => Confirm("Verlauf löschen?", "Alle nicht angehefteten Einträge und ihre gespeicherten Kopien werden gelöscht. Angeheftete Einträge bleiben erhalten.", async () => { await clipboard.Maintain(true); ShowSettings(); }), true);
            ActionRow(clear, "Gesamten Verlauf löschen …", () => Confirm("Gesamten Verlauf löschen?", "Auch angeheftete Einträge werden gelöscht und die aktuelle Windows-Zwischenablage wird geleert. Das kann nicht rückgängig gemacht werden.", async () => { if (await clipboard.Maintain(true, true)) { try { Clipboard.Clear(); } catch (System.Runtime.InteropServices.COMException) { } } ShowSettings(); }), true);
        }
        if (settingsTab == 2)
            PopulateDeviceSettings(panel);
        settings.ScrollToTop();
    }
    void ChooseSetting(string title, long selected, IReadOnlyList<(long Value, string Label)> choices, Action<long> change)
    {
        DismissConfirmation();
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        var options = new StackPanel();
        var buttons = new List<Button>();
        foreach (var choice in choices)
        {
            var button = MakeButton((choice.Value == selected ? "✓  " : "     ") + choice.Label, () => { DismissConfirmation(); change(choice.Value); });
            button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Margin = new Thickness(0, 2, 0, 2);
            button.FontSize = 13;
            button.Padding = new Thickness(12, 9, 12, 9);
            button.Background = choice.Value == selected ? new SolidColorBrush(Color.FromArgb(70, 10, 132, 255)) : Brushes.Transparent;
            System.Windows.Automation.AutomationProperties.SetName(button, choice.Label + (choice.Value == selected ? ", ausgewählt" : ""));
            buttons.Add(button);
            options.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = options, MaxHeight = 290, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(scroll);
        var cancel = MakeButton("Abbrechen", () => DismissConfirmation());
        cancel.Margin = new Thickness(0, 14, 0, 0);
        panel.Children.Add(cancel);
        confirmation = new Border { Background = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)), CornerRadius = new CornerRadius(15), Child = new Border { Width = Math.Min(360, Width - 60), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(15), Background = Ink(44), Child = panel } };
        Grid.SetRowSpan(confirmation, 5);
        Panel.SetZIndex(confirmation, 10);
        content.Children.Add(confirmation);
        confirmation.PreviewKeyDown += (_, e) => { if (e.Key is Key.Down or Key.Up) { var index = buttons.FindIndex(b => b.IsKeyboardFocusWithin); int next = Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, buttons.Count - 1); buttons[next].Focus(); buttons[next].BringIntoView(); e.Handled = true; } };
        buttons[Math.Max(0, choices.ToList().FindIndex(x => x.Value == selected))].Focus();
    }
    bool DismissConfirmation()
    {
        Shortcuts?.End();
        StopPairingUi();
        if (confirmation == null)
            return false;
        content.Children.Remove(confirmation);
        confirmation = null;
        return true;
    }
    void Confirm(string title, string detail, Func<Task> action)
    {
        DismissConfirmation();
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 19, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = detail, FontSize = 12, LineHeight = 19, Foreground = Ink(183), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 22) });
        var buttons = new Grid();
        buttons.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        buttons.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        var cancel = MakeButton("Abbrechen", () => DismissConfirmation());
        var delete = MakeButton("Löschen", async () => { DismissConfirmation(); await action(); });
        delete.Background = new SolidColorBrush(Color.FromRgb(190, 55, 63));
        Grid.SetColumn(delete, 1);
        buttons.Children.Add(cancel);
        buttons.Children.Add(delete);
        panel.Children.Add(buttons);
        confirmation = new Border { Background = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)), CornerRadius = new CornerRadius(15), Child = new Border { Width = 390, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(15), Background = Ink(44), Child = panel } };
        Grid.SetRowSpan(confirmation, 5);
        Panel.SetZIndex(confirmation, 10);
        content.Children.Add(confirmation);
        cancel.Focus();
    }
}
