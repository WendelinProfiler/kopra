using System.Runtime.InteropServices;
using System.Windows.Interop;
namespace Kopra;

static class Icons
{
    public static ImageSource SearchSymbol()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(174,174,184)), 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(11,11),9,9)));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(18,18),new Point(27,27))));
        var image = new DrawingImage(group); image.Freeze(); return image;
    }
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out nint bitmap);
    }
    [StructLayout(LayoutKind.Sequential)]
    struct NativeSize
    {
        public int width, height;
    }
    [DllImport("shell32", CharSet = CharSet.Unicode, PreserveSig = true)] static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out ImageFactory? factory);
    [DllImport("gdi32")] static extern bool DeleteObject(nint bitmap);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info
    {
        public nint icon; public int index; public uint attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string type;
    }
    [DllImport("shell32", CharSet = CharSet.Unicode)] static extern nint SHGetFileInfo(string path, uint attributes, out Info info, uint size, uint flags);
    [DllImport("user32")] static extern bool DestroyIcon(nint icon);
    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ImageSource?>> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly SemaphoreSlim extraction = new(3, 3);
    public static Task<ImageSource?> PreviewAsync(string path) => cache.GetOrAdd("preview:" + path, _ => Task.Run(async () => { await extraction.WaitAsync(); try { return Extract(path, true); } finally { extraction.Release(); } }));
    public static Task<ImageSource?> ForAsync(SearchItem item)
    {
        string path = item.Kind == "Einstellung" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "ImmersiveControlPanel", "SystemSettings.exe") : item.Path;
        return cache.GetOrAdd(path, key => Task.Run(async () => { await extraction.WaitAsync(); try { return Extract(key); } finally { extraction.Release(); } }));
    }
    static ImageSource? Extract(string path, bool preview = false)
    {
        ImageSource? image = null;
        ImageFactory? factory = null;
        try
        {
            var iid = typeof(ImageFactory).GUID;
            if (SHCreateItemFromParsingName(path, 0, ref iid, out factory) >= 0 && factory != null && factory.GetImage(new NativeSize { width = preview ? 480 : 96, height = preview ? 480 : 96 }, preview ? 9u : 5u, out nint bitmap) >= 0 && bitmap != 0)
            {
                try
                {
                    image = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    image.Freeze();
                }
                finally { DeleteObject(bitmap); }
            }
            if (image == null && !preview && !path.StartsWith("shell:"))
            {
                SHGetFileInfo(path, 0, out var info, (uint)Marshal.SizeOf<Info>(), 0x100);
                if (info.icon != 0)
                {
                    try
                    {
                        image = Imaging.CreateBitmapSourceFromHIcon(info.icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                        image.Freeze();
                    }
                    finally { DestroyIcon(info.icon); }
                }
            }
        }
        catch (COMException) { }
        finally { if (factory != null) Marshal.FinalReleaseComObject(factory); }
        return image;
    }
    public static ImageSource Symbol(string kind)
    {
        string path = kind switch
        {
            "Ordner" => "M3,7 L11,7 14,10 29,10 29,26 3,26 Z",
            "Einstellung" => "M16,3 L19,3 20,7 23,9 27,8 29,12 26,15 26,18 29,21 27,25 23,24 20,26 19,30 14,30 13,26 10,24 6,25 4,21 7,18 7,15 4,12 6,8 10,9 13,7 14,3 Z M21,16 A5,5 0 1 1 11,16 A5,5 0 1 1 21,16",
            "App" => "M5,5 L13,5 13,13 5,13 Z M19,5 L27,5 27,13 19,13 Z M5,19 L13,19 13,27 5,27 Z M19,19 L27,19 27,27 19,27 Z",
            _ => "M8,3 L20,3 27,10 27,29 8,29 Z M20,3 L20,11 27,11 M12,17 L23,17 M12,22 L23,22"
        };
        var drawing = new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromRgb(205, 209, 220)), 1.6), Geometry.Parse(path));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
    public static ImageSource TextSymbol()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(194, 196, 204)), 1.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var drawing = new GeometryDrawing(null, pen, Geometry.Parse("M5,7 L27,7 M5,13 L27,13 M5,19 L27,19 M5,25 L19,25"));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
    // Lucide settings.svg, ISC license: assets/icons/LUCIDE-LICENSE.txt.
    public static ImageSource SettingsSymbol()
    {
        using var stream = typeof(Icons).Assembly.GetManifestResourceStream("Kopra.assets.icons.settings.svg")!;
        var svg = System.Xml.Linq.XDocument.Load(stream);
        var ns = svg.Root!.Name.Namespace;
        var geometry = new GeometryGroup();
        geometry.Children.Add(Geometry.Parse(svg.Root.Element(ns + "path")!.Attribute("d")!.Value));
        geometry.Children.Add(new EllipseGeometry(new Point(12, 12), 3, 3));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(174, 174, 184)), 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 24, 24))));
        group.Children.Add(new GeometryDrawing(null, pen, geometry));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
    public static ImageSource ClipboardSymbol()
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(228, 231, 238)), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        group.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse("M10,6 L6,6 6,29 27,29 27,6 23,6 M12,3 L21,3 21,9 12,9 Z M11,15 L22,15 M11,20 L22,20 M11,25 L18,25")));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
