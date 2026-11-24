using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
namespace Kopra;

sealed partial class LauncherWindow : Window
{
    public DeviceSync? DeviceSync
    {
        get; set;
    }
    public Shortcuts? Shortcuts
    {
        get; set;
    }
    public void Toggle(bool mode)
    {
        if (IsVisible && history == mode && !editing)
        {
            Hide();
            return;
        }
        OpenMode(mode);
    }
    readonly HistoryStore store; readonly Preferences prefs; readonly SearchService search; readonly ClipboardService clipboard; readonly string root;
    readonly TextBox input = new(); readonly ListBox list = new(); readonly TextBlock status = new(), section = new(), footer = new(); readonly Grid body = new(), content = new(), preview = new(); readonly ScrollViewer settings = new();
    readonly FrameworkElement navigation; readonly TextBlock contextTitle = new(), placeholder = new(), hint = new(); CancellationTokenSource? queryStop; nint target; bool history, editing, previewMode, activating; int generation; string renderedQuery = "";
    public LauncherWindow(HistoryStore store, Preferences prefs, SearchService search, ClipboardService clipboard, string root)
    {
        this.store = store;
        this.prefs = prefs;
        this.search = search;
        this.clipboard = clipboard;
        this.root = root;
        Title = "Kopra";
        Width = 740;
        Height = 480;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        Foreground = Brushes.White;
        FontFamily = ChooseFont();
        FontSize = 14;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Resources.Add(typeof(Button), XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Setter Property="Foreground" Value="#EDEDF1"/><Setter Property="Background" Value="#18FFFFFF"/><Setter Property="Padding" Value="14,8"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Cursor" Value="Hand"/><Setter Property="FocusVisualStyle" Value="{x:Null}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" CornerRadius="9" BorderThickness="1" BorderBrush="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#70B5FF"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.6"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.4"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="Opacity" Value="0.78"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """));
        Resources.Add(typeof(CheckBox), XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="CheckBox"><Setter Property="Foreground" Value="#F2F2F4"/><Setter Property="Margin" Value="0,9"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><DockPanel><Border x:Name="track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" DockPanel.Dock="Right" Width="38" Height="22" CornerRadius="11" Background="#55555B"><Ellipse x:Name="knob" Width="18" Height="18" Fill="White" HorizontalAlignment="Left" Margin="2"/></Border><ContentPresenter VerticalAlignment="Center" Margin="0,0,20,0"/></DockPanel><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="track" Property="Background" Value="#0A84FF"/><Setter TargetName="knob" Property="HorizontalAlignment" Value="Right"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """));
        Resources.Add(typeof(TextBox), XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="TextBox"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}"><ScrollViewer x:Name="PART_ContentHost" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/></Border></ControlTemplate></Setter.Value></Setter></Style>
        """));
        Resources.Add(typeof(System.Windows.Controls.Primitives.Thumb), XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Thumb"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Thumb"><Border Background="#60FFFFFF" CornerRadius="3" Margin="1,2"/></ControlTemplate></Setter.Value></Setter></Style>
        """));
        Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ScrollBar"><Setter Property="Width" Value="5"/><Setter Property="Opacity" Value="0"/><Setter Property="Background" Value="Transparent"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton><Track.Thumb><Thumb MinHeight="28"/></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton></Track></ControlTemplate></Setter.Value></Setter><Style.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Opacity" Value="0.85"/></Trigger></Style.Triggers></Style>
        """));
        var surface = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(0.5), BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), Background = new LinearGradientBrush(Color.FromArgb(235, 44, 44, 48), Color.FromArgb(245, 29, 29, 33), 90), Padding = new Thickness(14, 14, 14, 10) };
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        Content = surface;
        surface.Child = content;
        content.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        content.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        content.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        content.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        content.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 0, 7), Visibility = Visibility.Collapsed };
        navigation = top;
        Add(content, top, 0);
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        DockPanel.SetDock(tools, Dock.Right);
        top.Children.Add(tools);
        var back = MakeButton("", () => OpenMode(false, false), L.Text("Zurück zur Suche"));
        back.Content = Vector("M16,3 L7,12 16,21", 13);
        back.Background = Brushes.Transparent;
        back.Padding = new Thickness(5, 0, 8, 2);
        back.FontSize = 24;
        tools.Children.Add(back);
        contextTitle.FontSize = 12;
        contextTitle.MinWidth = 140;
        contextTitle.Foreground = new SolidColorBrush(Color.FromRgb(169, 169, 179));
        contextTitle.VerticalAlignment = VerticalAlignment.Center;
        top.Children.Add(contextTitle);
        var searchBox = new Grid { Margin = new Thickness(8, 0, 2, 13) };
        searchBox.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(34)
        });
        searchBox.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        searchBox.ColumnDefinitions.Add(new()
        {
            Width = GridLength.Auto
        });
        Add(content, searchBox, 1);
        var magnifier = new System.Windows.Shapes.Path { Data = Geometry.Parse("M19,10 A9,9 0 1 1 1,10 A9,9 0 1 1 19,10 M17,17 L25,25"), Stroke = new SolidColorBrush(Color.FromRgb(174, 174, 184)), StrokeThickness = 1.7, Width = 25, Height = 25, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        searchBox.Children.Add(magnifier);
        var searchTools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(searchTools, 2);
        searchBox.Children.Add(searchTools);
        var gear = MakeButton("", () => ShowSettings(), L.Text("Einstellungen"));
        gear.Content = new Image { Source = Icons.SettingsSymbol(), Width = 20, Height = 20, Stretch = Stretch.Uniform };
        System.Windows.Automation.AutomationProperties.SetName(gear, L.Text("Einstellungen"));
        gear.Background = Brushes.Transparent;
        gear.Foreground = new SolidColorBrush(Color.FromRgb(151, 151, 161));
        gear.Padding = new Thickness(8, 6, 8, 6);
        searchTools.Children.Add(gear);
        input.FontSize = 24;
        input.Background = Brushes.Transparent;
        input.Foreground = Brushes.White;
        input.CaretBrush = Brushes.White;
        input.BorderThickness = new Thickness(0);
        input.Padding = new Thickness(4, 7, 4, 7);
        input.VerticalContentAlignment = VerticalAlignment.Center;
        input.SetValue(System.Windows.Input.InputMethod.IsInputMethodEnabledProperty, true);
        Grid.SetColumn(input, 1);
        searchBox.Children.Add(input);
        input.TextChanged += async (_, _) => await UpdateResults();
        placeholder.Text = L.Text("Suchen …");
        placeholder.FontSize = 24;
        placeholder.Foreground = new SolidColorBrush(Color.FromRgb(154, 154, 165));
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.Margin = new Thickness(5, 0, 0, 0);
        placeholder.IsHitTestVisible = false;
        Grid.SetColumn(placeholder, 1);
        searchBox.Children.Add(placeholder);
        input.TextChanged += (_, _) => placeholder.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        section.FontSize = 10;
        section.FontWeight = FontWeights.Medium;
        section.Foreground = new SolidColorBrush(Color.FromRgb(157, 158, 168));
        section.Margin = new Thickness(7, 0, 0, 8);
        Add(content, section, 2);
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        body.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(0)
        });
        Add(content, body, 3);
        list.Background = Brushes.Transparent;
        list.BorderThickness = new Thickness(0);
        list.Foreground = Brushes.White;
        list.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        list.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        list.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        list.SelectionChanged += (_, _) => UpdatePreview();
        list.MouseDoubleClick += async (_, _) => await ExecuteSelected();
        body.Children.Add(list);
        list.ItemContainerStyle = (Style)XamlReader.Parse("""
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem"><Setter Property="HorizontalContentAlignment" Value="Stretch"/><Setter Property="Margin" Value="0,2"/><Setter Property="Padding" Value="11,8"/><Setter Property="FocusVisualStyle" Value="{x:Null}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/><Setter Property="Foreground" Value="#F3F3F6"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ListBoxItem"><Border x:Name="b" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" CornerRadius="9" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsSelected" Value="True"><Setter TargetName="b" Property="Background" Value="#087FF5"/></Trigger><MultiTrigger><MultiTrigger.Conditions><Condition Property="IsMouseOver" Value="True"/><Condition Property="IsSelected" Value="False"/></MultiTrigger.Conditions><Setter TargetName="b" Property="Background" Value="#12FFFFFF"/></MultiTrigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
        """);
        list.ItemTemplate = (DataTemplate)XamlReader.Parse("""
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="44"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><Border Width="34" Height="34" CornerRadius="8" Background="Transparent" VerticalAlignment="Center"><Grid><TextBlock Text="{Binding Glyph}" FontSize="20" HorizontalAlignment="Center" VerticalAlignment="Center"/><Image Source="{Binding Thumbnail}" Stretch="Uniform" Margin="3"/></Grid></Border><StackPanel Grid.Column="1" VerticalAlignment="Center" Margin="6,0,0,0"><TextBlock Text="{Binding Title}" FontSize="15" FontWeight="Medium" TextTrimming="CharacterEllipsis"/><TextBlock Text="{Binding Subtitle}" FontSize="11" Foreground="#B8FFFFFF" Margin="0,4,0,0" TextTrimming="CharacterEllipsis"/></StackPanel></Grid></DataTemplate>
        """);
        var detail = new Border { BorderBrush = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), BorderThickness = new Thickness(0.5, 0, 0, 0), Margin = new Thickness(13, 4, 0, 3), Padding = new Thickness(16, 2, 4, 0), Child = preview };
        Grid.SetColumn(detail, 1);
        body.Children.Add(detail);
        var bottom = new DockPanel { Margin = new Thickness(8, 11, 8, 0) };
        Add(content, bottom, 4);
        hint.Text = L.Text("↑↓  Auswählen     ↵  Öffnen     esc  Ausblenden");
        hint.FontSize = 10;
        hint.Foreground = new SolidColorBrush(Color.FromRgb(139, 140, 151));
        hint.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(hint);
        footer.FontSize = 10;
        footer.Foreground = hint.Foreground;
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        bottom.Children.Add(footer);
        status.TextWrapping = TextWrapping.Wrap;
        status.Foreground = new SolidColorBrush(Color.FromRgb(176, 185, 201));
        status.FontSize = 12;
        settings.Visibility = Visibility.Collapsed;
        settings.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        Grid.SetRow(settings, 1);
        Grid.SetRowSpan(settings, 3);
        content.Children.Add(settings);
        void ScrollActivity(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalChange == 0)
                return;
            foreach (var bar in VisualChildren<System.Windows.Controls.Primitives.ScrollBar>((DependencyObject)sender))
            {
                var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.8, TimeSpan.Zero));
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.8, TimeSpan.FromMilliseconds(550)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, TimeSpan.FromMilliseconds(800)));
                bar.BeginAnimation(OpacityProperty, animation);
            }
        }
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ScrollActivity));
        settings.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ScrollActivity));
        PreviewKeyDown += OnKey;
        InitializeFocusOwnership();
        clipboard.Changed += () => Dispatcher.InvokeAsync(async () => { if (history && IsVisible && !editing) await UpdateResults(); });
        clipboard.Status += message => Dispatcher.InvokeAsync(() => { status.Text = message; if (message.Length > 0) footer.Text = message.Length > 54 ? message[..54] + "…" : message; else footer.Text = L.Text("Nur auf diesem Rechner"); });
    }
    static FontFamily ChooseFont() => new("Segoe UI Variable Text, Segoe UI");
    static void Add(Grid parent, UIElement element, int row)
    {
        Grid.SetRow(element, row);
        parent.Children.Add(element);
    }
    Button MakeButton(string title, Action action, string? tip = null)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 0, 5, 0) };
        System.Windows.Automation.AutomationProperties.SetName(button, tip ?? title);
        ToolTipService.SetIsEnabled(button, false);
        button.Click += (_, _) => action();
        return button;
    }
    static ViewItem HistoryAction() => new(L.Text("Verlauf"), L.Text("Texte, Bilder, Dateien und Ordner"), "", LauncherAction.History, Icons.ClipboardSymbol());
    public void OpenMode(bool clipboardMode, bool captureTarget = true)
    {
        if (IsVisible && !IsEnabled) return;
        DismissConfirmation();
        activating = !previewMode;
        if (captureTarget)
        {
            var foreground = Native.GetForegroundWindow();
            if (foreground != new WindowInteropHelper(this).Handle)
                target = foreground;
        }
        history = clipboardMode;
        editing = false;
        settings.Visibility = Visibility.Collapsed;
        body.Visibility = Visibility.Visible;
        section.Visibility = Visibility.Visible;
        foreach (UIElement child in content.Children)
        if (Grid.GetRow(child) == 1 && child != settings)
            child.Visibility = Visibility.Visible;
        body.ColumnDefinitions[1].Width = new GridLength(history ? 260 : 0);
        Height = history ? 510 : 250;
        navigation.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        contextTitle.Text = L.Text("Verlauf");
        placeholder.Text = history ? L.Text("Verlauf durchsuchen") : L.Text("Suchen …");
        renderedQuery = "\0";
        list.ItemsSource = history ? new List<ViewItem>() : new List<ViewItem> { HistoryAction() };
        list.SelectedIndex = history ? -1 : 0;
        input.Text = "";
        section.Text = history ? L.Text("ZWISCHENABLAGE · LOKAL GESICHERT") : L.Text("APPS, DATEIEN UND EINSTELLUNGEN");
        ToolTipService.SetIsEnabled(input, false);
        footer.Text = L.Text("Nur auf diesem Rechner");
        FitToScreen();
        if (!IsVisible)
        {
            var area = System.Windows.Forms.Screen.FromHandle(target).WorkingArea;
            var scale = VisualTreeHelper.GetDpi(this);
            Left = previewMode ? -5000 : (area.Left + (area.Width - Width * scale.DpiScaleX) / 2) / scale.DpiScaleX;
            Top = previewMode ? -5000 : (area.Top + Math.Min(area.Height * 0.17, 150 * scale.DpiScaleY)) / scale.DpiScaleY;
            Show();
            if (!previewMode)
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110)));
            }
        }
        if (!previewMode)
        {
            Native.Activate(new WindowInteropHelper(this).Handle);
            Activate();
            input.Focus();
            Keyboard.Focus(input);
            Dispatcher.InvokeAsync(() => { if (IsVisible) { Native.Activate(new WindowInteropHelper(this).Handle); input.Focus(); Keyboard.Focus(input); File.WriteAllText(Path.Combine(root, "focus-health.json"), System.Text.Json.JsonSerializer.Serialize(new { ForegroundOwned = Native.GetForegroundWindow() == new WindowInteropHelper(this).Handle, InputFocused = input.IsKeyboardFocusWithin, At = DateTime.UtcNow })); } activating = false; }, System.Windows.Threading.DispatcherPriority.Input);
        }
        _ = UpdateResults();
    }
    async Task UpdateResults()
    {
        if (editing)
            return;
        int current = ++generation;
        queryStop?.Cancel();
        queryStop = new();
        var token = queryStop.Token;
        try
        {
            await Task.Delay(100, token);
            string query = input.Text;
            List<ViewItem> rows;
            if (history)
            {
                var clips = store.All().Where(x => query.Length == 0 || x.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Text.Contains(query, StringComparison.OrdinalIgnoreCase) || x.Files.Any(f => Path.GetFileName(f).Contains(query, StringComparison.OrdinalIgnoreCase))).Take(200).ToList();
                rows = (await Task.WhenAll(clips.Select(async x => { var picture = Thumbnail(x); if (picture == null && x.Files.Length > 0 && IsPicture(x.Files[0])) picture = await Icons.PreviewAsync(x.Files[0]); ImageSource? icon = picture ?? (x.Kind == "files" && x.Files.Length > 0 ? await Icons.ForAsync(new(x.Title, x.Files[0], "Datei", x.Subtitle)) : null); return new ViewItem(x.Title, (x.OriginName.Length > 0 ? L.Format("Von {0} · ", x.OriginName) : "") + x.Subtitle, "", x, icon ?? (x.Kind == "text" ? Icons.TextSymbol() : null), picture); }))).ToList();
            }
            else
                rows = (await Task.WhenAll((await search.Search(query, token)).Select(async x => new ViewItem(x.Title, x.Detail, "", x, await Icons.ForAsync(x))))).ToList();
            if (!history && (query.Length == 0 || SearchService.Rank(L.Text("Verlauf"), query) > 0 || SearchService.Rank(L.Text("Zwischenablage"), query) > 0))
                rows.Insert(0, HistoryAction());
            if (current != generation || token.IsCancellationRequested || editing)
                return;
            object? selected = renderedQuery == query ? (list.SelectedItem as ViewItem)?.Value : null;
            list.ItemsSource = rows;
            int keep = selected == null ? -1 : rows.FindIndex(x => SameValue(x.Value, selected));
            list.SelectedIndex = keep >= 0 ? keep : rows.Count > 0 ? 0 : -1;
            renderedQuery = query;
            if (!history)
                Height = rows.Count == 0 ? 320 : Math.Clamp(154 + Math.Min(rows.Count, 6) * 57, 240, 500);
            FitToScreen();
            section.Text = history ? $"{rows.Count} {(rows.Count == 1 ? L.Text("Eintrag") : L.Text("Einträge"))}{(prefs.Paused ? L.Text(" · Aufnahme pausiert") : "")}" : query.Length == 0 ? L.Text("Vorschläge") : L.Text("Suchergebnisse");
            hint.Text = history ? L.Text("↑↓  Auswählen     ↵  Einfügen     esc  Ausblenden") : L.Text("↑↓  Auswählen     ↵  Öffnen     esc  Ausblenden");
            ShowEmptyState(rows.Count == 0, query);
            if (!history && search.IndexStatus.Length > 0)
                footer.Text = L.Text("Dateiindex nicht verfügbar");
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { footer.Text = L.Text("Suche: ") + e.Message; }
    }
    static bool SameValue(object first, object second) => first is ClipEntry a && second is ClipEntry b ? a.Id == b.Id : first is SearchItem x && second is SearchItem y ? x.Path == y.Path : Equals(first, second);
    static IEnumerable<T> VisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value)
                yield return value;
            foreach (var nested in VisualChildren<T>(child))
                yield return nested;
        }
    }
    static bool IsPicture(string path) => new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".avif" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    internal static ImageSource? Thumbnail(ClipEntry item)
    {
        if (item.Files.Length == 0 || !File.Exists(item.Files[0]) || (item.Kind != "image" && !IsPicture(item.Files[0])))
            return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 480;
            image.UriSource = new Uri(item.Files[0]);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
    void UpdatePreview()
    {
        preview.Children.Clear();
        preview.RowDefinitions.Clear();
        if (!history || list.SelectedItem is not ViewItem row || row.Value is not ClipEntry item)
            return;
        preview.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        preview.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        preview.RowDefinitions.Add(new()
        {
            Height = new GridLength(1, GridUnitType.Star)
        });
        preview.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        preview.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        Add(preview, new TextBlock { Text = row.Preview != null ? L.Text("Bildvorschau") : item.Kind == "files" ? L.Text("Dateien") : L.Text("Textvorschau"), FontSize = 13, FontWeight = FontWeights.Medium, Foreground = new SolidColorBrush(Color.FromRgb(226, 226, 233)) }, 0);
        Add(preview, new TextBlock { Text = $"{ClipEntry.FormatBytes(item.Bytes)}  ·  {item.Created.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)}{(item.Pinned ? L.Text("  ·  Angeheftet") : "")}", FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(142, 143, 154)), Margin = new Thickness(0, 5, 0, 16) }, 1);
        var document = new StackPanel();
        if (row.Preview != null)
            document.Children.Add(new Image { Source = row.Preview, MaxHeight = 180, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 12) });
        else if (row.Thumbnail != null && item.Kind != "text")
            document.Children.Add(new Image { Source = row.Thumbnail, Width = 38, Height = 38, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
        document.Children.Add(new TextBlock { Text = item.Kind == "text" ? (item.Text.Length > 6000 ? item.Text[..6000] + "…" : item.Text) : string.Join("\n", item.Files.Take(20).Select(Path.GetFileName)), TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = new SolidColorBrush(Color.FromRgb(225, 225, 231)) });
        var scroll = new ScrollViewer { Content = document, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 7, 0) };
        Add(preview, scroll, 2);
        var actions = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        actions.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(1, GridUnitType.Star)
        });
        actions.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(34)
        });
        actions.ColumnDefinitions.Add(new()
        {
            Width = new GridLength(34)
        });
        var copy = MakeButton(L.Text("Kopieren"), async () => await Copy(item, false));
        copy.Background = new SolidColorBrush(Color.FromArgb(35, 10, 132, 255));
        copy.Foreground = new SolidColorBrush(Color.FromRgb(116, 184, 255));
        copy.FontSize = 12;
        copy.Padding = new Thickness(9, 8, 9, 8);
        actions.Children.Add(copy);
        Button IconButton(string path, string name, Action action, bool selected = false)
        {
            var button = MakeButton("", action);
            button.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = new SolidColorBrush(selected ? Color.FromRgb(91, 171, 255) : Color.FromRgb(166, 168, 180)), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.Uniform, Width = 16, Height = 16 };
            button.Background = Brushes.Transparent;
            button.Padding = new Thickness(7);
            button.Margin = new Thickness(0);
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            return button;
        }
        var pin = IconButton("M8,2 L16,2 15,9 19,13 19,15 5,15 5,13 9,9 Z M12,15 L12,22", item.Pinned ? L.Text("Loslösen") : L.Text("Anheften"), () => { store.Pin(item); _ = UpdateResults(); }, item.Pinned);
        Grid.SetColumn(pin, 1);
        actions.Children.Add(pin);
        var trash = IconButton("M4,6 L20,6 M9,6 L9,3 15,3 15,6 M7,6 L8,21 16,21 17,6 M10,10 L10,17 M14,10 L14,17", L.Text("Eintrag löschen"), () => { store.Delete(item); _ = UpdateResults(); });
        Grid.SetColumn(trash, 2);
        actions.Children.Add(trash);
        Add(preview, actions, 3);
        Add(preview, new TextBlock { Text = L.Text("↵  Direkt einfügen"), FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(133, 134, 147)), Margin = new Thickness(0, 11, 0, 2) }, 4);
    }
    async void OnKey(object sender, KeyEventArgs e)
    {
        if (Shortcuts?.Recording == true)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                DismissConfirmation();
            }
            else
                Shortcuts.Capture(e);
            return;
        }
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            if (DismissConfirmation())
                return;
            Hide();
            return;
        }
        if (editing || Keyboard.FocusedElement is Button)
            return;
        if (e.Key == Key.Down || e.Key == Key.Up)
        {
            e.Handled = true;
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, list.Items.Count - 1);
                list.ScrollIntoView(list.SelectedItem);
            }
            return;
        }
        if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
        {
            e.Handled = true;
            await ExecuteSelected();
        }
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            OpenMode(!history, false);
        }
        if (history && Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Delete && list.SelectedItem is ViewItem row && row.Value is ClipEntry clip)
        {
            e.Handled = true;
            store.Delete(clip);
            await UpdateResults();
        }
    }
    async Task ExecuteSelected()
    {
        if (list.SelectedItem is not ViewItem row)
            return;
        try
        {
            if (row.Value is LauncherAction.History)
            {
                OpenMode(true, false);
                return;
            }
            if (row.Value is SearchItem app)
            {
                Hide();
                search.Open(app);
            }
            else if (row.Value is ClipEntry clip)
                await Copy(clip, true);
        }
        catch (Exception e) { footer.Text = e.Message; }
    }
    async Task Copy(ClipEntry clip, bool paste)
    {
        try
        {
            Hide();
            bool inserted = await clipboard.Restore(clip, target, paste);
            if (paste && !inserted)
                Program.Notify(L.Text("In Zwischenablage kopiert · mit Strg + V einfügen"));
        }
        catch (Exception e) { OpenMode(true, false); footer.Text = e.Message; }
    }
    void Save() => prefs.Save(root);
    public async Task RenderSettingsPreview(string path, int tab = 0)
    {
        settingsTab = tab;
        previewMode = true;
        ShowActivated = false;
        OpenMode(false, false);
        ShowSettings();
        await Task.Delay(200);
        Measure(new Size(Width, Height));
        Arrange(new Rect(0, 0, Width, Height));
        UpdateLayout();
        var image = new RenderTargetBitmap((int)Width * 2, (int)Height * 2, 192, 192, PixelFormats.Pbgra32);
        image.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var output = File.Create(path);
        encoder.Save(output);
    }
    public async Task<object> TestFocus()
    {
        nint previous = Native.GetForegroundWindow();
        using var peer = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--focus-test-peer") { UseShellExecute = false })!;
        peer.WaitForInputIdle(5000); peer.Refresh();
        for (int wait = 0; wait < 40 && peer.MainWindowHandle == 0 && !peer.HasExited; wait++) { await Task.Delay(50); peer.Refresh(); }
        var peerWindow = peer.MainWindowHandle;
        if (peerWindow == 0) throw new IOException("Focus fixture did not open");
        try
        {
            Native.Activate(peerWindow);
            OpenMode(false);
            await Task.Delay(250);
            var hwnd = new WindowInteropHelper(this).Handle;
            bool initial = IsVisible && Native.GetForegroundWindow() == hwnd && input.IsKeyboardFocusWithin;
            bool recovered = true;
            for (int i = 0; i < 3; i++)
            { Native.Activate(peerWindow); await Task.Delay(350); recovered &= IsVisible && Native.GetForegroundWindow() == hwnd && input.IsKeyboardFocusWithin; }
            ShowSettings(); Native.Activate(peerWindow); await Task.Delay(350);
            bool settingsFocus = IsVisible && Native.GetForegroundWindow() == hwnd && IsKeyboardFocusWithin && Keyboard.FocusedElement is UIElement { IsVisible: true };
            var field = new TextBox { Text = "Eigener Dialog" };
            var dialog = new Window { Title = "Kopra Dialogtest", Owner = this, Width = 220, Height = 90, Content = field };
            dialog.Show(); Native.Activate(new WindowInteropHelper(dialog).Handle); field.Focus(); Keyboard.Focus(field);
            await Task.Delay(350);
            bool dialogPreserved = dialog.IsActive && field.IsKeyboardFocusWithin;
            dialog.Close(); await Task.Delay(350);
            bool dialogReturned = IsVisible && Native.GetForegroundWindow() == hwnd && IsKeyboardFocusWithin;
            Hide(); Native.Activate(peerWindow); await Task.Delay(350);
            bool hiddenReleased = !IsVisible && Native.GetForegroundWindow() == peerWindow;
            return new { InitialFocus = initial, RecoversAfterThreeFocusLosses = recovered, SettingsFocused = settingsFocus, OwnedDialogKeepsFocus = dialogPreserved, FocusReturnsAfterDialog = dialogReturned, HiddenReleasesFocus = hiddenReleased, NativeFramePresent = Native.HasNativeFrame(hwnd), Passed = initial && recovered && settingsFocus && dialogPreserved && dialogReturned && hiddenReleased };
        }
        finally { Hide(); if (!peer.HasExited) { peer.CloseMainWindow(); if (!peer.WaitForExit(1000)) peer.Kill(); } Native.SetForegroundWindow(previous); }
    }
    public async Task<object> TestInteraction()
    {
        previewMode = true;
        ShowActivated = false;
        OpenMode(false, false);
        bool immediate = list.SelectedItem is ViewItem { Value: LauncherAction.History };
        await UpdateResults();
        bool first = list.SelectedItem is ViewItem { Value: LauncherAction.History };
        await ExecuteSelected();
        await UpdateResults();
        bool entered = history && IsVisible;
        int start = list.SelectedIndex;
        void Key(Key key) => RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Key(System.Windows.Input.Key.Down);
        bool down = list.SelectedIndex == Math.Min(start + 1, list.Items.Count - 1);
        Key(System.Windows.Input.Key.Up);
        bool up = list.SelectedIndex == start;
        var tabEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, System.Windows.Input.Key.Tab) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        RaiseEvent(tabEvent);
        bool tabNavigates = !tabEvent.Handled && history;
        ShowSettings();
        bool settingsExclusive = settings.IsVisible && !body.IsVisible;
        bool tabs = true;
        for (int i = 0; i < 3; i++)
        {
            settingsTab = i;
            ShowSettings();
            UpdateLayout();
            tabs &= settings.Content is StackPanel { Children.Count: > 3 };
        }
        settingsTab = 1;
        advancedSettings = true;
        ShowSettings();
        UpdateLayout();
        bool noTyping = !VisualChildren<TextBox>(settings).Any();
        long chosen = 7;
        ChooseSetting("Aufbewahrung", chosen, [(7, L.Text("1 Woche")), (30, L.Text("1 Monat"))], x => chosen = x);
        Key(System.Windows.Input.Key.Escape);
        bool choiceCancelled = chosen == 7 && confirmation == null;
        ChooseSetting("Aufbewahrung", chosen, [(7, L.Text("1 Woche")), (30, L.Text("1 Monat"))], x => chosen = x);
        UpdateLayout();
        var month = VisualChildren<Button>(confirmation!).First(x => x.Content is string text && text.Contains(L.Text("1 Monat")));
        month.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        bool choiceApplied = chosen == 30 && confirmation == null;
        Confirm("Testbestätigung", "Diese Vorschau löscht keine Daten.", () => Task.CompletedTask);
        Key(System.Windows.Input.Key.Escape);
        bool cancelSheet = confirmation == null && IsVisible;
        OpenMode(true, false);
        await UpdateResults();
        input.Text = "No-result-" + Guid.NewGuid();
        await UpdateResults();
        bool empty = emptyState.IsVisible;
        OpenMode(false, false); input.Text = "No-result-" + Guid.NewGuid(); await UpdateResults(); UpdateLayout();
        bool emptyFits = VisualChildren<FrameworkElement>(emptyState).Where(x => x is Image or TextBlock).All(x => { var bounds = x.TransformToAncestor(emptyState).TransformBounds(new Rect(x.RenderSize)); return bounds.Top >= 0 && bounds.Bottom <= emptyState.ActualHeight + 1; });
        Key(System.Windows.Input.Key.Escape);
        bool hidden = !IsVisible;
        return new
        {
            ImmediateHistorySelection = immediate,
            HistoryFirst = first,
            EnterOpensHistory = entered,
            ArrowDown = down,
            ArrowUp = up,
            TabKeepsMode = tabNavigates,
            SettingsExclusive = settingsExclusive,
            SettingsTabs = tabs,
            NoNumericTyping = noTyping,
            ChoiceCancelKeepsValue = choiceCancelled,
            ChoiceAppliesValue = choiceApplied,
            EscapeCancelsSheet = cancelSheet,
            EmptyState = empty,
            EmptyStateFits = emptyFits,
            EscapeHides = hidden,
            Passed = immediate && first && entered && down && up && tabNavigates && settingsExclusive && tabs && noTyping && choiceCancelled && choiceApplied && cancelSheet && empty && emptyFits && hidden
        };
    }
    public async Task RenderPreview(string path, bool clipboardMode, int selected = 0)
    {
        previewMode = true;
        ShowActivated = false;
        OpenMode(clipboardMode, false);
        await UpdateResults();
        if (selected < list.Items.Count)
            list.SelectedIndex = selected;
        Measure(new Size(Width, Height));
        Arrange(new Rect(0, 0, Width, Height));
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Width * 2, (int)Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
