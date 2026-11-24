namespace Kopra;

sealed partial class LauncherWindow
{
    void RecordShortcut(bool historyShortcut)
    {
        if (Shortcuts == null)
            return;
        DismissConfirmation();
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = historyShortcut ? L.Text("Verlauf öffnen") : L.Text("Suche öffnen"), FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = L.Text("Drücke die gewünschte Tastenkombination. Escape bricht ab."), TextWrapping = TextWrapping.Wrap, Foreground = Ink(175), FontSize = 12, Margin = new Thickness(0, 12, 0, 18) });
        var feedback = new TextBlock { Text = Shortcuts.Label(historyShortcut), FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
        panel.Children.Add(feedback);
        void Apply(uint key, uint mods, bool windowsKey)
        {
            if (Shortcuts.Change(historyShortcut, key, mods, windowsKey, out string error))
            {
                Save();
                DismissConfirmation();
                ShowSettings();
            }
            else
            {
                feedback.Text = error;
                feedback.FontSize = 12;
            }
        }
        if (!historyShortcut)
            panel.Children.Add(MakeButton(L.Text("Windows-Taste verwenden"), () => Apply(0, 0, true)));
        panel.Children.Add(MakeButton(L.Text("Abbrechen"), () => DismissConfirmation()));
        confirmation = new Border { Background = new SolidColorBrush(Color.FromArgb(155, 0, 0, 0)), CornerRadius = new CornerRadius(15), Child = new Border { Width = 390, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(15), Background = Ink(44), Child = panel } };
        Grid.SetRowSpan(confirmation, 5);
        Panel.SetZIndex(confirmation, 10);
        content.Children.Add(confirmation);
        Shortcuts.Begin(Apply);
    }
}
