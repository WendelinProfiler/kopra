using System.Windows.Media.Imaging;
namespace Kopra;

static class AppIconGenerator
{
    public static void Generate(string folder)
    {
        Directory.CreateDirectory(folder);
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256, 512, 1024 };
        var images = new Dictionary<int, byte[]>();
        foreach (int size in sizes)
        {
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.PushTransform(new ScaleTransform(size / 1024d, size / 1024d));
                var tile = new LinearGradientBrush(Color.FromRgb(70, 147, 255), Color.FromRgb(19, 77, 210), 90);
                draw.DrawRoundedRectangle(tile, null, new Rect(16, 16, 992, 992), 218, 218);
                var shine = new LinearGradientBrush(Color.FromArgb(50, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
                draw.DrawRoundedRectangle(shine, new Pen(new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), 9), new Rect(24, 24, 976, 976), 210, 210);
                var white = new SolidColorBrush(Color.FromRgb(250, 252, 255));
                var pen = new Pen(white, 62) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                draw.DrawEllipse(new SolidColorBrush(Color.FromArgb(16, 255, 255, 255)), pen, new Point(443, 430), 205, 205);
                draw.DrawLine(pen, new Point(592, 580), new Point(780, 768));
                if (size >= 32)
                {
                    var line = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 33) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                    draw.DrawLine(line, new Point(355, 398), new Point(526, 398));
                    draw.DrawLine(line, new Point(355, 467), new Point(481, 467));
                }
                draw.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var data = new MemoryStream();
            png.Save(data);
            images[size] = data.ToArray();
            File.WriteAllBytes(Path.Combine(folder, $"icon-{size}.png"), images[size]);
        }
        var icoSizes = sizes.Where(x => x <= 256).ToArray();
        using var stream = File.Create(Path.Combine(folder, "Kopra.ico"));
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)icoSizes.Length);
        int offset = 6 + icoSizes.Length * 16;
        foreach (int size in icoSizes)
        {
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)(size == 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[size].Length);
            writer.Write(offset);
            offset += images[size].Length;
        }
        foreach (int size in icoSizes)
            writer.Write(images[size]);
    }
}
