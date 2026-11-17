using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

internal static class DrawAssets
{
    private static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath(); float d = r * 2;
        path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90); path.AddArc(x, y + h - d, d, d, 90, 90); path.CloseFigure();
        return path;
    }

    // Simplified mark for 16/20/24 px, where the full artwork's trails collapse into noise.
    private static Bitmap Simplified(int size)
    {
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image))
        using (var tile = Rounded(0, 0, 64, 64, 14))
        using (var background = new SolidBrush(Color.FromArgb(39, 39, 39)))
        using (var white = new SolidBrush(Color.White))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 64f, size / 64f);
            g.FillPath(background, tile);
            PointF[] cursor = {
                new PointF(14, 10), new PointF(14, 47), new PointF(25, 37), new PointF(31, 53),
                new PointF(39, 49), new PointF(33, 34), new PointF(48, 34)
            };
            g.FillPolygon(white, cursor);
        }
        return image;
    }

    private static Bitmap Resize(Image source, int size)
    {
        var image = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(image))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);
            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            float width = source.Width * scale, height = source.Height * scale;
            g.DrawImage(source, (size - width) / 2, (size - height) / 2, width, height);
        }
        return image;
    }

    public static int Main(string[] args)
    {
        string folder = args[0];
        Directory.CreateDirectory(folder);
        string sourcePath = Path.Combine(folder, "logo-source.png");
        if (!File.Exists(sourcePath))
        {
            Console.Error.WriteLine("Missing logo source: " + sourcePath);
            return 1;
        }
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        var data = new List<byte[]>();
        using (var source = new Bitmap(sourcePath))
        {
            foreach (int size in sizes)
            {
                using (Bitmap bitmap = size <= 24 ? Simplified(size) : Resize(source, size))
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    data.Add(stream.ToArray());
                    if (size == 256) bitmap.Save(Path.Combine(folder, "logo.png"), ImageFormat.Png);
                }
            }
        }
        using (var file = File.Create(Path.Combine(folder, "app.ico")))
        using (var writer = new BinaryWriter(file))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(data[i].Length); writer.Write(offset);
                offset += data[i].Length;
            }
            foreach (byte[] bytes in data) writer.Write(bytes);
        }
        return 0;
    }
}
