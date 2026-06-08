using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace Autocorrect.App;

// Turns images or GIFs into tight, transparent pet frames by removing the solid background and cropping padding.
public static class PetImageProcessor
{
    public static string ProcessToTransparentPng(string sourcePath, int tolerance = 48)
    {
        using var source = new Bitmap(sourcePath);
        var frame = ToArgbKnockout(source, tolerance);
        try
        {
            return CropAndSave(new List<Bitmap> { frame })[0];
        }
        finally
        {
            frame.Dispose();
        }
    }

    // Splits an animated GIF into transparent, padding-trimmed frames and reports the average frame delay.
    public static List<string> ProcessGifToFrames(string gifPath, out int frameIntervalMs, int tolerance = 48)
    {
        using var gif = Image.FromFile(gifPath);
        var dimension = new FrameDimension(gif.FrameDimensionsList[0]);
        var frameCount = gif.GetFrameCount(dimension);
        frameIntervalMs = ReadGifDelayMs(gif);

        var frames = new List<Bitmap>();
        try
        {
            for (var i = 0; i < frameCount; i++)
            {
                gif.SelectActiveFrame(dimension, i);
                using var raw = new Bitmap(gif);
                frames.Add(ToArgbKnockout(raw, tolerance));
            }

            return CropAndSave(frames);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    // Processes a folder of PNG frames into a consistent, padding-trimmed sequence.
    public static List<string> ProcessFolderToFrames(string folder, int tolerance = 48)
    {
        var files = Directory.GetFiles(folder, "*.png").OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
        var frames = new List<Bitmap>();
        try
        {
            foreach (var file in files)
            {
                using var raw = new Bitmap(file);
                frames.Add(ToArgbKnockout(raw, tolerance));
            }

            return frames.Count == 0 ? new List<string>() : CropAndSave(frames);
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private static Bitmap ToArgbKnockout(Image source, int tolerance)
    {
        var argb = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(argb))
        {
            graphics.DrawImage(source, 0, 0, source.Width, source.Height);
        }

        KnockOutBackground(argb, tolerance);
        return argb;
    }

    // Crops every frame to the shared content bounds (so the sequence never jitters) and writes PNGs.
    private static List<string> CropAndSave(List<Bitmap> frames)
    {
        var bounds = UnionContentBounds(frames);
        Directory.CreateDirectory(SettingsRepository.DataDirectory);
        var paths = new List<string>();
        foreach (var frame in frames)
        {
            using var cropped = frame.Clone(bounds, PixelFormat.Format32bppArgb);
            var target = Path.Combine(SettingsRepository.DataDirectory, $"pet-{Guid.NewGuid():N}.png");
            cropped.Save(target, ImageFormat.Png);
            paths.Add(target);
        }

        return paths;
    }

    private static int ReadGifDelayMs(Image gif)
    {
        try
        {
            var raw = gif.GetPropertyItem(0x5100)?.Value;
            if (raw is { Length: >= 4 })
            {
                var sum = 0;
                var counted = 0;
                for (var i = 0; i + 4 <= raw.Length; i += 4)
                {
                    var centiseconds = BitConverter.ToInt32(raw, i);
                    if (centiseconds > 0)
                    {
                        sum += centiseconds;
                        counted++;
                    }
                }

                if (counted > 0)
                {
                    return Math.Clamp((int)Math.Round(sum / (double)counted * 10), 40, 400);
                }
            }
        }
        catch
        {
            // Fall through to a sensible default.
        }

        return 90;
    }

    // Flood-fills the border background color inward, preserving interior detail of the same color.
    private static void KnockOutBackground(Bitmap bitmap, int tolerance)
    {
        int width = bitmap.Width, height = bitmap.Height;
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var stride = Math.Abs(data.Stride);
        var length = stride * height;
        var buffer = new byte[length];
        Marshal.Copy(data.Scan0, buffer, 0, length);

        var (bgB, bgG, bgR) = SampleBackground(buffer, stride, width, height);
        var visited = new bool[width * height];
        var stack = new Stack<int>();

        bool IsBackground(int x, int y)
        {
            var offset = y * stride + x * 4;
            return Math.Abs(buffer[offset] - bgB) <= tolerance &&
                   Math.Abs(buffer[offset + 1] - bgG) <= tolerance &&
                   Math.Abs(buffer[offset + 2] - bgR) <= tolerance;
        }

        void Seed(int x, int y)
        {
            var pixel = y * width + x;
            if (!visited[pixel] && IsBackground(x, y))
            {
                visited[pixel] = true;
                stack.Push(pixel);
            }
        }

        for (var x = 0; x < width; x++)
        {
            Seed(x, 0);
            Seed(x, height - 1);
        }

        for (var y = 0; y < height; y++)
        {
            Seed(0, y);
            Seed(width - 1, y);
        }

        while (stack.Count > 0)
        {
            var pixel = stack.Pop();
            var x = pixel % width;
            var y = pixel / width;
            buffer[y * stride + x * 4 + 3] = 0;

            if (x > 0)
            {
                Seed(x - 1, y);
            }

            if (x < width - 1)
            {
                Seed(x + 1, y);
            }

            if (y > 0)
            {
                Seed(x, y - 1);
            }

            if (y < height - 1)
            {
                Seed(x, y + 1);
            }
        }

        Marshal.Copy(buffer, 0, data.Scan0, length);
        bitmap.UnlockBits(data);
    }

    // Finds the smallest rectangle that contains every opaque pixel across all frames, with a little padding.
    private static Rectangle UnionContentBounds(List<Bitmap> frames)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var frame in frames)
        {
            var rect = new Rectangle(0, 0, frame.Width, frame.Height);
            var data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var stride = Math.Abs(data.Stride);
            var length = stride * frame.Height;
            var buffer = new byte[length];
            Marshal.Copy(data.Scan0, buffer, 0, length);
            frame.UnlockBits(data);

            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    if (buffer[y * stride + x * 4 + 3] > 16)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
        }

        var width = frames[0].Width;
        var height = frames[0].Height;
        if (maxX < minX)
        {
            return new Rectangle(0, 0, width, height);
        }

        const int pad = 6;
        minX = Math.Max(0, minX - pad);
        minY = Math.Max(0, minY - pad);
        maxX = Math.Min(width - 1, maxX + pad);
        maxY = Math.Min(height - 1, maxY + pad);
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // Averages the four corners (BGRA byte order) to estimate the background color.
    private static (int B, int G, int R) SampleBackground(byte[] buffer, int stride, int width, int height)
    {
        var corners = new[]
        {
            0,
            (width - 1) * 4,
            (height - 1) * stride,
            (height - 1) * stride + (width - 1) * 4
        };

        int b = 0, g = 0, r = 0;
        foreach (var offset in corners)
        {
            b += buffer[offset];
            g += buffer[offset + 1];
            r += buffer[offset + 2];
        }

        return (b / corners.Length, g / corners.Length, r / corners.Length);
    }
}
